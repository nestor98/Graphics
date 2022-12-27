using System.Collections;
using System.Collections.Generic;

using System;
using UnityEngine;
using System.Globalization;

using System.IO;

public static class CSVReader
{
    public static List<List<float>> ReadRows(string file, char sep)
    {
        using (var reader = new StreamReader(file))
        {
            //List<string> ListA = new List<string>();
            //List<string> ListB = new List<string>();
            List < List<float> > list = new List<List<float>>();
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                //var values = line.Split(';');
                var values = Array.ConvertAll(line.Split(sep), s => float.Parse(s, CultureInfo.InvariantCulture));

                for (int i = 0; i < values.Length; i++)
                {
                    // Debug.Log(values[i]);
                    list.Add(new List<float>());
                    list[i].Add(values[i]);
                }

                //ListA.Add(values[0]);
                //ListB.Add(values[1]);
            }
            return list;
        }
    }


    public static List<List<float>> ReadColumns(string file, char sep)
    {
        using (var reader = new StreamReader(file))
        {
            //List<string> ListA = new List<string>();
            //List<string> ListB = new List<string>();

            List<List<float>> list = new List<List<float>>();
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                //var values = line.Split(';');
                var values = Array.ConvertAll(line.Split(sep), s => float.Parse(s, CultureInfo.InvariantCulture));

                //Copy(values, list[i++], values.Length);
                list.Add(new List<float>(values));
                //List[i].Add(values);

                //ListA.Add(values[0]);
                //ListB.Add(values[1]);
            }
            return list;
        }
    }

}
