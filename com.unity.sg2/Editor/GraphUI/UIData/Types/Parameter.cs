using UnityEngine;

public class Parameter
{
    public string Name;
    public string DisplayName;
    public string Tooltip;
    public bool UseColor;
    public bool UseSlider;
    public bool InspectorOnly;

    public static Parameter CreateFromJSON(string jsonString)
    {
        return JsonUtility.FromJson<Parameter>(jsonString);
    }
}
