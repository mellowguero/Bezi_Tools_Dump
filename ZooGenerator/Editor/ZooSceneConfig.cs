using System.Collections.Generic;
using UnityEngine;

public enum LayoutMode { Grid, AutoFit }

[System.Serializable]
public class CategoryDefinition
{
    public string displayName;
    public string folderPath; // e.g. "Assets/PolygonPrototype/Prefabs/Buildings/Polygon"
}

[System.Serializable]
public class GridSettings
{
    public float spacingX = 5f;
    public float spacingZ = 5f;
    public int maxColumns = 10;
}

[System.Serializable]
public class AutoFitSettings
{
    public float padding = 1f;
    public float maxRowWidth = 100f; // wrap to next row beyond this world-unit width
}

[CreateAssetMenu(menuName = "Zoo Scene/Config", fileName = "ZooSceneConfig")]
public class ZooSceneConfig : ScriptableObject
{
    public List<CategoryDefinition> categories = new();
    public LayoutMode layoutMode = LayoutMode.Grid;
    public GridSettings gridSettings = new();
    public AutoFitSettings autoFitSettings = new();
    public float categorySpacing = 20f;
    public bool previewMode = false; // bounds-only placeholder mode
}
