using UnityEngine;

public class Node
{
    public int Version;
    public string Name;
    public string DisplayName;
    public string Tooltip;
    public bool HasPreview;

    public static Node CreateFromJSON(string jsonString)
    {
        return JsonUtility.FromJson<Node>(jsonString);
    }
}
