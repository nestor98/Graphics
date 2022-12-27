using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO; // Path

public class OceanData 
{

    public SpectralData spectralData;
    private int numberWavelengths = 8;

    private bool dataHasChanged = true;

    public float waterSurfaceHeight = 0f;
    // Spectral data csvs:
    public string mediumCSV = "Assets/Data/medium-spectral-data/waterType_Jerlov1C_properties.csv",
                  cameraCSV = "Assets/Data/response-curves/Arriflex D21.csv";

    public OceanData()
    {
        mediumCSV = Application.streamingAssetsPath + "/medium-spectral-data/waterType_Jerlov1C_properties.csv";
        cameraCSV = Application.streamingAssetsPath + "/response-curves/Arriflex D21.csv";
        LoadData();
    }

    public void SetNWavelengths(int nWavelengths) {
        numberWavelengths = nWavelengths;
        // LoadData();
        spectralData.SetNWavelengths(numberWavelengths);
        dataHasChanged = true;
    }

    public void LoadData()
    {
        if (spectralData==null) spectralData = new SpectralData();
        spectralData.LoadResponseCurve(cameraCSV);
        spectralData.LoadMedium(mediumCSV);
        spectralData.SetNWavelengths(numberWavelengths);
        dataHasChanged = true;
    }


    public bool MustSendToGPU() {
        return dataHasChanged;
    }

    public void SendDataToGPU() {
        dataHasChanged = true;
    }

    public void SetSentData() {
        dataHasChanged = false;
    }

    // Update coefs:
    public void SetMediumPath(string path)
    {
        mediumCSV = path;
        LoadData();
    }
    public void SetCameraPath(string path)
    {
        cameraCSV = path;
        LoadData();
    }
    public void SetScatteringMultiplier(float val)
    {
        spectralData.SetScatteringMultiplier(val);
        dataHasChanged=true;
    }
    public void SetAbsorptionMultiplier(float val)
    {
        spectralData.SetAbsorptionMultiplier(val);
        dataHasChanged=true;
    }


    // For the volumes single scattering method:
    public class FogParameters {
        public Color scat;
        public float ext;

        public FogParameters(Color _scat, float _ext) {
            scat = _scat;
            ext = _ext;
        }
    }

    public float GetHeight() {
        return waterSurfaceHeight;
    }


    // [Deprecated?] Oops i didnt mean to use the response curve for this
    //public List<FogParameters> ConvertToFogParametersWithResponseCurve() {
    //    var scat = spectralData.GetScattering();
    //    var ext  = spectralData.GetExtinction();
    //    var responseCurve = spectralData.GetResponseCurve();

    //    var fogs = new List<FogParameters>();

    //    int nWls = spectralData.GetNWavelengths();
    //    for (int i = 0; i<nWls; i++) {
    //        Color responseWL = new Color(responseCurve[i][0],responseCurve[i][1],responseCurve[i][2]);
    //        // TODO: scat & average ext
    //        Color scat_i = responseWL * scat[i];
    //        Color ext_i  = responseWL * ext[i];
    //        float ext_i_avg = (ext_i.r+ext_i.g+ext_i.b)/3.0f; // Fog in unity only has monochrome extinction, take avg
    //        fogs.Add(new FogParameters(scat_i, ext_i_avg));
    //    }
    //}

    private float NextOrZero(List<float> input, int idx) {
        if (idx>=input.Count) return 0;
        else return input[idx]; 
    }

    private float avgExt(List<float> exts, int idx) {
        if (idx+2<exts.Count) {
            return (exts[idx] + exts[idx+1] + exts[idx+2])/3.0f;
        }
        else if (idx+1<exts.Count) {
            return (exts[idx] + exts[idx+1])/2.0f;
        }
        else return exts[idx];
    }

    public List<FogParameters> GetFogParameters() {
        var scat = spectralData.GetScattering();
        var ext  = spectralData.GetExtinction();

        var fogs = new List<FogParameters>();

        int nWls = spectralData.GetNWavelengths();
        for (int i = 0; i<nWls; i+=3) {
            // TODO: scat & average ext
            Color scat_i = new Color(NextOrZero(scat, i),NextOrZero(scat, i+1),NextOrZero(scat, i+2));
            float ext_i  = avgExt(ext, i);
            fogs.Add(new FogParameters(scat_i, ext_i));
        }

        if (fogs.Count != GetNumberSingleScatteringPasses()) {
            Debug.Log("[OceanData.GetFogParameters] WRONG NUMBER OF SINGLE SCAT PASSES");
        }

        return fogs;
    }

    public int GetNumberSingleScatteringPasses() {
        return (int)Mathf.Ceil((float)numberWavelengths/3.0f);
    }

    public Vector3 GetFogScattering_Old() {
        Vector3 scatColor = Vector3.zero;
        var scat = spectralData.GetScattering();
        int nWls = spectralData.GetNWavelengths();

        for (int i=0; i<nWls; i++) {
            Vector3 responseWL = spectralData.GetResponseWL(i);
            scatColor += responseWL * scat[i];
        }
        return scatColor / (float)nWls;
    }

    // Mirar regla rectangulo

    // r+g+b=sum(sigma_s)
    // Memoria, mates SS
    // 

    float L_medium(Vector3 w, float T, float distanceToSurface, float scat, float ext, float dw) {
          // w.y = abs(w.y); // preguntar, probar -abs
          // distanceToSurface = 0.0;
        return -(scat * Mathf.Exp(-dw * distanceToSurface)) / (4.0f*3.1415f * (ext + dw*w.y)) * (Mathf.Exp(-(ext + dw*w.y) * T) - 1.0f);
     }


    public float tonemap(float a) {
        return a / (1.0f + a);
    }

    public Vector3 GetFogScattering() {
        Vector3 scatColor = Vector3.zero;
        var scatList = spectralData.GetScattering();
        var extList = spectralData.GetExtinction();
        var dwList = spectralData.GetDownwelling();
        int nWls = spectralData.GetNWavelengths();

        Vector3 w = new Vector3(1.0f,0.0f,0.0f);
        float T = 1000.0f;
        float distanceToSurface = 10.0f;

        for (int i=0; i<nWls; i++) {
            Vector3 responseWL = spectralData.GetResponseWL(i);
            scatColor += responseWL * L_medium(w, T, distanceToSurface, scatList[i], extList[i], dwList[i]);
        }
        return new Vector3(tonemap(scatColor.x), tonemap(scatColor.y), tonemap(scatColor.z));
    }

    public Color GetFogScatteringColor() {
        Vector3 scatVec = GetFogScattering();
        Color scat = new Color(scatVec.x, scatVec.y, scatVec.z);
        // Color scat = GetFogScattering();
        float h,s,v;
        Color.RGBToHSV(scat, out h, out s, out v);
        v = 1.0f;
        return Color.HSVToRGB(h,s,v);
    }



    // TODO: ESTO
    public float GetFogExtinction() {
        float ext_avg = 0;
        var ext = spectralData.GetExtinction();
        int nWls = spectralData.GetNWavelengths();

        for (int i=0; i<nWls; i++) {
            ext_avg += ext[i];
        }
        return ext_avg/(float)nWls;
    }

    public string GetMediumName() {
        return Path.GetFileNameWithoutExtension(mediumCSV);
    }

    public string GetCameraName() {
        return Path.GetFileNameWithoutExtension(cameraCSV);
    }

    public string GetDepthName() {
        return string.Format("depth_{0}m", Mathf.RoundToInt(waterSurfaceHeight));
    }


}
