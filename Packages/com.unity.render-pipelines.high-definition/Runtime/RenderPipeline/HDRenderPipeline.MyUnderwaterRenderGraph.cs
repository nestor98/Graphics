using System;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.VFX;
using UnityEngine.Rendering.RendererUtils;

// Resove the ambiguity in the RendererList name (pick the in-engine version)
using RendererList = UnityEngine.Rendering.RendererList;
using RendererListDesc = UnityEngine.Rendering.RendererUtils.RendererListDesc;

namespace UnityEngine.Rendering.HighDefinition
{
    public partial class HDRenderPipeline
    {

        public OceanData oceanData; 
        public bool doRGBRendering = false;

        void RecordUnderwaterRenderGraph(RenderRequest renderRequest,
            AOVRequestData aovRequest,
            List<RTHandle> aovBuffers,
            List<RTHandle> aovCustomPassBuffers,
            ScriptableRenderContext renderContext,
            CommandBuffer commandBuffer)
        {
            // return;
            // Debug.Log("In underwater render graph");


            using (new ProfilingScope(commandBuffer, ProfilingSampler.Get(HDProfileId.RecordUnderwaterRenderGraph)))
            {
                var hdCamera = renderRequest.hdCamera;
                var camera = hdCamera.camera;
                var cullingResults = renderRequest.cullingResults.cullingResults;
                var customPassCullingResults = renderRequest.cullingResults.customPassCullingResults ?? cullingResults;
                bool msaa = hdCamera.msaaEnabled;
                var target = renderRequest.target;

                // BlitFinalCameraTexture(m_RenderGraph, hdCamera, prepassOutput.resolvedDepthBuffer, m_RenderGraph.ImportTexture(target.targetDepth), uiBuffer, afterPostProcessBuffer, viewIndex, outputsToHDR: false, cubemapFace: target.face);

                // HDUtils.DrawFullScreen(context.cmd, data.viewport, data.blitMaterial, data.destination, data.cubemapFace, propertyBlock, 0, data.dstTexArraySlice);
                // var srcTexArraySlice = -1;
                // var viewport = hdCamera.finalViewport;
                // var blitMaterial = HDUtils.GetBlitMaterial(TextureXR.useTexArray ? TextureDimension.Tex2DArray : TextureDimension.Tex2D, singleSlice: srcTexArraySlice >= 0);

                // var destination = todo;
                // var cubemapFace = todo;
                // var propertyBlock = todo;
                // var dstTexArraySlice = -1;

                // HDUtils.DrawFullScreen(renderContext.cmd, viewport, blitMaterial, destination, cubemapFace, propertyBlock, 0, dstTexArraySlice);


                //Set resolution group for the entire frame
                SetCurrentResolutionGroup(m_RenderGraph, hdCamera, ResolutionGroup.BeforeDynamicResUpscale);

                // Caution: We require sun light here as some skies use the sun light to render, it means that UpdateSkyEnvironment must be called after PrepareLightsForGPU.
                // TODO: Try to arrange code so we can trigger this call earlier and use async compute here to run sky convolution during other passes (once we move convolution shader to compute).
                if (!m_CurrentDebugDisplaySettings.IsMatcapViewEnabled(hdCamera))
                    m_SkyManager.UpdateEnvironment(m_RenderGraph, hdCamera, GetMainLight(), m_CurrentDebugDisplaySettings);

                // We need to initialize the MipChainInfo here, so it will be available to any render graph pass that wants to use it during setup
                // Be careful, ComputePackedMipChainInfo needs the render texture size and not the viewport size. Otherwise it would compute the wrong size.
                hdCamera.depthBufferMipChainInfo.ComputePackedMipChainInfo(RTHandles.rtHandleProperties.currentRenderTargetSize);

                // Bind the depth pyramid offset info for the HDSceneDepth node in ShaderGraph. This can be used by users in custom passes.
                commandBuffer.SetGlobalBuffer(HDShaderIDs._DepthPyramidMipLevelOffsets, hdCamera.depthBufferMipChainInfo.GetOffsetBufferData(m_DepthPyramidMipLevelOffsetsBuffer));

#if UNITY_EDITOR
                var showGizmos = camera.cameraType == CameraType.Game
                    || camera.cameraType == CameraType.SceneView;
#endif

                // Set the default color buffer format for full screen debug rendering
                GraphicsFormat fullScreenDebugFormat = GraphicsFormat.R16G16B16A16_SFloat;
                
                m_ShouldOverrideColorBufferFormat = false;

                UpdateParentExposure(m_RenderGraph, hdCamera);

                TextureHandle backBuffer = m_RenderGraph.ImportBackbuffer(target.id);
                TextureHandle colorBuffer = CreateColorBuffer(m_RenderGraph, hdCamera, msaa);
                m_NonMSAAColorBuffer = CreateColorBuffer(m_RenderGraph, hdCamera, false);
                TextureHandle currentColorPyramid = m_RenderGraph.ImportTexture(hdCamera.GetCurrentFrameRT((int)HDCameraFrameHistoryType.ColorBufferMipChain));
                TextureHandle rayCountTexture = RayCountManager.CreateRayCountTexture(m_RenderGraph);



                // NEw for UW + sky:
                
                TextureHandle skyColorBuffer = CreateColorBuffer(m_RenderGraph, hdCamera, msaa);
                // DrawFullScreen();
                
#if ENABLE_VIRTUALTEXTURES
                TextureHandle vtFeedbackBuffer = VTBufferManager.CreateVTFeedbackBuffer(m_RenderGraph, hdCamera.msaaSamples);
                bool resolveVirtualTextureFeedback = true;
#else
                TextureHandle vtFeedbackBuffer = TextureHandle.nullHandle;
#endif

                // Evaluate the ray tracing acceleration structure debug views
                EvaluateRTASDebugView(m_RenderGraph, hdCamera);

                LightingBuffers lightingBuffers = new LightingBuffers();
                lightingBuffers.diffuseLightingBuffer = CreateDiffuseLightingBuffer(m_RenderGraph, hdCamera.msaaSamples);
                lightingBuffers.sssBuffer = CreateSSSBuffer(m_RenderGraph, hdCamera, hdCamera.msaaSamples);

                var prepassOutput = RenderPrepass(m_RenderGraph, colorBuffer, lightingBuffers.sssBuffer, vtFeedbackBuffer, cullingResults, customPassCullingResults, hdCamera, aovRequest, aovBuffers);

                // Need this during debug render at the end outside of the main loop scope.
                // Once render graph move is implemented, we can probably remove the branch and this.
                ShadowResult shadowResult = new ShadowResult();
                BuildGPULightListOutput gpuLightListOutput = new BuildGPULightListOutput();
                TextureHandle uiBuffer = m_RenderGraph.defaultResources.blackTextureXR;
                TextureHandle sunOcclusionTexture = m_RenderGraph.defaultResources.whiteTexture;

                if (m_CurrentDebugDisplaySettings.IsDebugDisplayEnabled() && m_CurrentDebugDisplaySettings.IsFullScreenDebugPassEnabled())
                {
                    // Stop Single Pass is after post process.
                    StartXRSinglePass(m_RenderGraph, hdCamera);

                    RenderFullScreenDebug(m_RenderGraph, colorBuffer, prepassOutput.depthBuffer, cullingResults, hdCamera);

#if ENABLE_VIRTUALTEXTURES
                    resolveVirtualTextureFeedback = false; // Could be handled but not needed for fullscreen debug pass currently
#endif
                }
                else if (m_CurrentDebugDisplaySettings.IsDebugMaterialDisplayEnabled() || m_CurrentDebugDisplaySettings.IsMaterialValidationEnabled() || CoreUtils.IsSceneLightingDisabled(hdCamera.camera))
                {
                    gpuLightListOutput = BuildGPULightList(m_RenderGraph, hdCamera, m_TileAndClusterData, m_TotalLightCount, ref m_ShaderVariablesLightListCB, prepassOutput.depthBuffer, prepassOutput.stencilBuffer, prepassOutput.gbuffer);

                    // For alpha output in AOVs or debug views, in case we have a shadow matte material, we need to render the shadow maps
                    if (m_CurrentDebugDisplaySettings.data.materialDebugSettings.debugViewMaterialCommonValue == Attributes.MaterialSharedProperty.Alpha)
                        RenderShadows(m_RenderGraph, hdCamera, cullingResults, ref shadowResult);
                    else
                        HDShadowManager.BindDefaultShadowGlobalResources(m_RenderGraph);

                    // Stop Single Pass is after post process.
                    StartXRSinglePass(m_RenderGraph, hdCamera);

                    colorBuffer = RenderDebugViewMaterial(m_RenderGraph, cullingResults, hdCamera, gpuLightListOutput, prepassOutput.dbuffer, prepassOutput.gbuffer, prepassOutput.depthBuffer, vtFeedbackBuffer);
                    colorBuffer = ResolveMSAAColor(m_RenderGraph, hdCamera, colorBuffer);
                }
                else if (hdCamera.frameSettings.IsEnabled(FrameSettingsField.RayTracing) && hdCamera.volumeStack.GetComponent<PathTracing>().enable.value && hdCamera.camera.cameraType != CameraType.Preview && GetRayTracingState() && GetRayTracingClusterState())
                {
                    //// We only request the light cluster if we are gonna use it for debug mode
                    //if (FullScreenDebugMode.LightCluster == m_CurrentDebugDisplaySettings.data.fullScreenDebugMode && GetRayTracingClusterState())
                    //{
                    //    HDRaytracingLightCluster lightCluster = RequestLightCluster();
                    //    lightCluster.EvaluateClusterDebugView(cmd, hdCamera);
                    //}

                    if (hdCamera.viewCount == 1)
                    {
                        colorBuffer = RenderPathTracing(m_RenderGraph, hdCamera, colorBuffer);
                    }
                    else
                    {
                        Debug.LogWarning("Path Tracing is not supported with XR single-pass rendering.");
                    }

#if ENABLE_VIRTUALTEXTURES
                    resolveVirtualTextureFeedback = false;
#endif
                }
                else
                {
                    gpuLightListOutput = BuildGPULightList(m_RenderGraph, hdCamera, m_TileAndClusterData, m_TotalLightCount, ref m_ShaderVariablesLightListCB, prepassOutput.depthBuffer, prepassOutput.stencilBuffer, prepassOutput.gbuffer);

                    // Evaluate the history validation buffer that may be required by temporal accumulation based effects
                    TextureHandle historyValidationTexture = EvaluateHistoryValidationBuffer(m_RenderGraph, hdCamera, prepassOutput.depthBuffer, prepassOutput.resolvedNormalBuffer, prepassOutput.resolvedMotionVectorsBuffer);

                    lightingBuffers.ambientOcclusionBuffer = RenderAmbientOcclusion(m_RenderGraph, hdCamera, prepassOutput.depthPyramidTexture, prepassOutput.resolvedNormalBuffer, prepassOutput.resolvedMotionVectorsBuffer, historyValidationTexture, hdCamera.depthBufferMipChainInfo, m_ShaderVariablesRayTracingCB, rayCountTexture);
                    lightingBuffers.contactShadowsBuffer = m_RenderGraph.defaultResources.blackUIntTextureXR; //MyChanges: no contact shadows
                    //RenderContactShadows(m_RenderGraph, hdCamera, msaa ? prepassOutput.depthValuesMSAA : prepassOutput.depthPyramidTexture, gpuLightListOutput, hdCamera.depthBufferMipChainInfo.mipLevelOffsets[1].y);
                    

                    // Single scat spectral density: // TODO: check last parameter, gpuLightListOutput.bigTileLightList
                    
                    // var UWvolumetricDensityBuffers = UWVolumeVoxelizationPasses(m_RenderGraph, hdCamera, gpuLightListOutput.bigTileLightList);
                    
                    var volumetricDensityBuffer = VolumeVoxelizationPass(m_RenderGraph, hdCamera, m_VisibleVolumeBoundsBuffer, m_VisibleVolumeDataBuffer, gpuLightListOutput.bigTileLightList);

                    RenderShadows(m_RenderGraph, hdCamera, cullingResults, ref shadowResult);
                    // shadowResult = m_RenderGraph.defaultResources.defaultShadowTexture; 
                    // MyChanges: no shadows? -> problem: VolumetricLightingPass uses a texture bound here

                    StartXRSinglePass(m_RenderGraph, hdCamera);

                    // Evaluate the clear coat mask texture based on the lit shader mode
                    var clearCoatMask = hdCamera.frameSettings.litShaderMode == LitShaderMode.Deferred ? prepassOutput.gbuffer.mrt[2] : m_RenderGraph.defaultResources.blackTextureXR;
                    lightingBuffers.ssrLightingBuffer = m_RenderGraph.defaultResources.blackTextureXR; // MyChanges: no screen space reflections
                    //RenderSSR(m_RenderGraph, hdCamera, ref prepassOutput, clearCoatMask, rayCountTexture, m_SkyManager.GetSkyReflection(hdCamera), transparent: false);
                    lightingBuffers.ssgiLightingBuffer = m_RenderGraph.defaultResources.blackTextureXR;// MyChanges: no screen space indirect diffuse illumination
                    //RenderScreenSpaceIndirectDiffuse(hdCamera, prepassOutput, rayCountTexture, historyValidationTexture, gpuLightListOutput.lightList);

                    if (hdCamera.frameSettings.IsEnabled(FrameSettingsField.RayTracing) && GetRayTracingClusterState())
                    {
                        HDRaytracingLightCluster lightCluster = RequestLightCluster();
                        lightCluster.EvaluateClusterDebugView(m_RenderGraph, hdCamera, prepassOutput.depthBuffer, prepassOutput.depthPyramidTexture);
                    }

                    lightingBuffers.screenspaceShadowBuffer = m_RenderGraph.defaultResources.blackTextureArrayXR; // MyChanges: No SS Shadows
                    // RenderScreenSpaceShadows(m_RenderGraph, hdCamera, prepassOutput, prepassOutput.depthBuffer, prepassOutput.normalBuffer, prepassOutput.motionVectorsBuffer, historyValidationTexture, rayCountTexture);

                    // The maxZ is used for volumetric effects (need it for water)
                    var maxZMask = GenerateMaxZPass(m_RenderGraph, hdCamera, prepassOutput.depthPyramidTexture, hdCamera.depthBufferMipChainInfo);

                    // var UWvolumetricLighting = UWVolumetricLightingPasses(m_RenderGraph, hdCamera, prepassOutput.depthPyramidTexture, UWvolumetricDensityBuffers, maxZMask, gpuLightListOutput.bigTileLightList, shadowResult);
                    var volumetricLighting = VolumetricLightingPass(m_RenderGraph, hdCamera, prepassOutput.depthPyramidTexture, volumetricDensityBuffer, maxZMask, gpuLightListOutput.bigTileLightList, shadowResult);



                    //var deferredLightingOutput = new LightingOutput(); // MyChanges: Empty deferred lighting
                    
                    // var colorBufferBK = colorBuffer;
                    // UWRenderDeferredLighting(m_RenderGraph, hdCamera, colorBuffer, prepassOutput.depthBuffer, prepassOutput.depthPyramidTexture, lightingBuffers, prepassOutput.gbuffer, shadowResult, gpuLightListOutput);
                    // RenderDeferredLighting()

                    // colorBuffer = UWRenderWaterSurf(m_RenderGraph, hdCamera, colorBuffer, prepassOutput.resolvedNormalBuffer, vtFeedbackBuffer, currentColorPyramid, volumetricLighting, rayCountTexture, m_SkyManager.GetSkyReflection(hdCamera), gpuLightListOutput, ref prepassOutput,
                    //     shadowResult, cullingResults, customPassCullingResults, aovRequest, aovCustomPassBuffers);


                    // --------------------------------------------------------
                    // TODO: add volumetricLighting here, use inside UWDeferred.compute?
                    // Other option: separate pass: scary
                    // , volumetricLighting
                    if (!doRGBRendering) {

                        UWRenderSky(m_RenderGraph, hdCamera, skyColorBuffer, volumetricLighting, prepassOutput.depthBuffer, msaa ? prepassOutput.depthAsColor : prepassOutput.depthPyramidTexture);

                        UWRenderDeferredLighting(m_RenderGraph, hdCamera, colorBuffer, skyColorBuffer, prepassOutput.depthBuffer, prepassOutput.depthPyramidTexture, lightingBuffers, prepassOutput.gbuffer, shadowResult, gpuLightListOutput, volumetricLighting);
                        // colorBuffer = skyColorBuffer;                    
                    }
                    else {
                        Debug.Log("RGB deferred rendering");
                        UWRGBRenderDeferredLighting(m_RenderGraph, hdCamera, colorBuffer, prepassOutput.depthBuffer, prepassOutput.depthPyramidTexture, lightingBuffers, prepassOutput.gbuffer, shadowResult, gpuLightListOutput, volumetricLighting);
                    }
                    // return; 

                    ApplyCameraMipBias(hdCamera);
                    //MyChanges: no Forward opaque?
                    RenderForwardOpaque(m_RenderGraph, hdCamera, colorBuffer, lightingBuffers, gpuLightListOutput, prepassOutput.depthBuffer, vtFeedbackBuffer, shadowResult, prepassOutput.dbuffer, cullingResults);
                    ResetCameraMipBias(hdCamera);

                    if (aovRequest.isValid)
                        aovRequest.PushCameraTexture(m_RenderGraph, AOVBuffers.Normals, hdCamera, prepassOutput.resolvedNormalBuffer, aovBuffers);

                    //MyChanges: no Subsurface Scattering.
                    //RenderSubsurfaceScattering(m_RenderGraph, hdCamera, colorBuffer, historyValidationTexture, ref lightingBuffers, ref prepassOutput);

                    // Nestor (15/09): trying make sky work with UW view: render sky must be before deferred
                    // UWRenderSky(m_RenderGraph, hdCamera, colorBuffer, volumetricLighting, prepassOutput.depthBuffer, msaa ? prepassOutput.depthAsColor : prepassOutput.depthPyramidTexture);
                    //sunOcclusionTexture = RenderVolumetricClouds(m_RenderGraph, hdCamera, colorBuffer, prepassOutput.depthPyramidTexture, prepassOutput.motionVectorsBuffer, volumetricLighting, maxZMask);

                    //TODO: Aqui
                        // Resto de cosas igual maybe
                        // Cambiar 1: VolumetricLightingPass por mi agua
                        //         2: RenderDeferredLighting por mi funcion
                        //     Igual ayuda controlar las lights (solo queremos una direccional + dw):
                        //              ver ClearLightLists en LightLoop.cs


                    // Send all the geometry graphics buffer to client systems if required (must be done after the pyramid and before the transparent depth pre-pass)
                    SendGeometryGraphicsBuffers(m_RenderGraph, prepassOutput.normalBuffer, prepassOutput.depthPyramidTexture, hdCamera);

                    DoUserAfterOpaqueAndSky(m_RenderGraph, hdCamera, colorBuffer, prepassOutput.resolvedDepthBuffer, prepassOutput.resolvedNormalBuffer, prepassOutput.resolvedMotionVectorsBuffer);

                    // No need for old stencil values here since from transparent on different features are tagged
                    ClearStencilBuffer(m_RenderGraph, hdCamera, prepassOutput.depthBuffer);

                    colorBuffer = RenderTransparency(m_RenderGraph, hdCamera, colorBuffer, prepassOutput.resolvedNormalBuffer, vtFeedbackBuffer, currentColorPyramid, volumetricLighting, rayCountTexture, m_SkyManager.GetSkyReflection(hdCamera), gpuLightListOutput, ref prepassOutput,
                        shadowResult, cullingResults, customPassCullingResults, aovRequest, aovCustomPassBuffers);

                    uiBuffer = RenderTransparentUI(m_RenderGraph, hdCamera, prepassOutput.depthBuffer);

                    if (NeedMotionVectorForTransparent(hdCamera.frameSettings))
                    {
                        prepassOutput.resolvedMotionVectorsBuffer = ResolveMotionVector(m_RenderGraph, hdCamera, prepassOutput.motionVectorsBuffer);
                    }

                    // We push the motion vector debug texture here as transparent object can overwrite the motion vector texture content.
                    if (m_Asset.currentPlatformRenderPipelineSettings.supportMotionVectors)
                        PushFullScreenDebugTexture(m_RenderGraph, prepassOutput.resolvedMotionVectorsBuffer, FullScreenDebugMode.MotionVectors, fullScreenDebugFormat);

                    // Transparent objects may write to the depth and motion vectors buffers.
                    if (aovRequest.isValid)
                    {
                        aovRequest.PushCameraTexture(m_RenderGraph, AOVBuffers.DepthStencil, hdCamera, prepassOutput.resolvedDepthBuffer, aovBuffers);
                        if (m_Asset.currentPlatformRenderPipelineSettings.supportMotionVectors)
                            aovRequest.PushCameraTexture(m_RenderGraph, AOVBuffers.MotionVectors, hdCamera, prepassOutput.resolvedMotionVectorsBuffer, aovBuffers);
                    }

                    var distortionRendererList = m_RenderGraph.CreateRendererList(CreateTransparentRendererListDesc(cullingResults, hdCamera.camera, HDShaderPassNames.s_DistortionVectorsName));

                    // This final Gaussian pyramid can be reused by SSR, so disable it only if there is no distortion
                    if (hdCamera.frameSettings.IsEnabled(FrameSettingsField.Distortion) && hdCamera.frameSettings.IsEnabled(FrameSettingsField.RoughDistortion))
                    {
                        TextureHandle distortionColorPyramid = m_RenderGraph.CreateTexture(
                            new TextureDesc(Vector2.one, true, true)
                            {
                                colorFormat = GetColorBufferFormat(),
                                enableRandomWrite = true,
                                useMipMap = true,
                                autoGenerateMips = false,
                                name = "DistortionColorBufferMipChain"
                            });
                        GenerateColorPyramid(m_RenderGraph, hdCamera, colorBuffer, distortionColorPyramid, FullScreenDebugMode.PreRefractionColorPyramid, distortionRendererList);
                        currentColorPyramid = distortionColorPyramid;
                    }

                    using (new RenderGraphProfilingScope(m_RenderGraph, ProfilingSampler.Get(HDProfileId.Distortion)))
                    {
                        var distortionBuffer = AccumulateDistortion(m_RenderGraph, hdCamera, prepassOutput.resolvedDepthBuffer, distortionRendererList);
                        RenderDistortion(m_RenderGraph, hdCamera, colorBuffer, prepassOutput.resolvedDepthBuffer, currentColorPyramid, distortionBuffer, distortionRendererList);
                    }

                    PushFullScreenDebugTexture(m_RenderGraph, colorBuffer, FullScreenDebugMode.NanTracker, fullScreenDebugFormat);
                    PushFullScreenDebugTexture(m_RenderGraph, colorBuffer, FullScreenDebugMode.WorldSpacePosition, fullScreenDebugFormat);
                    PushFullScreenLightingDebugTexture(m_RenderGraph, colorBuffer, fullScreenDebugFormat);

                    bool accumulateInPost = m_PostProcessEnabled && m_DepthOfField.IsActive();
                    if (!accumulateInPost && m_SubFrameManager.isRecording && m_SubFrameManager.subFrameCount > 1)
                    {
                        RenderAccumulation(m_RenderGraph, hdCamera, colorBuffer, colorBuffer, null, false);
                    }

                    // Render gizmos that should be affected by post processes
                    RenderGizmos(m_RenderGraph, hdCamera, GizmoSubset.PreImageEffects);
                }

#if ENABLE_VIRTUALTEXTURES
                // Note: This pass rely on availability of vtFeedbackBuffer buffer (i.e it need to be write before we read it here)
                // We don't write it when FullScreenDebug mode or path tracer.
                if (resolveVirtualTextureFeedback)
                {
                    hdCamera.ResolveVirtualTextureFeedback(m_RenderGraph, vtFeedbackBuffer);
                    PushFullScreenVTFeedbackDebugTexture(m_RenderGraph, vtFeedbackBuffer, msaa);
                }
#endif

                // At this point, the color buffer has been filled by either debug views are regular rendering so we can push it here.
                var colorPickerTexture = PushColorPickerDebugTexture(m_RenderGraph, colorBuffer);

                RenderCustomPass(m_RenderGraph, hdCamera, colorBuffer, prepassOutput, customPassCullingResults, cullingResults, CustomPassInjectionPoint.BeforePostProcess, aovRequest, aovCustomPassBuffers);

                if (aovRequest.isValid)
                {
                    aovRequest.PushCameraTexture(m_RenderGraph, AOVBuffers.Color, hdCamera, colorBuffer, aovBuffers);
                }

                bool postProcessIsFinalPass = HDUtils.PostProcessIsFinalPass(hdCamera, aovRequest);
                TextureHandle afterPostProcessBuffer = RenderAfterPostProcessObjects(m_RenderGraph, hdCamera, cullingResults, prepassOutput);
                var postProcessTargetFace = postProcessIsFinalPass ? target.face : CubemapFace.Unknown;
                TextureHandle postProcessDest = RenderPostProcess(m_RenderGraph, prepassOutput, colorBuffer, backBuffer, uiBuffer, afterPostProcessBuffer, sunOcclusionTexture, cullingResults, hdCamera, postProcessTargetFace, postProcessIsFinalPass);

                var xyMapping = GenerateDebugHDRxyMapping(m_RenderGraph, hdCamera, postProcessDest);
                GenerateDebugImageHistogram(m_RenderGraph, hdCamera, postProcessDest);
                PushFullScreenExposureDebugTexture(m_RenderGraph, postProcessDest, fullScreenDebugFormat);
                PushFullScreenHDRDebugTexture(m_RenderGraph, postProcessDest, fullScreenDebugFormat);

                ResetCameraSizeForAfterPostProcess(m_RenderGraph, hdCamera, commandBuffer);

                RenderCustomPass(m_RenderGraph, hdCamera, postProcessDest, prepassOutput, customPassCullingResults, cullingResults, CustomPassInjectionPoint.AfterPostProcess, aovRequest, aovCustomPassBuffers);

                CopyXRDepth(m_RenderGraph, hdCamera, prepassOutput.resolvedDepthBuffer, backBuffer);

                // In developer build, we always render post process in an intermediate buffer at (0,0) in which we will then render debug.
                // Because of this, we need another blit here to the final render target at the right viewport.
                if (!postProcessIsFinalPass)
                {
                    hdCamera.ExecuteCaptureActions(m_RenderGraph, postProcessDest);

                    postProcessDest = RenderDebug(m_RenderGraph,
                        hdCamera,
                        postProcessDest,
                        prepassOutput.resolvedDepthBuffer,
                        prepassOutput.depthPyramidTexture,
                        colorPickerTexture,
                        rayCountTexture,
                        xyMapping,
                        gpuLightListOutput,
                        shadowResult,
                        cullingResults,
                        fullScreenDebugFormat);

                    StopXRSinglePass(m_RenderGraph, hdCamera);

                    for (int viewIndex = 0; viewIndex < hdCamera.viewCount; ++viewIndex)
                    {
                        BlitFinalCameraTexture(m_RenderGraph, hdCamera, postProcessDest, backBuffer, uiBuffer, afterPostProcessBuffer, viewIndex, HDROutputIsActive(), target.face);
                    }

                }

                // This code is only for planar reflections. Given that the depth texture cannot be shared currently with the other depth copy that we do
                // we need to do this separately.
                for (int viewIndex = 0; viewIndex < hdCamera.viewCount; ++viewIndex)
                {
                    if (target.targetDepth != null)
                    {
                        BlitFinalCameraTexture(m_RenderGraph, hdCamera, prepassOutput.resolvedDepthBuffer, m_RenderGraph.ImportTexture(target.targetDepth), uiBuffer, afterPostProcessBuffer, viewIndex, outputsToHDR: false, cubemapFace: target.face);
                    }
                }

                SendColorGraphicsBuffer(m_RenderGraph, hdCamera);

                SetFinalTarget(m_RenderGraph, hdCamera, prepassOutput.resolvedDepthBuffer, backBuffer, target.face);

                RenderWireOverlay(m_RenderGraph, hdCamera, backBuffer);

                RenderGizmos(m_RenderGraph, hdCamera, GizmoSubset.PostImageEffects);

                RenderScreenSpaceOverlayUI(m_RenderGraph, hdCamera, backBuffer);
                // BlitFinalCameraTexture(m_RenderGraph, hdCamera, prepassOutput.resolvedDepthBuffer, m_RenderGraph.ImportTexture(target.targetDepth), uiBuffer, afterPostProcessBuffer, viewIndex, outputsToHDR: false, cubemapFace: target.face);

            }
        }



        TextureHandle UWRenderWaterSurf(RenderGraph renderGraph,
            HDCamera hdCamera,
            TextureHandle colorBuffer,
            TextureHandle normalBuffer,
            TextureHandle vtFeedbackBuffer,
            TextureHandle currentColorPyramid,
            TextureHandle volumetricLighting,
            TextureHandle rayCountTexture,
            Texture skyTexture,
            in BuildGPULightListOutput lightLists,
            ref PrepassOutput prepassOutput,
            ShadowResult shadowResult,
            CullingResults cullingResults,
            CullingResults customPassCullingResults,
            AOVRequestData aovRequest,
            List<RTHandle> aovCustomPassBuffers)
        {
            // Transparent (non recursive) objects that are rendered in front of transparent (recursive) require the recursive rendering to be executed for that pixel.
            // This means our flagging process needs to happen before the transparent depth prepass as we use the depth to discriminate pixels that do not need recursive rendering.
            // var flagMaskBuffer = RenderRayTracingFlagMask(renderGraph, cullingResults, hdCamera, prepassOutput.depthBuffer);

            // RenderTransparentDepthPrepass(renderGraph, hdCamera, prepassOutput, cullingResults);

            // Render the water gbuffer (and prepare for the transparent SSR pass)
            var waterGBuffer = RenderWaterGBuffer(m_RenderGraph, hdCamera, prepassOutput.depthBuffer, prepassOutput.normalBuffer, currentColorPyramid, prepassOutput.depthPyramidTexture);


            // if (hdCamera.frameSettings.IsEnabled(FrameSettingsField.Refraction) || hdCamera.IsSSREnabled() || hdCamera.IsSSREnabled(true) || hdCamera.IsSSGIEnabled() || hdCamera.frameSettings.IsEnabled(FrameSettingsField.Water))
            // {
            //     var resolvedColorBuffer = ResolveMSAAColor(renderGraph, hdCamera, colorBuffer, m_NonMSAAColorBuffer);
            //     GenerateColorPyramid(renderGraph, hdCamera, resolvedColorBuffer, currentColorPyramid, FullScreenDebugMode.FinalColorPyramid);
            // }

            // Render the deferred water lighting
            RenderWaterLighting(m_RenderGraph, hdCamera, colorBuffer, prepassOutput.depthBuffer, currentColorPyramid, volumetricLighting, waterGBuffer, lightLists);

            return colorBuffer;
        }


        void UWRenderSky(RenderGraph renderGraph, HDCamera hdCamera, TextureHandle colorBuffer, TextureHandle volumetricLighting, TextureHandle depthStencilBuffer, TextureHandle depthTexture)
        {
            // if (m_CurrentDebugDisplaySettings.DebugHideSky(hdCamera))
            //     return;

            m_SkyManager.RenderSky(renderGraph, hdCamera, colorBuffer, depthStencilBuffer, "Render Sky", ProfilingSampler.Get(HDProfileId.RenderSky));
            // m_SkyManager.RenderOpaqueAtmosphericScattering(renderGraph, hdCamera, colorBuffer, depthTexture, volumetricLighting, depthStencilBuffer);
        }



    }
}

