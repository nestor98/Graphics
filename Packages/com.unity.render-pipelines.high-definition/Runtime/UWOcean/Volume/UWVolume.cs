using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.HighDefinition
{
    /*
    public partial class HDRenderPipeline
    {

        LocalVolumetricFogManager m_UWOceanFogManager;

        List<OrientedBBox> m_UWVisibleVolumeBounds = null;
        List<LocalVolumetricFogEngineData> m_UWVisibleVolumeData = null;

        // ComputeBuffer m_UWVisibleVolumeBoundsBuffer, m_UWVisibleVolumeDataBuffer; 
        List<ComputeBuffer> m_UWVisibleVolumeBoundsBuffers, m_UWVisibleVolumeDataBuffers;

        // Called from Lighting/VolumetricLighting/VolumetricLighting.cs:
        void UWCreateVolumetricLightingBuffers() 
        {
            // New UW things:
            m_UWVisibleVolumeBounds = new List<OrientedBBox>();
            m_UWVisibleVolumeData = new List<LocalVolumetricFogEngineData>();

            if (m_UWOceanFogManager==null) { // First time, create the second manager and read the coefs
                m_UWOceanFogManager = new LocalVolumetricFogManager(); 
                for (int i = 0; i<oceanData.GetNumberSingleScatteringPasses();i++) {
                    m_UWOceanFogManager.RegisterVolume(new LocalVolumetricFog());

                    var visibleVolumeBoundsBuffer = new ComputeBuffer(k_MaxVisibleLocalVolumetricFogCount, Marshal.SizeOf(typeof(OrientedBBox)));
                    var visibleVolumeDataBuffer = new ComputeBuffer(k_MaxVisibleLocalVolumetricFogCount, Marshal.SizeOf(typeof(LocalVolumetricFogEngineData)));
                    m_UWVisibleVolumeBoundsBuffers.Add(visibleVolumeBoundsBuffer);
                    m_UWVisibleVolumeDataBuffers.Add(visibleVolumeDataBuffer);
                }
            }
        }

        void UWDestroyVolumetricLightingBuffers()
        {
            for (int i=0; i<m_UWVisibleVolumeDataBuffers.Count; i++) {

                CoreUtils.SafeRelease(m_UWVisibleVolumeDataBuffers[i]);
                CoreUtils.SafeRelease(m_UWVisibleVolumeBoundsBuffers[i]);
            }
            m_UWVisibleVolumeBoundsBuffers = null;
            m_UWVisibleVolumeDataBuffers = null;

            m_UWVisibleVolumeData = null; // free()
            m_UWVisibleVolumeBounds = null; // free()

        }


        LocalVolumetricFogList PrepareVolumetricOceanFogList(HDCamera hdCamera, CommandBuffer cmd, OceanData ocean)
        {

            List<OceanData.FogParameters> fogParams = ocean.GetFogParameters();
            for (int i = 0; i<fogParams.Count; i++) {
                LocalVolumetricFog localVolFog = UWToLocalVolumetricFog(fogParams[i]);
                m_UWOceanFogManager.m_Volumes[i] = localVolFog;
            }

            LocalVolumetricFogList localVolumetricFog = new LocalVolumetricFogList();

            if (!Fog.IsVolumetricFogEnabled(hdCamera))
                return localVolumetricFog;

            using (new ProfilingScope(cmd, ProfilingSampler.Get(HDProfileId.PrepareVisibleLocalVolumetricFogList)))
            {
                Vector3 camPosition = hdCamera.camera.transform.position;
                Vector3 camOffset = Vector3.zero;// World-origin-relative

                if (ShaderConfig.s_CameraRelativeRendering != 0)
                {
                    camOffset = camPosition; // Camera-relative
                }

                m_UWVisibleVolumeBounds.Clear(); 
                m_UWVisibleVolumeData.Clear();   
                m_UWVisibleVolumeBoundsBuffers.Clear();
                m_UWVisibleVolumeDataBuffers.Clear();   

                // Collect all visible finite volume data, and upload it to the GPU.
                var volumes = m_UWOceanFogManager.PrepareLocalVolumetricFogData(cmd, hdCamera);
                // var volumes = UWPrepareLocalVolumetricFogData(m_UWOceanFogManager, cmd, hdCamera, );



                for (int i = 0; i < Math.Min(volumes.Count, k_MaxVisibleLocalVolumetricFogCount); i++)
                {
                    LocalVolumetricFog volume = volumes[i];

                    // TODO: cache these?
                    var obb = new OrientedBBox(Matrix4x4.TRS(volume.transform.position, volume.transform.rotation, volume.parameters.size));

                    // Handle camera-relative rendering.
                    obb.center -= camOffset;

                    List<LocalVolumetricFogEngineData> dataL = new List<LocalVolumetricFogEngineData>();
                    // Frustum cull on the CPU for now. TODO: do it on the GPU.
                    // TODO: account for custom near and far planes of the V-Buffer's frustum.
                    // It's typically much shorter (along the Z axis) than the camera's frustum.
                    if (GeometryUtils.Overlap(obb, hdCamera.frustum, 6, 8))
                    {
                        // TODO: cache these?
                        var data = volume.parameters.ConvertToEngineData();

                        m_UWVisibleVolumeBounds.Add(obb);
                        m_UWVisibleVolumeData.Add(data);

                        dataL = new List<LocalVolumetricFogEngineData>() { data };
                    }

                    // Single element list versions of the same things for SetData 
                    // (need to keep them separate, otherwise the scat will get mixed)
                    var obbL  = new List<OrientedBBox>() {obb};
                    

                    m_UWVisibleVolumeBoundsBuffers[i].SetData(obbL);
                    m_UWVisibleVolumeDataBuffers[i].SetData(dataL);
                }

                // m_UWVisibleVolumeBoundsBuffer.SetData(m_UWVisibleVolumeBounds);
                // m_UWVisibleVolumeDataBuffers.SetData(m_UWVisibleVolumeData);

                // Fill the struct with pointers in order to share the data with the light loop.
                localVolumetricFog.bounds = m_UWVisibleVolumeBounds;
                localVolumetricFog.density = m_UWVisibleVolumeData;

                return localVolumetricFog;
            }
        }

        private LocalVolumetricFog UWToLocalVolumetricFog(OceanData.FogParameters fogParams) {
            LocalVolumetricFog fog = new LocalVolumetricFog();

            fog.transform.position = new Vector3(0.0f,0.0f,0.0f);
            fog.parameters.size = new Vector3(200.0f, 200.0f, 200.0f); // TODO: CHANGE VOL POSITION

            fog.parameters.meanFreePath = VolumeRenderingUtils.MeanFreePathFromExtinction(fogParams.ext);

            // TODO: color to vector3
            fog.parameters.albedo = VolumeRenderingUtils.AlbedoFromMeanFreePathAndScattering(fog.parameters.meanFreePath, fogParams.scat);
            return fog;
        }


        public List<TextureHandle> UWVolumeVoxelizationPasses(
            RenderGraph renderGraph,
            HDCamera hdCamera,
            ComputeBufferHandle bigTileLightList) 
        {
            List<TextureHandle> volDensityBuffers;
            for (int i=0; i<oceanData.GetNumberSingleScatteringPasses(); i++) { // Do a volume pass for each one of the volumes, otherwise their scat coefs get mixed!
                ComputeBuffer bounds = m_UWVisibleVolumeBoundsBuffers[i];
                ComputeBuffer data   = m_UWVisibleVolumeDataBuffers[i];
                var volumetricDensityBuffer = VolumeVoxelizationPass(m_RenderGraph, hdCamera, bounds, data, gpuLightListOutput.bigTileLightList);
                volDensityBuffers.Add(volumetricDensityBuffer);
            }
            return volDensityBuffers; 

        } // TODO: same for VolLightingPass
                 
       
        TextureHandle UWVolumetricLightingPasses(
            RenderGraph renderGraph, 
            HDCamera hdCamera, 
            TextureHandle depthTexture, 
            List<TextureHandle> densityBuffers, 
            TextureHandle maxZBuffer, 
            ComputeBufferHandle bigTileLightListBuffer, 
            ShadowResult shadowResult)
        {
            if (Fog.IsVolumetricFogEnabled(hdCamera))
            {
                for (int i=0; i<densityBuffers.Count; i++) {
                    var volLightWL = VolumetricLightingPass(m_RenderGraph, hdCamera, prepassOutput.depthPyramidTexture, UWvolumetricDensityBuffers[i], maxZMask, gpuLightListOutput.bigTileLightList, shadowResult);
                }

            }

            return renderGraph.ImportTexture(HDUtils.clearTexture3DRTH);
        }


        class DecodeVolumeLightingPassData
        {
            public List<List<float>> responseCurve;
            public int nWls, nInputTextures;
            public Material material;
            public List<TextureHandle> inputTexture;
            public TextureHandle outputTexture;
        }


        TextureHandle DecodeVolumeLightingPass(RenderGraph renderGraph, List<TextureHandle> inputTextures, List<List<float>> responseCurve, Material material)
        {
            // Note: out var passData will be of type MyRenderPassData. Why does it have to be _out_? No idea
            using (var builder = renderGraph.AddRenderPass<DecodeVolumeLightingPassData>("Decode single scattering lighting pass", out var passData))
            {
                // TODO: how to send list????
                passData.responseCurve = responseCurve;
                passData.material = material;

                // Tells the graph that this pass will read inputTexture.
                passData.inputTexture = builder.ReadTexture(inputTexture);

                // Creates the output texture.
                TextureHandle output = renderGraph.CreateTexture(new TextureDesc(Vector2.one, true, true)
                                { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, clearBuffer = true, clearColor = Color.black, name = "Output" });
                // Tells the graph that this pass will write this texture and needs to be set as render target 0.
                passData.outputTexture = builder.UseColorBuffer(output, 0);

                builder.SetRenderFunc(
                (DecodeVolumeLightingPassData data, RenderGraphContext ctx) =>
                {
                    // Render Target is already set via the use of UseColorBuffer above.
                    // If builder.WriteTexture was used, you'd need to do something like that:
                    // CoreUtils.SetRenderTarget(ctx.cmd, data.output);

                    // Setup material for rendering
                    var materialPropertyBlock = ctx.renderGraphPool.GetTempMaterialPropertyBlock();
                    materialPropertyBlock.SetTexture("_MainTexture", data.input);
                    materialPropertyBlock.SetFloat("_FloatParam", data.parameter);

                    CoreUtils.DrawFullScreen(ctx.cmd, data.material, materialPropertyBlock);
                });

                return output;
            }
        }
    }
    */
}
