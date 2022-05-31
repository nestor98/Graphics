using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.ShaderGraph.GraphUI.UIData
{
    internal class UI
    {
        private static readonly Dictionary<string, string> strings;

        private UI() { }

        // Adds a string file source to the accessible strings.
        public static void AddSource(TextAsset jsonTextAsset)
        {
            Nodes nodesInJson = JsonUtility.FromJson<Nodes>(jsonTextAsset.text);
            foreach (var node in nodesInJson.nodes)
            {
                strings[$"nodes.{node.name}.tooltip"] = node.tooltip;
                // TODO (Brett) ...
            }
        }

        // Returns a string that is supposed to go in the UI.
        public static string String(string key, string fallback)
        {
            string val = strings[key];
            return System.String.IsNullOrEmpty(val) ? fallback : val;
        }
    }
}
