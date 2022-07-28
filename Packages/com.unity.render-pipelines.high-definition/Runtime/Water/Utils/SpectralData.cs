using System.Collections;
using System.Collections.Generic;

using System;
using UnityEngine;

using System.IO;


using System.Linq;


public class SpectralData
{
    private List<List<float>> mediumSpectralCoefs, mediumSpectralCoefsInterpolated; // NOTE: lists in c# actually have random access (they are arrays internally)
    private List<List<float>> cameraResponseCurve, cameraResponseCurveInterpolated;
    // private int nWavelenghts;

    private char sep = ',';

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

    private void InterpolateWL(List<List<float>> inCoefs, ref List<List<float>> outCoefs, float wl) {

      int nextWl = FindNextWL(inCoefs[0], wl); // first get wl to the right
      // Debug.Log(nextWl + " " + inCoefs[0].Count);
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

    public void SetNWavelengths(int nWavelengths) {
      Debug.Log("Setting " +  nWavelengths + ". Original curves:");
      PrintLists(cameraResponseCurve, mediumSpectralCoefs);
      // Initialize:
      cameraResponseCurveInterpolated = new List<List<float>>();
      mediumSpectralCoefsInterpolated = new List<List<float>>();
      for(int i = 0; i < 4; i++)
      {
        cameraResponseCurveInterpolated.Add(new List<float>());
        mediumSpectralCoefsInterpolated.Add(new List<float>());
      }
      // Set up wavelengths:
      float minwl = cameraResponseCurve[0].First(),
            maxwl = cameraResponseCurve[0].Last(),
            stepwl = (maxwl - minwl) / (float)nWavelengths;
      // Fill every wl:
      for(int i = 0; i < nWavelengths; i++)
      {
        float wl = minwl + stepwl*(float)i; // The wavelength
        cameraResponseCurveInterpolated[0].Add(wl); // Into the responseCurve
        // Interpolate everything else:
        InterpolateWL(cameraResponseCurve, ref cameraResponseCurveInterpolated, wl);
        InterpolateWL(mediumSpectralCoefs, ref mediumSpectralCoefsInterpolated, wl);
      }

      // TestNWavelengths(nWavelengths);
    }

    private void TestNWavelengths(int nWavelengths) {
      bool ok = true;
      int i = 0;
      foreach(var list in mediumSpectralCoefsInterpolated) {
        if (list.Count != nWavelengths) {
          Debug.Log("Not ok on " + i);
          ok = false;
        }
        i++;
      }
      foreach(var list in cameraResponseCurveInterpolated) {
        if (list.Count != nWavelengths) {
          Debug.Log("Not ok on " + i);
          ok = false;
        }
        i++;
      }
      if (!ok) {
        Debug.Log("NOT ok\n------------------");
      }
      else {
        Debug.Log("Number of wls ok\n------------------");
      }

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

    private void PrintLists(List<List<float>> list1, List<List<float>> list2) {
      Debug.Log("There are " + list2[1].Count + " wls");
      for (int i = 0; i<list2[1].Count; i++) {
        Debug.Log(list1[0][i] + " " + list1[1][i] + " " + list1[2][i] + " " + list1[3][i] + " " + list2[1][i] + " " + list2[2][i] + " " + list2[3][i] + "\n");
      }
    }
    public void PrintCurves() {
      PrintLists(cameraResponseCurveInterpolated, mediumSpectralCoefsInterpolated);
    }

}
