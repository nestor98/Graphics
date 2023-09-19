using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;


using UnityEditor;

#if UNITY_EDITOR
[ExecuteInEditMode]
#endif



public class HDRPManager : MonoBehaviour
{
    public static HDRPManager manager;

    public float waterSurfaceHeight = 7.0f;
    public WaterSurface waterSurface;
    public GameObject myWaterSurface;
    // public Transform myWaterSurfaceTransform;

    public bool dynamicUnderwater = true;


    private LocalVolumetricFog fog;

    private void Initialize() {
        if (manager == null)
        {
            manager = this;

            ocean = new OceanData();

            OnValidate();
            // myWaterSurface = transform.GetChild(0).GetChild(0).gameObject;

        }
        else if (manager != this)
        {
            Destroy(gameObject);
        }
    }

    private void ReInitialize() {
         Debug.Log("[HDRPManager] Re initializing");
        manager = null;
        Initialize();
    }

    private void Awake()
    {
         Debug.Log("[HDRPManager] Awaking"); 
        Initialize();
    }

    private void Start() {
         Debug.Log("[HDRPManager] Starting"); 
        Initialize();
    }

    // In the Inspector, assign a Render Pipeline Asset to each of these fields
    //public RenderPipelineAsset defaultRenderPipelineAsset;
    //public RenderPipelineAsset overrideRenderPipelineAsset;
    private HDRenderPipeline currentPipeline;
    public bool doCustomOceanPass = true;
    public bool doRGBRendering = true;

    public OceanData ocean;    

    // Awake is earlier than Start. This is so the UIController can access this OceanData on its Start

    bool IsUnderwater()
    {
        return Camera.main.transform.position.y < myWaterSurface.transform.position.y;
    }

    // Update is called once per frame
    void Update()
    {
        //Debug.Log("??????????");
        if (ocean==null) ReInitialize();

        if (currentPipeline == null)
        {
            currentPipeline = (HDRenderPipeline)RenderPipelineManager.currentPipeline;
            Debug.Log("updating manager but current pipeline was null");
        }
        else
        {
            if (dynamicUnderwater)
            {
                doCustomOceanPass = IsUnderwater();
                SetDoUnderwater(doCustomOceanPass);
            }

            // Debug.Log("Setting ocean to HDRP");
            if (ocean == null) Debug.Log("WAIT ocean is null");
            currentPipeline.doSpectralUnderwaterPass = doCustomOceanPass;
            currentPipeline.doRGBRendering = doRGBRendering;
            currentPipeline.oceanData = ocean;
            currentPipeline.m_hdrpManager = this;

            //SetDoUnderwater(doCustomOceanPass);
            //currentPipeline.
        }
        //Debug.Log("Current pipeline: " + currentPipeline);
    }

    void OnValidate()
    {
        if (waterSurface != null) 
            waterSurface.transform.position = new Vector3(waterSurface.transform.position.x, waterSurfaceHeight, waterSurface.transform.position.z);
        if (ocean != null) {
            ocean.waterSurfaceHeight = waterSurfaceHeight;
            ocean.SendDataToGPU();
        }
        if (myWaterSurface != null) myWaterSurface.transform.localPosition = new Vector3(myWaterSurface.transform.localPosition.x, waterSurfaceHeight, myWaterSurface.transform.localPosition.z);
        //UpdateFog();
        if (fog != null) UpdateFogPosition();
    }

    public void SetSurfaceHeight(float val)
    {
        waterSurfaceHeight = val;
        OnValidate();
    }

    private void UpdateFogPosition()
    {
        float size = 200.0f;
        fog.transform.position = new Vector3(0.0f, 10+waterSurfaceHeight - size / 2.0f, 0.0f);
        fog.parameters.size = new Vector3(size, size, size); // TODO: CHANGE VOL POSITION

        //Debug.Log("New fog pos: " + fog.transform.position.y);
    }

    public LocalVolumetricFog UpdateFogOld() {
        Vector3 scat = ocean.GetFogScattering();
        float ext  = ocean.GetFogExtinction();

        //Debug.Log("Updating fog with scat " + scat + " and ext " + ext);

        //m_hdrpManager = new GameObject("OceanFog");
        fog = transform.gameObject.GetComponent(typeof(LocalVolumetricFog)) as LocalVolumetricFog;
        if (fog == null) fog = transform.gameObject.AddComponent<LocalVolumetricFog>();

        // TODO: exponential fog, etc?

        bool updateFog = true;
        if (updateFog)
        {
            UpdateFogPosition();

            fog.parameters.meanFreePath = VolumeRenderingUtils.MeanFreePathFromExtinction(ext);
            Vector3 albedo = VolumeRenderingUtils.AlbedoFromMeanFreePathAndScattering(fog.parameters.meanFreePath, scat);
            Color albedoColor = new Color(albedo.x, albedo.y, albedo.z);
            fog.parameters.albedo = albedoColor;

            // Also update the water surface color

            if (waterSurface != null) UpdateSurfaceColor(new Color(scat.x, scat.y, scat.z), fog.parameters.meanFreePath);
        }

        return fog;
    }


    public LocalVolumetricFog UpdateFog()
    {
        //Vector3 scat = ocean.GetFogScattering();
        //float ext = ocean.GetFogExtinction();

        //Debug.Log("Updating fog with scat " + scat + " and ext " + ext);

        //m_hdrpManager = new GameObject("OceanFog");
        fog = transform.gameObject.GetComponent(typeof(LocalVolumetricFog)) as LocalVolumetricFog;
        if (fog == null) fog = transform.gameObject.AddComponent<LocalVolumetricFog>();

        // TODO: exponential fog, etc?

        bool updateFog = true;
        if (updateFog)
        {
            UpdateFogPosition();

            float ext = ocean.GetFogExtinction();

            fog.parameters.meanFreePath = VolumeRenderingUtils.MeanFreePathFromExtinction(ext);
            //Vector3 albedo = VolumeRenderingUtils.AlbedoFromMeanFreePathAndScattering(fog.parameters.meanFreePath, scat);
            //Color albedoColor = new Color(albedo.x, albedo.y, albedo.z);
            Color albedoColor = ocean.GetFogScatteringColor();
            fog.parameters.albedo = albedoColor;

            // Debug.Log("Updating fog with albedo " + albedoColor + " and mean free path " + fog.parameters.meanFreePath);

            // Also update the water surface color

            if (waterSurface != null) UpdateSurfaceColor(new Color(albedoColor.r, albedoColor.g, albedoColor.b)/10.0f, fog.parameters.meanFreePath);
        }

        return fog;
    }

    void UpdateSurfaceColor(Color albedo, float meanFreePath)
    {
        waterSurface.refractionColor = albedo;
        waterSurface.scatteringColor = albedo;
        waterSurface.absorptionDistance = meanFreePath;
    }


    public void SetDoUnderwater(bool val)
    {
        if (currentPipeline == null)
        {
            Debug.Log("[HDRPManager.SetDoUnderwater] currentPipeline was null");
            ReInitialize();
            //currentPipeline = (HDRenderPipeline)RenderPipelineManager.currentPipeline;
            if (currentPipeline == null)
            {
                Debug.Log("[HDRPManager.SetDoUnderwater] and still is???");
                return;
            }
            Debug.Log("[HDRPManager.SetDoUnderwater] but not anymore");
            
        }
        doCustomOceanPass = val;
        currentPipeline.doSpectralUnderwaterPass = doCustomOceanPass;

        waterSurface.enabled = (!val);
        myWaterSurface.SetActive(val);

    }

    public void SetAutomaticUnderwater(bool val)
    {
        if (currentPipeline == null)
        {
            Debug.Log("[HDRPManager.SetDoUnderwater] currentPipeline was null");
            ReInitialize();
            //currentPipeline = (HDRenderPipeline)RenderPipelineManager.currentPipeline;
            if (currentPipeline == null)
            {
                Debug.Log("[HDRPManager.SetDoUnderwater] and still is???");
                return;
            }
            Debug.Log("[HDRPManager.SetDoUnderwater] but not anymore");

        }
        dynamicUnderwater = val;

        SetDoUnderwater(IsUnderwater());


    }

    public void SetNWavelengths(int n) {
        ocean.SetNWavelengths(n);
        ocean.SendDataToGPU();
    }


    public void SetRGBRendering(bool val) {
        doRGBRendering = val;
        if (currentPipeline!=null) currentPipeline.doRGBRendering = val;
        if (ocean!=null) ocean.SendDataToGPU();
    }

    // public void SetScatMult(float val) {
    //     ocean.SetScatteringMultiplier(val);
    // }


    // public void SetAbsMult(float val) {
    //     ocean.SetScatteringMultiplier(val);
    // }



}
