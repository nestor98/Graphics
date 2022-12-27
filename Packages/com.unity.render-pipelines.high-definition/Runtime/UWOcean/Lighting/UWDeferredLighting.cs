
using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.HighDefinition
{
    public partial class HDRenderPipeline
    {

        class UWDeferredLightingPassData
        {
            public int numTilesX;
            public int numTilesY;
            public int numTiles;
            public bool enableTile;
            public bool outputSplitLighting;
            public bool useComputeLightingEvaluation;
            public bool enableFeatureVariants;
            public bool enableShadowMasks;
            public int numVariants;
            public DebugDisplaySettings debugDisplaySettings;

            // Compute Lighting
            public ComputeShader deferredComputeShader;
            public int viewCount;

            // Full Screen Pixel (debug)
            public Material splitLightingMat;
            public Material regularLightingMat;

            public TextureHandle colorBuffer;
            public TextureHandle sssDiffuseLightingBuffer;
            public TextureHandle depthBuffer;
            public TextureHandle depthTexture;
            public TextureHandle volumetricLightingTexture;

            public int gbufferCount;
            public int lightLayersTextureIndex;
            public int shadowMaskTextureIndex;
            public TextureHandle[] gbuffer = new TextureHandle[8];

            public ComputeBufferHandle lightListBuffer;
            public ComputeBufferHandle tileFeatureFlagsBuffer;
            public ComputeBufferHandle tileListBuffer;
            public ComputeBufferHandle dispatchIndirectBuffer;

            public LightingBuffers lightingBuffers;

            // NEW: Now I use the Spectral struct for the wl dependent coefs
            public UWShaderVariablesUnderWaterSpectral underWaterSpectralCB;
        }

        // float GetCamToSurface() {
        //     oceanData.GetHeight()-Camera.main.transform.position.y;
        // }
        

        unsafe void UWSetOceanData(UWDeferredLightingPassData passData, HDCamera hdCamera) {
            if (oceanData == null || oceanData.spectralData == null) {
                return;
            }


            if (!oceanData.MustSendToGPU()) {
                // Debug.Log("Avoided coming in here twice hehe");
                return;
            }
            else oceanData.SetSentData();
            // passData.underWaterSpectralCB._CamToSurface = 10000.0f; // TODO: -camera position or smthing

            // Debug.Log("Cam to surface: " + passData.underWaterSpectralCB._CamToSurface);

            // passData.underWaterSpectralCB._NWavelengths = (uint)oceanData.spectralData.GetNWavelengths();
            // Debug.Log(passData.underWaterSpectralCB._NWavelengths);
            var medium = oceanData.spectralData.GetMediumCoefs();
            var responseCurve = oceanData.spectralData.GetResponseCurve();

            float camToSurface = oceanData.GetHeight() - hdCamera.camera.transform.position.y;

            passData.underWaterSpectralCB._WaterScatExtDw[3] = camToSurface; // Hack because the cbuffer doesnt seem to like having the _CamToSurface variable
            passData.underWaterSpectralCB._WaterScatExtDw[4*2-1] = (float)oceanData.spectralData.GetNWavelengths()+0.1f;
            passData.underWaterSpectralCB._WaterScatExtDw[4*3-1] = (float)oceanData.spectralData.GetMaxWLIdxB()+0.1f;
            passData.underWaterSpectralCB._WaterScatExtDw[4*4-1] = (float)oceanData.spectralData.GetMaxWLIdxG()+0.1f;
            Debug.Log("Sending nWls = " + passData.underWaterSpectralCB._WaterScatExtDw[7]);
            for (int i = 0; i < oceanData.spectralData.GetNWavelengths(); ++i) { // Each wl
                // Medium coefs:
                passData.underWaterSpectralCB._WaterScatExtDw[i * 4] = medium[1][i];
                passData.underWaterSpectralCB._WaterScatExtDw[i * 4 + 1] = medium[2][i];
                passData.underWaterSpectralCB._WaterScatExtDw[i * 4 + 2] = medium[3][i];
                // Camera response curve:
                passData.underWaterSpectralCB._ResponseCurve[i * 4] = responseCurve[1][i];
                passData.underWaterSpectralCB._ResponseCurve[i * 4 + 1] = responseCurve[2][i];
                passData.underWaterSpectralCB._ResponseCurve[i * 4 + 2] = responseCurve[3][i];
            }
            passData.underWaterSpectralCB._CamToSurface = camToSurface; // TODO: -camera position or smthing

        }   

        unsafe LightingOutput UWRenderDeferredLighting(
            RenderGraph renderGraph,
            HDCamera hdCamera,
            TextureHandle colorBuffer,
            TextureHandle depthStencilBuffer,
            TextureHandle depthPyramidTexture,
            //TextureHandle volumetricLightingTexture,
            in LightingBuffers lightingBuffers,
            in GBufferOutput gbuffer,
            in ShadowResult shadowResult,
            in BuildGPULightListOutput lightLists,
            TextureHandle volumetricLightingTexture)
        {
            // return new LightingOutput();
            if (hdCamera.frameSettings.litShaderMode != LitShaderMode.Deferred ||
                !hdCamera.frameSettings.IsEnabled(FrameSettingsField.OpaqueObjects))
                return new LightingOutput();

            if (oceanData == null || oceanData.spectralData == null) {
                if (oceanData == null) Debug.Log("No ocean set");
                else Debug.Log("No oceanData.SpectralData set");
                // return new LightingOutput();
            }

            using (var builder = renderGraph.AddRenderPass<UWDeferredLightingPassData>("Underwater Deferred Lighting", out var passData))
            {
                bool debugDisplayOrSceneLightOff = CoreUtils.IsSceneLightingDisabled(hdCamera.camera) || m_CurrentDebugDisplaySettings.IsDebugDisplayEnabled();

                int w = hdCamera.actualWidth;
                int h = hdCamera.actualHeight;
                passData.numTilesX = (w + 15) / 16;
                passData.numTilesY = (h + 15) / 16;
                passData.numTiles = passData.numTilesX * passData.numTilesY;
                passData.enableTile = hdCamera.frameSettings.IsEnabled(FrameSettingsField.DeferredTile);
                passData.outputSplitLighting = hdCamera.frameSettings.IsEnabled(FrameSettingsField.SubsurfaceScattering);
                passData.useComputeLightingEvaluation = hdCamera.frameSettings.IsEnabled(FrameSettingsField.ComputeLightEvaluation);
                passData.enableFeatureVariants = false;//GetFeatureVariantsEnabled(hdCamera.frameSettings) && !debugDisplayOrSceneLightOff;
                passData.enableShadowMasks = m_EnableBakeShadowMask;
                passData.numVariants = LightDefinitions.s_NumFeatureVariants;
                passData.debugDisplaySettings = m_CurrentDebugDisplaySettings;

                passData.volumetricLightingTexture = builder.ReadTexture(volumetricLightingTexture); // UW new: for single scat

                // Compute Lighting
                passData.deferredComputeShader = defaultResources.shaders.UWdeferredCS;
                passData.viewCount = hdCamera.viewCount;

                // Full Screen Pixel (debug)
                passData.splitLightingMat = GetDeferredLightingMaterial(true /*split lighting*/, passData.enableShadowMasks, debugDisplayOrSceneLightOff);
                passData.regularLightingMat = GetDeferredLightingMaterial(false /*split lighting*/, passData.enableShadowMasks, debugDisplayOrSceneLightOff);

                passData.colorBuffer = builder.WriteTexture(colorBuffer);

                // Warning ------- ugly test here:
                UWSetOceanData(passData, hdCamera); 
                

                if (passData.outputSplitLighting)
                {
                    passData.sssDiffuseLightingBuffer = builder.WriteTexture(lightingBuffers.diffuseLightingBuffer);
                }
                else
                {
                    // TODO RENDERGRAPH: Check how to avoid this kind of pattern.
                    // Unfortunately, the low level needs this texture to always be bound with UAV enabled, so in order to avoid effectively creating the full resolution texture here,
                    // we need to create a small dummy texture.
                    passData.sssDiffuseLightingBuffer = builder.CreateTransientTexture(new TextureDesc(1, 1, true, true) { colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, enableRandomWrite = true });
                }
                passData.depthBuffer = builder.ReadTexture(depthStencilBuffer);
                passData.depthTexture = builder.ReadTexture(depthPyramidTexture);

                passData.lightingBuffers = ReadLightingBuffers(lightingBuffers, builder);

                passData.lightLayersTextureIndex = gbuffer.lightLayersTextureIndex;
                passData.shadowMaskTextureIndex = gbuffer.shadowMaskTextureIndex;
                passData.gbufferCount = gbuffer.gBufferCount;
                for (int i = 0; i < gbuffer.gBufferCount; ++i)
                    passData.gbuffer[i] = builder.ReadTexture(gbuffer.mrt[i]);

                HDShadowManager.ReadShadowResult(shadowResult, builder);

                passData.lightListBuffer = builder.ReadComputeBuffer(lightLists.lightList);
                passData.tileFeatureFlagsBuffer = builder.ReadComputeBuffer(lightLists.tileFeatureFlags);
                passData.tileListBuffer = builder.ReadComputeBuffer(lightLists.tileList);
                passData.dispatchIndirectBuffer = builder.ReadComputeBuffer(lightLists.dispatchIndirectBuffer);

                var output = new LightingOutput();
                output.colorBuffer = passData.colorBuffer;

                builder.SetRenderFunc(
                    (UWDeferredLightingPassData data, RenderGraphContext context) =>
                    {
                        var colorBuffers = context.renderGraphPool.GetTempArray<RenderTargetIdentifier>(2);
                        colorBuffers[0] = data.colorBuffer;
                        colorBuffers[1] = data.sssDiffuseLightingBuffer;

                        // TODO RENDERGRAPH: Remove these SetGlobal and properly send these textures to the deferred passes and bind them directly to compute shaders.
                        // This can wait that we remove the old code path.
                        for (int i = 0; i < data.gbufferCount; ++i)
                            context.cmd.SetGlobalTexture(HDShaderIDs._GBufferTexture[i], data.gbuffer[i]);

                        if (data.lightLayersTextureIndex != -1)
                            context.cmd.SetGlobalTexture(HDShaderIDs._LightLayersTexture, data.gbuffer[data.lightLayersTextureIndex]);
                        else
                            context.cmd.SetGlobalTexture(HDShaderIDs._LightLayersTexture, TextureXR.GetWhiteTexture());

                        if (data.shadowMaskTextureIndex != -1)
                            context.cmd.SetGlobalTexture(HDShaderIDs._ShadowMaskTexture, data.gbuffer[data.shadowMaskTextureIndex]);
                        else
                            context.cmd.SetGlobalTexture(HDShaderIDs._ShadowMaskTexture, TextureXR.GetWhiteTexture());

                        BindGlobalLightingBuffers(data.lightingBuffers, context.cmd);

                        UWRenderComputeDeferredLighting(data, colorBuffers, context.cmd);

                        // if (data.enableTile)
                        // {
                        //     bool useCompute = data.useComputeLightingEvaluation && !k_PreferFragment;
                        //     if (useCompute)
                        //         //TODO 
                        //     RenderComputeDeferredLighting(data, colorBuffers, context.cmd);
                        //     else
                        //         // TODO  RenderComputeAsPixelDeferredLighting(data, colorBuffers, context.cmd);
                        // }
                        // else
                        // {
                        //     // TODO RenderPixelDeferredLighting(data, colorBuffers, context.cmd);
                        // }
                    });

                return output;
            }
        }



        static void UWRenderComputeDeferredLighting(UWDeferredLightingPassData data, RenderTargetIdentifier[] colorBuffers, CommandBuffer cmd)
        {
            using (new ProfilingScope(cmd, ProfilingSampler.Get(HDProfileId.RenderDeferredLightingCompute)))
            {
                data.deferredComputeShader.shaderKeywords = null;

                // data.deferredComputeShader.EnableKeyword("SHADOW_LOW");
                // data.deferredComputeShader.EnableKeyword("AREA_SHADOW_MEDIUM");          

                switch (HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams.shadowFilteringQuality)
                {
                    case HDShadowFilteringQuality.Low:
                        data.deferredComputeShader.EnableKeyword("SHADOW_LOW");
                        break;
                    case HDShadowFilteringQuality.Medium:
                        data.deferredComputeShader.EnableKeyword("SHADOW_MEDIUM");
                        break;
                    case HDShadowFilteringQuality.High:
                        data.deferredComputeShader.EnableKeyword("SHADOW_HIGH");
                        break;
                    default:
                        data.deferredComputeShader.EnableKeyword("SHADOW_MEDIUM");
                        break;
                }

                switch (HDRenderPipeline.currentAsset.currentPlatformRenderPipelineSettings.hdShadowInitParams.areaShadowFilteringQuality)
                {
                    case HDAreaShadowFilteringQuality.Medium:
                        data.deferredComputeShader.EnableKeyword("AREA_SHADOW_MEDIUM");
                        break;
                    case HDAreaShadowFilteringQuality.High:
                        data.deferredComputeShader.EnableKeyword("AREA_SHADOW_HIGH");
                        break;
                    default:
                        data.deferredComputeShader.EnableKeyword("AREA_SHADOW_MEDIUM");
                        break;
                }

                if (data.enableShadowMasks)
                {
                    data.deferredComputeShader.EnableKeyword("SHADOWS_SHADOWMASK");
                }

                // ------------------------------------------------------------------------------------------
                var kernel = data.debugDisplaySettings.IsDebugDisplayEnabled() ? s_UWshadeOpaqueDirectFptlDebugDisplayKernel : s_UWshadeOpaqueDirectFptlKernel;

                ConstantBuffer.Push(cmd, data.underWaterSpectralCB, data.deferredComputeShader, HDShaderIDs._ShaderVariablesUnderWaterSpectral); // Should be UnderwaterSpectral?

                cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs._CameraDepthTexture, data.depthTexture);

                cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs.specularLightingUAV, colorBuffers[0]);
                cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs.diffuseLightingUAV, colorBuffers[1]);
                cmd.SetComputeBufferParam(data.deferredComputeShader, kernel, HDShaderIDs.g_vLightListTile, data.lightListBuffer);

                cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs._StencilTexture, data.depthBuffer, 0, RenderTextureSubElement.Stencil);

                // UW: New for Single Scattering:
                cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs._VBufferLighting, data.volumetricLightingTexture);


                // 4x 8x8 groups per a 16x16 tile.
                cmd.DispatchCompute(data.deferredComputeShader, kernel, data.numTilesX * 2, data.numTilesY * 2, data.viewCount);
                // ------------------------------------------------------------------------------------------
                // Debug.Log("Compute dispatched.....");
                // if (data.enableFeatureVariants)
                // {
                //     for (int variant = 0; variant < data.numVariants; variant++)
                //     {
                //         var kernel = s_shadeOpaqueIndirectFptlKernels[variant];

                //         cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs._CameraDepthTexture, data.depthTexture);

                //         // TODO: Is it possible to setup this outside the loop ? Can figure out how, get this: Property (specularLightingUAV) at kernel index (21) is not set
                //         cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs.specularLightingUAV, colorBuffers[0]);
                //         cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs.diffuseLightingUAV, colorBuffers[1]);
                //         cmd.SetComputeBufferParam(data.deferredComputeShader, kernel, HDShaderIDs.g_vLightListTile, data.lightListBuffer);

                //         cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs._StencilTexture, data.depthBuffer, 0, RenderTextureSubElement.Stencil);

                //         // always do deferred lighting in blocks of 16x16 (not same as tiled light size)
                //         cmd.SetComputeBufferParam(data.deferredComputeShader, kernel, HDShaderIDs.g_TileFeatureFlags, data.tileFeatureFlagsBuffer);
                //         cmd.SetComputeIntParam(data.deferredComputeShader, HDShaderIDs.g_TileListOffset, variant * data.numTiles * data.viewCount);
                //         cmd.SetComputeBufferParam(data.deferredComputeShader, kernel, HDShaderIDs.g_TileList, data.tileListBuffer);
                
                        
                //             // UW: New for Single Scattering:
                //             cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs._VBufferLighting, data.volumetricLightingTexture);

                //         cmd.DispatchCompute(data.deferredComputeShader, kernel, data.dispatchIndirectBuffer, (uint)variant * 3 * sizeof(uint));
                //     }
                // }
                // else
                // {
                //     var kernel = data.debugDisplaySettings.IsDebugDisplayEnabled() ? s_shadeOpaqueDirectFptlDebugDisplayKernel : s_shadeOpaqueDirectFptlKernel;

                //     cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs._CameraDepthTexture, data.depthTexture);

                //     cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs.specularLightingUAV, colorBuffers[0]);
                //     cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs.diffuseLightingUAV, colorBuffers[1]);
                //     cmd.SetComputeBufferParam(data.deferredComputeShader, kernel, HDShaderIDs.g_vLightListTile, data.lightListBuffer);

                //     cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs._StencilTexture, data.depthBuffer, 0, RenderTextureSubElement.Stencil);

                //     // UW: New for Single Scattering:
                //     cmd.SetComputeTextureParam(data.deferredComputeShader, kernel, HDShaderIDs._VBufferLighting, data.volumetricLightingTexture);

                //     // 4x 8x8 groups per a 16x16 tile.
                //     cmd.DispatchCompute(data.deferredComputeShader, kernel, data.numTilesX * 2, data.numTilesY * 2, data.viewCount);
                // }

            }
        }
    }


}
