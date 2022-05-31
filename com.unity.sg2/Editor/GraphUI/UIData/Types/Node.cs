using UnityEngine;

public class Node
{
    public string name;
    public int version;
    public string displayName;
    public string tooltip;
    public bool hasPreview;

    public static Node CreateFromJSON(string jsonString)
    {
        return JsonUtility.FromJson<Node>(jsonString);
    }
}
