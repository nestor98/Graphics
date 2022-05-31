using System.Collections.Generic;

namespace UnityEditor.ShaderGraph.GraphUI.UIData
{
    internal class UI
    {
        private static readonly Dictionary<string, string> strings;

        private UI() { }

        // Adds a string file source to the accessible strings.
        public static void AddSource(string filePath)
        {
            // parse JSON
            // add all values (last wins)
        }

        // Returns a string that is supposed to go in the UI.
        public static string String(string key, string fallback)
        {
            string val = strings[key];
            return System.String.IsNullOrEmpty(val) ? fallback : val;
        }
    }
}
