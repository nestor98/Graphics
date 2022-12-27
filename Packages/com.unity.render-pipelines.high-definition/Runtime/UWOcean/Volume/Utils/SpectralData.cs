
#define STRETCH_WL_RANGE


using System.Collections;
using System.Collections.Generic;

using System;
using UnityEngine;

using System.IO;
using System.Globalization;

using System.Linq;

// Camera is wl, r, g, b
// Medium is wl, scat, ext, dw
public class SpectralData
{
    private List<List<float>> mediumSpectralCoefs, mediumSpectralCoefsInterpolated; // NOTE: lists in c# actually have random access (they are arrays internally)
    private List<List<float>> cameraResponseCurve, cameraResponseCurveInterpolated;
    // private int nWavelenghts;

    private char sep = ',';

    private float m_AbsorptionMult = 1.0f, m_ScatteringMult = 1.0f;


    private Vector3 m_ScatteringRGB, m_ExtinctionRGB, m_DownwellingRGB;


    private int idLogCurve = 0; // TMP

    public void LoadResponseCurve(string filename) {
      cameraResponseCurve = CSVReader.ReadColumns(filename, sep);
    }

    public void LoadMedium(string filename) {
      var absScatDw = CSVReader.ReadRows(filename, sep);
      // Init
      mediumSpectralCoefs = new List<List<float>>();
      for(int i = 0; i < 4; i++) mediumSpectralCoefs.Add(new List<float>());
      // Coefs:
      mediumSpectralCoefs[0] = absScatDw[0]; // WL
      mediumSpectralCoefs[1] = absScatDw[2]; // Scat
      mediumSpectralCoefs[3] = absScatDw[3]; // Dw
      for(int i = 0; i < absScatDw[0].Count; i++)
      {
        // Ext = scat + abs = [2] + [1]:
        mediumSpectralCoefs[2].Add(absScatDw[1][i]+absScatDw[2][i]);
      }

    }

    private int FindNextWL(List<float> wls, float wl) {
        // int myIndex=List<float>.BinarySearch(wls, wl);
        int idx = wls.BinarySearch(wl);
        if (idx < 0)
        { // Not found, returns next:
            return ~idx;
        }
        else
        { // Found, also return next
            return (idx<wls.Count-1) ? idx+1 : idx;
        }
    }

    // Given the list of 3 values inCoefs, outCoefs[123][wl] becomes the interpolation between the wavelength to the left of wl 
    // in the original curve and the one just to the right 
    private void InterpolateWL(List<List<float>> inCoefs, ref List<List<float>> outCoefs, float wl) {

      int nextWl = FindNextWL(inCoefs[0], wl); // first get wl to the right
      // //Debug.Log(nextWl + " " + inCoefs[0].Count);
      float wlleft = inCoefs[0][nextWl - 1],
            wlright = inCoefs[0][nextWl],
            dist = (wl - wlleft) / (wlright - wlleft);
      // RGB or scat/ext/dw:
      float val1 = Mathf.Lerp(inCoefs[1][nextWl - 1], inCoefs[1][nextWl], dist),
            val2 = Mathf.Lerp(inCoefs[2][nextWl - 1], inCoefs[2][nextWl], dist),
            val3 = Mathf.Lerp(inCoefs[3][nextWl - 1], inCoefs[3][nextWl], dist);
      // Add to output list
      outCoefs[1].Add(val1);
      outCoefs[2].Add(val2);
      outCoefs[3].Add(val3);
    }

#if (STRETCH_WL_RANGE)
    // Wls go from [begin, end]

    // Sets the wavelengths to nWls
    // The range becomes the min to the max of the response curve
    // then, each new wl is equidistant in between
    // The response for each of the RGB channels is interpolated
    // For the medium coefs, they are also interpolated from their respective curves
    // 
    // Note: a better way to do this would be to average more values, 
    // especially if nWavelengths is much lower than the original number of wls
    public void SetNWavelengths(int nWavelengths) {
      // Debug.Log("Setting " +  nWavelengths);
      // Debug.Log(". Original curves:");
      //PrintLists(cameraResponseCurve, mediumSpectralCoefs);
      // Initialize:
      cameraResponseCurveInterpolated = new List<List<float>>();
      mediumSpectralCoefsInterpolated = new List<List<float>>();
      for(int i = 0; i < 4; i++)
      {
        cameraResponseCurveInterpolated.Add(new List<float>());
        mediumSpectralCoefsInterpolated.Add(new List<float>());
      }
      // Set up wavelengths:
      float minwl = Mathf.Max(cameraResponseCurve[0].First(), mediumSpectralCoefs[0].First()),
            maxwl = Mathf.Min(cameraResponseCurve[0].Last(), mediumSpectralCoefs[0].Last()),
            stepwl = (maxwl - minwl) / (float)(nWavelengths-1);
      // Fill every wl:
      for(int i = 0; i < nWavelengths; i++)
      {
        float wl = minwl + stepwl*(float)i; // The wavelength
        cameraResponseCurveInterpolated[0].Add(wl); // Into the responseCurve
        // Interpolate everything else:
        InterpolateWL(cameraResponseCurve, ref cameraResponseCurveInterpolated, wl);
        InterpolateWL(mediumSpectralCoefs, ref mediumSpectralCoefsInterpolated, wl);
      }

      UpdateMultipliers();

      ComputeScatExtDwRGB();

      // TestNWavelengths(nWavelengths);
    }

#else
    // Center each wl in its range

    // Sets the wavelengths to nWls
    // The range becomes the min to the max of the response curve
    // then, each new wl is equidistant in between
    // The response for each of the RGB channels is interpolated
    // For the medium coefs, they are also interpolated from their respective curves
    // 
    // This is the averaging way to do it
    public void SetNWavelengths(int nWavelengths) {
        // Debug.Log("Setting " +  nWavelengths);
        // Debug.Log(". Original curves:");
        //PrintLists(cameraResponseCurve, mediumSpectralCoefs);
        // Initialize:
        cameraResponseCurveInterpolated = new List<List<float>>();
        mediumSpectralCoefsInterpolated = new List<List<float>>();
        for(int i = 0; i < 4; i++)
        {
            cameraResponseCurveInterpolated.Add(new List<float>());
            mediumSpectralCoefsInterpolated.Add(new List<float>());
        }
        // Set up wavelengths:
        float minwl = Mathf.Max(cameraResponseCurve[0].First(), mediumSpectralCoefs[0].First()),
              maxwl = Mathf.Min(cameraResponseCurve[0].Last(), mediumSpectralCoefs[0].Last()),
              stepwl = (maxwl - minwl) / (float)nWavelengths;

        // Fill every wl:
        for(int i = 0; i < nWavelengths; i++)
        {
            float wl = minwl + stepwl*(((float)i) + 0.5f); // The wavelength
            cameraResponseCurveInterpolated[0].Add(wl); // Into the responseCurve
            // Interpolate everything else:
            InterpolateWL(cameraResponseCurve, ref cameraResponseCurveInterpolated, wl);
            InterpolateWL(mediumSpectralCoefs, ref mediumSpectralCoefsInterpolated, wl);
        }

        UpdateMultipliers();

        ComputeScatExtDwRGB();

        // TestNWavelengths(nWavelengths);
    }
#endif
    private void UpdateMultipliers() {
      var scat = GetScattering();
      var ext  = GetExtinction();
      for (int i=0; i<GetNWavelengths(); i++) {
        float absWl = ext[i] - scat[i];
        scat[i] *= m_ScatteringMult;
        ext[i]   = scat[i] + absWl * m_AbsorptionMult;
      }
    }

    private bool TestNWavelengths(int nWavelengths) {
      int i = 0;
      foreach(var list in mediumSpectralCoefsInterpolated) {
        if (list.Count != nWavelengths) {
          //Debug.Log("Not ok on " + i);
          return false;
        }
        i++;
      }
      foreach(var list in cameraResponseCurveInterpolated) {
        if (list.Count != nWavelengths) {
          //Debug.Log("Not ok on " + i);
          return false;
        }
        i++;
      }
      return true;
    }

    public List<List<float>> GetResponseCurve() {
      return cameraResponseCurveInterpolated;
    }

    public List<List<float>> GetMediumCoefs() {
      return mediumSpectralCoefsInterpolated;
    }

    public int GetNWavelengths() {
      return cameraResponseCurveInterpolated[0].Count;
    }

    public List<float> GetScattering() {
      return mediumSpectralCoefsInterpolated[1];
    }

    public List<float> GetExtinction() {
      return mediumSpectralCoefsInterpolated[2];
    }

    public List<float> GetDownwelling() {
      return mediumSpectralCoefsInterpolated[3];
    }

    // Sets the scattering multiplier (for the interpolated curve)
    public void SetScatteringMultiplier(float mult) {
      var scattering = GetScattering();
      var extinction = GetExtinction();
      for (int i = 0; i<scattering.Count; i++) 
      {
          float absorptionWl = extinction[i] - scattering[i];
          scattering[i] = scattering[i] * mult / m_ScatteringMult;
          extinction[i] = absorptionWl + scattering[i];
      }
      m_ScatteringMult = mult;
    }

    // Sets the absorption multiplier (for the interpolated curve)
    public void SetAbsorptionMultiplier(float mult) {
      var scattering = GetScattering();
      var extinction = GetExtinction();
      for (int i = 0; i<scattering.Count; i++) 
      {
          float absorptionWl = extinction[i] - scattering[i];
          absorptionWl = absorptionWl * mult / m_AbsorptionMult;
          extinction[i] = absorptionWl + scattering[i];
      }
      m_AbsorptionMult = mult;
    }

    private void PrintLists(List<List<float>> list1, List<List<float>> list2) {
      Debug.Log("There are " + list2[1].Count + " wls");
      for (int i = 0; i<list2[1].Count; i++) {
        Debug.Log(list1[0][i] + " " + list1[1][i] + " " + list1[2][i] + " " + list1[3][i] + " " + list2[1][i] + " " + list2[2][i] + " " + list2[3][i] + "\n");
      }
    }
    public void PrintCurves() {
      PrintLists(cameraResponseCurveInterpolated, mediumSpectralCoefsInterpolated);
    }

    // Note: Hacky way to do this
    public Color GetRefractionColor() {
      var response = cameraResponseCurveInterpolated;
      var medium = mediumSpectralCoefsInterpolated;
      int nWls = GetNWavelengths();

      Color refraction = new Color(0.0f,0.0f,0.0f);
      for(int i = 0; i < nWls; i++)
      {
        float albedo = medium[1][i] / medium[2][i]; // albedo = scat/ext
        refraction.r += response[1][i] * Mathf.Exp(albedo);
        refraction.g += response[2][i] * Mathf.Exp(albedo);
        refraction.b += response[3][i] * Mathf.Exp(albedo);
      }
      return refraction / (float)nWls;
    }

    public Vector3 GetResponseWL(int wlIdx) {
      var rc = cameraResponseCurveInterpolated;
      return new Vector3(rc[1][wlIdx], rc[2][wlIdx], rc[3][wlIdx]);
    }

    public Color GetScatteringColor()
    {
        return GetRefractionColor();
        // var response = cameraResponseCurveInterpolated;
        // var medium = mediumSpectralCoefsInterpolated;
        // int nWls = GetNWavelengths();

        // Color refraction = new Color(0.0f, 0.0f, 0.0f);
        // for (int i = 0; i < nWls; i++)
        // {
        //     float albedo = medium[1][i] / medium[2][i]; // albedo = scat/ext
        //     // multiple scat up to inf
        //     refraction.r += response[1][i] * albedo;
        //     refraction.g += response[2][i] * albedo;
        //     refraction.b += response[3][i] * albedo;
        // }
        // return refraction / (float)nWls;
    }


    // Only use the wavelength of maximum intensity for each channel, then the corresponding 
    // coef of that wl for each of the scat,ext,dw coefs
    private void ComputeScatExtDwRGB() {

        var response = cameraResponseCurveInterpolated;
        var medium = mediumSpectralCoefsInterpolated;
        int nWls = GetNWavelengths();

        // Max intensity of each c:
        var maxR = response[1].Max();
        var maxG = response[2].Max();
        var maxB = response[3].Max();

        // Index of max intensity of each:
        var maxIdxR = response[1].IndexOf(maxR);
        var maxIdxG = response[2].IndexOf(maxG);
        var maxIdxB = response[3].IndexOf(maxB);

        m_ScatteringRGB = Vector3.zero;
        m_ExtinctionRGB = Vector3.zero;
        m_DownwellingRGB = Vector3.zero;
        
        m_ScatteringRGB.x = medium[1][maxIdxR];
        m_ScatteringRGB.y = medium[1][maxIdxG];
        m_ScatteringRGB.z = medium[1][maxIdxB];

        m_ExtinctionRGB.x = medium[2][maxIdxR];
        m_ExtinctionRGB.y = medium[2][maxIdxG];
        m_ExtinctionRGB.z = medium[2][maxIdxB];

        m_DownwellingRGB.x = medium[3][maxIdxR];
        m_DownwellingRGB.y = medium[3][maxIdxG];
        m_DownwellingRGB.z = medium[3][maxIdxB];
    }


    // Pre-convolving the coefs with the response curve. This is a bad idea because, if we have a spectrum like L=coefs*e^(coefs)...:
    //   sum((e^(coef)...)*response) != sum(e^(coef*response)
    private void ComputeScatExtDwRGBOld() {

        var response = cameraResponseCurveInterpolated;
        var medium = mediumSpectralCoefsInterpolated;
        int nWls = GetNWavelengths();

        m_ScatteringRGB = Vector3.zero;
        m_ExtinctionRGB = Vector3.zero;
        m_DownwellingRGB = Vector3.zero;
        for(int i = 0; i < nWls; i++)
        {
            m_ScatteringRGB.x += response[1][i] * medium[1][i];
            m_ScatteringRGB.y += response[2][i] * medium[1][i];
            m_ScatteringRGB.z += response[3][i] * medium[1][i];

            m_ExtinctionRGB.x += response[1][i] * medium[2][i];
            m_ExtinctionRGB.y += response[2][i] * medium[2][i];
            m_ExtinctionRGB.z += response[3][i] * medium[2][i];

            m_DownwellingRGB.x += response[1][i] * medium[3][i];
            m_DownwellingRGB.y += response[2][i] * medium[3][i];
            m_DownwellingRGB.z += response[3][i] * medium[3][i];
        }
        m_ScatteringRGB *= 5.0f / (float)nWls;
        m_ExtinctionRGB *= 5.0f / (float)nWls;
        m_DownwellingRGB *= 5.0f / (float)nWls;
    }

    // Pre-convolving the coefs with the response curve. This is a bad idea because, if we have a spectrum like L=coefs*e^(coefs)...:
    //   sum((e^(coef)...)*response) != sum(e^(coef*response)
    // In this one i tried log(response)*coef, which would work if L was only L=exp(coef). But it isn't
    private void ComputeScatExtDwRGBPreconvolveLog() {

        var response = cameraResponseCurveInterpolated;
        var medium = mediumSpectralCoefsInterpolated;
        int nWls = GetNWavelengths();

        m_ScatteringRGB = Vector3.zero;
        m_ExtinctionRGB = Vector3.zero;
        m_DownwellingRGB = Vector3.zero;
        for(int i = 0; i < nWls; i++)
        {
            m_ScatteringRGB.x += Mathf.Log(response[1][i]) + medium[1][i];
            m_ScatteringRGB.y += Mathf.Log(response[2][i]) + medium[1][i];
            m_ScatteringRGB.z += Mathf.Log(response[3][i]) + medium[1][i];

            m_ExtinctionRGB.x += Mathf.Log(response[1][i]) + medium[2][i];
            m_ExtinctionRGB.y += Mathf.Log(response[2][i]) + medium[2][i];
            m_ExtinctionRGB.z += Mathf.Log(response[3][i]) + medium[2][i];

            m_DownwellingRGB.x += Mathf.Log(response[1][i]) + medium[3][i];
            m_DownwellingRGB.y += Mathf.Log(response[2][i]) + medium[3][i];
            m_DownwellingRGB.z += Mathf.Log(response[3][i]) + medium[3][i];
        }
        m_ScatteringRGB *= 0.50f / (float)nWls;
        m_ExtinctionRGB *= 0.50f / (float)nWls;
        m_DownwellingRGB *= 0.50f / (float)nWls;
    }




    // To test the RGB version of our approximation
    public Vector3 GetScatteringRGB() {
        if (m_ScatteringRGB == null) {
          ComputeScatExtDwRGB();
        }
        Debug.Log("Scat " + m_ScatteringRGB);
        return m_ScatteringRGB;
    }
    public Vector3 GetExtinctionRGB() {
        if (m_ExtinctionRGB == null) {
          ComputeScatExtDwRGB();
        }

        Debug.Log("ext " + m_ExtinctionRGB);
        return m_ExtinctionRGB;
    }
    public Vector3 GetDownwellingRGB() {
        if (m_DownwellingRGB == null) {
          ComputeScatExtDwRGB();
        }
        Debug.Log("dw " + m_DownwellingRGB);

        return m_DownwellingRGB;
    }



    public void SaveInterpolatedResponseCurve(string name) {
        // var filename = "Assets/Logs~/response-curves/" + DateTime.Now.ToString("yyyy-MM-dd--hh-mm-ss_") + idLogCurve++ + ".csv";
        var filename = "Assets/Logs~/response-curves/" + name + "_" + GetNWavelengths() + "_wls" + ".csv";
        Directory.CreateDirectory(Path.GetDirectoryName(filename));
        FileStream stream = new FileStream(filename, FileMode.OpenOrCreate);

        using (var w = new StreamWriter(stream))
        {

            var response = GetResponseCurve();
            for (int i = 0; i<4; i++)
            {
                var line = "";
                for (int wl=0;wl<GetNWavelengths();wl++) {
                  line += string.Format("{0}", response[i][wl], CultureInfo.InvariantCulture); 
                  if (wl<GetNWavelengths()-1) line += ",";
                }
                w.WriteLine(line);
            }
            w.Flush();
        }

        Debug.Log("Saved Log " + filename);
    }


    // For RGB->spectral conversion in the shader
    // Returns the idx of the max wl considered "blue"+1
    public int GetMaxWLIdxB() {
      float blueWl = 490.0f; // approx https://www.google.com/url?sa=i&url=https%3A%2F%2Fopg.optica.org%2Fabstract.cfm%3Furi%3Doe-25-3-2016&psig=AOvVaw0hWLXD0Cqfm98E81hLsFi6&ust=1666703833082000&source=images&cd=vfe&ved=0CA0QjRxqFwoTCLCm8eP5-PoCFQAAAAAdAAAAABAH
      var wls = GetResponseCurve()[0];
      int nextWl = FindNextWL(wls, blueWl); // first get wl to the right
      return nextWl;
    }

    // For RGB->spectral conversion in the shader
    // Returns the idx of the max wl considered "green"+1
    public int GetMaxWLIdxG() {
      float greenWl = 590.0f; // approx https://www.google.com/url?sa=i&url=https%3A%2F%2Fopg.optica.org%2Fabstract.cfm%3Furi%3Doe-25-3-2016&psig=AOvVaw0hWLXD0Cqfm98E81hLsFi6&ust=1666703833082000&source=images&cd=vfe&ved=0CA0QjRxqFwoTCLCm8eP5-PoCFQAAAAAdAAAAABAH
      var wls = GetResponseCurve()[0];
      int nextWl = FindNextWL(wls, greenWl); // first get wl to the right
      return nextWl;
    }

    // version 3 en el cuadernillo, hace las superficies "un poco mas grises":
    // // For RGB->spectral conversion in the shader
    // // Returns the idx of the max wl considered "blue"+1
    // public int GetMaxWLIdxB() {
    //   float blueWl = 470.0f; 
    //   var wls = GetResponseCurve()[0];
    //   int nextWl = FindNextWL(wls, blueWl); // first get wl to the right
    //   return nextWl;
    // }

    // // For RGB->spectral conversion in the shader
    // // Returns the idx of the max wl considered "green"+1
    // public int GetMaxWLIdxG() {
    //   float greenWl = 600.0f; 
    //   var wls = GetResponseCurve()[0];
    //   int nextWl = FindNextWL(wls, greenWl); // first get wl to the right
    //   return nextWl;
    // }
}
