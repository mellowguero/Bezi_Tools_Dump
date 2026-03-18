using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Static class that drives zoo scene generation — iterates categories, places category
/// container GameObjects, instantiates/previews prefabs, and guarantees no category overlap
/// by advancing a world-space cursor along the X axis.
/// </summary>
public static class ZooSceneGenerator
{
    private const string RootObjectName = "[ZooScene]";
    private const string UndoGroupName = "Generate Zoo Scene";

    /// <summary>
    /// Generates the zoo scene from the given config in the currently active scene.
    /// </summary>
    public static void Generate(ZooSceneConfig config)
    {
        if (config == null)
        {
            Debug.LogWarning("[ZooSceneGenerator] No config provided.");
            return;
        }

        if (config.categories == null || config.categories.Count == 0)
        {
            Debug.LogWarning("[ZooSceneGenerator] Config has no categories defined.");
            return;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoGroupName);

        Clear();

        GameObject root = new GameObject(RootObjectName);
        Undo.RegisterCreatedObjectUndo(root, UndoGroupName);

        float worldCursorX = 0f;

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (var category in config.categories)
            {
                if (string.IsNullOrEmpty(category.folderPath))
                {
                    Debug.LogWarning($"[ZooSceneGenerator] Category '{category.displayName}' has no folder path set. Skipping.");
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { category.folderPath });
                if (guids.Length == 0)
                {
                    Debug.LogWarning($"[ZooSceneGenerator] No prefabs found in folder '{category.folderPath}' for category '{category.displayName}'. Skipping.");
                    continue;
                }

                string[] prefabPaths = guids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(p => p)
                    .ToArray();

                // Compute layout positions
                List<Vector3> localPositions;
                if (config.layoutMode == LayoutMode.AutoFit)
                    localPositions = ZooPrefabLayoutEngine.ComputeAutoFitPositions(prefabPaths, config.autoFitSettings);
                else
                    localPositions = ZooPrefabLayoutEngine.ComputeGridPositions(prefabPaths, config.gridSettings);

                // Compute per-prefab bounds
                var perPrefabBounds = new List<Bounds>(prefabPaths.Length);
                var prefabAssets = new List<GameObject>(prefabPaths.Length);

                foreach (var path in prefabPaths)
                {
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (asset == null)
                    {
                        Debug.LogWarning($"[ZooSceneGenerator] Could not load prefab at: {path}. Using unit bounds.");
                        perPrefabBounds.Add(new Bounds(Vector3.zero, Vector3.one));
                        prefabAssets.Add(null);
                        continue;
                    }

                    prefabAssets.Add(asset);
                    perPrefabBounds.Add(ZooPrefabLayoutEngine.ComputePrefabBounds(asset));
                }

                Bounds footprint = ZooPrefabLayoutEngine.ComputeCategoryFootprint(localPositions, perPrefabBounds);

                // Position container so content starts flush at worldCursorX
                float containerX = worldCursorX - footprint.min.x;
                var categoryContainer = new GameObject(string.IsNullOrEmpty(category.displayName)
                    ? category.folderPath
                    : category.displayName);

                categoryContainer.transform.SetParent(root.transform, false);
                categoryContainer.transform.position = new Vector3(containerX, 0f, 0f);
                Undo.RegisterCreatedObjectUndo(categoryContainer, UndoGroupName);

                // Instantiate or preview each prefab
                for (int i = 0; i < prefabPaths.Length; i++)
                {
                    Vector3 localPos = i < localPositions.Count ? localPositions[i] : Vector3.zero;

                    if (config.previewMode)
                    {
                        // Create a placeholder cube scaled to prefab bounds
                        Bounds bounds = i < perPrefabBounds.Count ? perPrefabBounds[i] : new Bounds(Vector3.zero, Vector3.one);
                        var placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        placeholder.name = System.IO.Path.GetFileNameWithoutExtension(prefabPaths[i]) + " [Preview]";
                        placeholder.transform.SetParent(categoryContainer.transform, false);
                        placeholder.transform.localPosition = localPos;
                        placeholder.transform.localScale = bounds.size == Vector3.zero ? Vector3.one : bounds.size;

                        // Disable renderer so it shows as wireframe only in Scene view
                        var renderer = placeholder.GetComponent<MeshRenderer>();
                        if (renderer != null)
                            renderer.enabled = false;

                        Undo.RegisterCreatedObjectUndo(placeholder, UndoGroupName);
                    }
                    else
                    {
                        if (prefabAssets[i] == null)
                            continue;

                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAssets[i], categoryContainer.transform);
                        instance.transform.localPosition = localPos;
                        Undo.RegisterCreatedObjectUndo(instance, UndoGroupName);
                    }
                }

                worldCursorX += footprint.size.x + config.categorySpacing;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    /// <summary>
    /// Removes all generated content by destroying the root sentinel object.
    /// </summary>
    public static void Clear()
    {
        var root = GameObject.Find(RootObjectName);
        if (root != null)
            Undo.DestroyObjectImmediate(root);
    }
}
