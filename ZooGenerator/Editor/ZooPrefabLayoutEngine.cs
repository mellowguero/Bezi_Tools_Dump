using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Static utility class responsible for computing per-prefab world-space positions
/// within a single category, and for computing prefab bounds from uninstantiated prefab assets.
/// </summary>
public static class ZooPrefabLayoutEngine
{
    /// <summary>
    /// Returns local positions (relative to the category origin) for each prefab path using a grid layout.
    /// </summary>
    public static List<Vector3> ComputeGridPositions(IList<string> prefabPaths, GridSettings settings)
    {
        var positions = new List<Vector3>(prefabPaths.Count);
        int maxColumns = Mathf.Max(1, settings.maxColumns);

        for (int i = 0; i < prefabPaths.Count; i++)
        {
            int col = i % maxColumns;
            int row = i / maxColumns;
            positions.Add(new Vector3(col * settings.spacingX, 0f, row * settings.spacingZ));
        }

        return positions;
    }

    /// <summary>
    /// Returns local positions (relative to the category origin) for each prefab path,
    /// packing items by their actual XZ footprint with configurable padding.
    /// </summary>
    public static List<Vector3> ComputeAutoFitPositions(IList<string> prefabPaths, AutoFitSettings settings)
    {
        var positions = new List<Vector3>(prefabPaths.Count);

        float cursorX = 0f;
        float cursorZ = 0f;
        float rowMaxZ = 0f;

        for (int i = 0; i < prefabPaths.Count; i++)
        {
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[i]);
            if (prefabAsset == null)
            {
                Debug.LogWarning($"[ZooPrefabLayoutEngine] Could not load prefab at path: {prefabPaths[i]}. Skipping.");
                positions.Add(Vector3.zero);
                continue;
            }

            Bounds bounds = ComputePrefabBounds(prefabAsset);
            float halfWidth = bounds.size.x * 0.5f;
            float halfDepth = bounds.size.z * 0.5f;

            positions.Add(new Vector3(cursorX + halfWidth, 0f, cursorZ + halfDepth));

            cursorX += bounds.size.x + settings.padding;
            rowMaxZ = Mathf.Max(rowMaxZ, bounds.size.z);

            if (cursorX > settings.maxRowWidth && i < prefabPaths.Count - 1)
            {
                cursorX = 0f;
                cursorZ += rowMaxZ + settings.padding;
                rowMaxZ = 0f;
            }
        }

        return positions;
    }

    /// <summary>
    /// Computes an approximate Bounds of a prefab asset in the prefab's root local space.
    /// </summary>
    public static Bounds ComputePrefabBounds(GameObject prefabAsset)
    {
        var meshFilters = prefabAsset.GetComponentsInChildren<MeshFilter>(true);

        if (meshFilters == null || meshFilters.Length == 0)
            return new Bounds(Vector3.zero, Vector3.one);

        bool initialized = false;
        Bounds combined = new Bounds();

        foreach (var meshFilter in meshFilters)
        {
            if (meshFilter.sharedMesh == null)
                continue;

            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            Matrix4x4 localToRoot = meshFilter.transform.localToWorldMatrix;

            // Transform all 8 corners of the mesh bounds into prefab root space
            Vector3 center = meshBounds.center;
            Vector3 extents = meshBounds.extents;

            Vector3[] corners = new Vector3[8]
            {
                center + new Vector3(-extents.x, -extents.y, -extents.z),
                center + new Vector3( extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x,  extents.y, -extents.z),
                center + new Vector3( extents.x,  extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y,  extents.z),
                center + new Vector3( extents.x, -extents.y,  extents.z),
                center + new Vector3(-extents.x,  extents.y,  extents.z),
                center + new Vector3( extents.x,  extents.y,  extents.z),
            };

            foreach (var corner in corners)
            {
                Vector3 worldCorner = localToRoot.MultiplyPoint3x4(corner);
                if (!initialized)
                {
                    combined = new Bounds(worldCorner, Vector3.zero);
                    initialized = true;
                }
                else
                {
                    combined.Encapsulate(worldCorner);
                }
            }
        }

        return initialized ? combined : new Bounds(Vector3.zero, Vector3.one);
    }

    /// <summary>
    /// Returns the combined Bounds of a set of (position, bounds) pairs.
    /// </summary>
    public static Bounds ComputeCategoryFootprint(IList<Vector3> positions, IList<Bounds> perPrefabBounds)
    {
        bool initialized = false;
        Bounds combined = new Bounds();

        for (int i = 0; i < positions.Count && i < perPrefabBounds.Count; i++)
        {
            Bounds shifted = new Bounds(positions[i] + perPrefabBounds[i].center, perPrefabBounds[i].size);

            if (!initialized)
            {
                combined = shifted;
                initialized = true;
            }
            else
            {
                combined.Encapsulate(shifted);
            }
        }

        return initialized ? combined : new Bounds(Vector3.zero, Vector3.zero);
    }
}
