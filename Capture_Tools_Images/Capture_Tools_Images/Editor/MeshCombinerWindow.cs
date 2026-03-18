using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tools.Editor
{
    /// <summary>
    /// Editor window that analyzes a GameObject hierarchy, collects all child MeshFilter + MeshRenderer pairs,
    /// combines their meshes into a single mesh with one sub-mesh per material, and saves the result to disk.
    /// </summary>
    public class MeshCombinerWindow : EditorWindow
    {
        private const string DEFAULT_OUTPUT_DIRECTORY = "Assets/Meshes/Combined";
        private const string COMBINED_SUFFIX = "_Combined";
        private const int MAX_UINT16_VERTEX_COUNT = 65535;

        /// <summary>
        /// Holds data about a single mesh source discovered during analysis.
        /// </summary>
        private struct CombineEntry
        {
            public Mesh SharedMesh;
            public Material[] SharedMaterials;
            public Matrix4x4 RelativeTransform;
            public int SubMeshCount;
            public bool IsReadable;
        }

        private GameObject targetObject;
        private string outputDirectory = DEFAULT_OUTPUT_DIRECTORY;
        private string outputMeshName = "";
        private bool removeChildRenderers = true;
        private bool dryRun;

        private List<CombineEntry> analysisEntries = new List<CombineEntry>();
        private bool hasAnalysis;
        private int totalVertexCount;
        private int totalTriangleCount;
        private List<Material> discoveredMaterials = new List<Material>();
        private List<string> warnings = new List<string>();

        private Vector2 scrollPosition;

        [MenuItem("Tools/Mesh Combiner")]
        public static void ShowWindow()
        {
            var window = GetWindow<MeshCombinerWindow>("Mesh Combiner");
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += OnSelectionChanged;
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        /// <summary>
        /// Syncs target, mesh name, and auto-analyzes whenever the editor selection changes.
        /// </summary>
        private void OnSelectionChanged()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null || selected == targetObject)
                return;

            targetObject = selected;
            outputMeshName = targetObject.name + COMBINED_SUFFIX;
            hasAnalysis = false;

            AnalyzeMeshes();
            Repaint();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawTargetSection();
            DrawOutputSection();
            DrawOptionsSection();
            DrawAnalysisInfoSection();
            DrawActionButtons();
            DrawWarningsSection();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// Draws the target GameObject selection field.
        /// </summary>
        private void DrawTargetSection()
        {
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            targetObject = (GameObject)EditorGUILayout.ObjectField(
                "Root GameObject", targetObject, typeof(GameObject), true);

            if (EditorGUI.EndChangeCheck())
            {
                hasAnalysis = false;
                if (targetObject != null)
                {
                    outputMeshName = targetObject.name + COMBINED_SUFFIX;
                    AnalyzeMeshes();
                }
            }

            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Draws output path and mesh name fields.
        /// </summary>
        private void DrawOutputSection()
        {
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            outputDirectory = EditorGUILayout.TextField("Output Directory", outputDirectory);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string selectedPath = EditorUtility.SaveFolderPanel(
                    "Select Output Directory", "Assets", "Combined");

                if (!string.IsNullOrEmpty(selectedPath))
                {
                    if (selectedPath.StartsWith(Application.dataPath))
                    {
                        outputDirectory = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Invalid Path",
                            "The selected folder must be inside the Assets directory.", "OK");
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            outputMeshName = EditorGUILayout.TextField("Mesh Name", outputMeshName);

            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Draws toggle options for child renderer cleanup and dry run.
        /// </summary>
        private void DrawOptionsSection()
        {
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
            removeChildRenderers = EditorGUILayout.Toggle("Remove Child Renderers", removeChildRenderers);
            dryRun = EditorGUILayout.Toggle("Dry Run (log only)", dryRun);
            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Displays read-only analysis results: mesh count, vertex/triangle counts, and materials.
        /// </summary>
        private void DrawAnalysisInfoSection()
        {
            if (!hasAnalysis)
                return;

            EditorGUILayout.LabelField("Analysis Results", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.IntField("Meshes Found", analysisEntries.Count);
            EditorGUILayout.IntField("Total Vertices", totalVertexCount);
            EditorGUILayout.IntField("Total Triangles", totalTriangleCount);
            EditorGUILayout.Space(2);

            EditorGUILayout.LabelField("Materials:");
            for (int i = 0; i < discoveredMaterials.Count; i++)
            {
                EditorGUILayout.ObjectField(
                    $"  [{i}]", discoveredMaterials[i], typeof(Material), false);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Draws the Analyze and Combine & Save buttons.
        /// </summary>
        private void DrawActionButtons()
        {
            EditorGUI.BeginDisabledGroup(targetObject == null);

            if (GUILayout.Button("Analyze", GUILayout.Height(28)))
            {
                AnalyzeMeshes();
            }

            EditorGUI.BeginDisabledGroup(!hasAnalysis || analysisEntries.Count == 0);
            if (GUILayout.Button("Combine & Save", GUILayout.Height(32)))
            {
                ExecuteCombinePipeline();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Draws any warnings collected during analysis.
        /// </summary>
        private void DrawWarningsSection()
        {
            if (warnings.Count == 0)
                return;

            EditorGUILayout.LabelField("Warnings", EditorStyles.boldLabel);
            var style = new GUIStyle(EditorStyles.helpBox)
            {
                richText = true,
                wordWrap = true
            };

            foreach (string warning in warnings)
            {
                EditorGUILayout.LabelField(warning, style);
            }
        }

        /// <summary>
        /// Discovers all MeshFilter + MeshRenderer pairs in children of the target and populates analysis data.
        /// </summary>
        private void AnalyzeMeshes()
        {
            analysisEntries.Clear();
            discoveredMaterials.Clear();
            warnings.Clear();
            totalVertexCount = 0;
            totalTriangleCount = 0;
            hasAnalysis = false;

            GameObject source = ResolveSourceObject(out bool isPrefabAsset, out GameObject tempInstance);

            if (source == null)
                return;

            MeshFilter[] meshFilters = source.GetComponentsInChildren<MeshFilter>();

            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter.sharedMesh == null)
                {
                    warnings.Add($"Skipped '{meshFilter.gameObject.name}': MeshFilter has no mesh assigned.");
                    continue;
                }

                MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    warnings.Add($"Skipped '{meshFilter.gameObject.name}': No MeshRenderer found.");
                    continue;
                }

                Mesh mesh = meshFilter.sharedMesh;
                bool isReadable = mesh.isReadable;

                if (!isReadable)
                {
                    warnings.Add(
                        $"'{meshFilter.gameObject.name}' uses mesh '{mesh.name}' which is not readable. " +
                        "Enable Read/Write in the mesh import settings.");
                }

                Matrix4x4 relativeTransform = source.transform.worldToLocalMatrix *
                                              meshFilter.transform.localToWorldMatrix;

                var entry = new CombineEntry
                {
                    SharedMesh = mesh,
                    SharedMaterials = renderer.sharedMaterials,
                    RelativeTransform = relativeTransform,
                    SubMeshCount = mesh.subMeshCount,
                    IsReadable = isReadable
                };

                analysisEntries.Add(entry);
                totalVertexCount += mesh.vertexCount;
                totalTriangleCount += mesh.triangles.Length / 3;
            }

            BuildMaterialsList();

            if (tempInstance != null)
            {
                DestroyImmediate(tempInstance);
            }

            if (analysisEntries.Count == 0)
            {
                EditorUtility.DisplayDialog("No Meshes Found",
                    "No valid MeshFilter + MeshRenderer pairs found in the target's children.", "OK");
                return;
            }

            hasAnalysis = true;
            Repaint();
        }

        /// <summary>
        /// Resolves the source object: if it's a prefab asset, instantiates a temp copy; otherwise uses the scene instance.
        /// </summary>
        private GameObject ResolveSourceObject(out bool isPrefabAsset, out GameObject tempInstance)
        {
            tempInstance = null;
            isPrefabAsset = false;

            if (targetObject == null)
                return null;

            isPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(targetObject) &&
                            !PrefabUtility.IsPartOfPrefabInstance(targetObject);

            if (isPrefabAsset)
            {
                tempInstance = (GameObject)PrefabUtility.InstantiatePrefab(targetObject);
                tempInstance.hideFlags = HideFlags.HideAndDontSave;
                return tempInstance;
            }

            return targetObject;
        }

        /// <summary>
        /// Builds the flat list of materials from all discovered entries, preserving one slot per sub-mesh.
        /// </summary>
        private void BuildMaterialsList()
        {
            discoveredMaterials.Clear();

            foreach (CombineEntry entry in analysisEntries)
            {
                for (int i = 0; i < entry.SubMeshCount; i++)
                {
                    Material mat = (i < entry.SharedMaterials.Length) ? entry.SharedMaterials[i] : null;
                    discoveredMaterials.Add(mat);
                }
            }
        }

        /// <summary>
        /// Runs the full combine pipeline: validate, combine, save, apply.
        /// </summary>
        private void ExecuteCombinePipeline()
        {
            if (!ValidateBeforeCombine())
                return;

            if (dryRun)
            {
                LogDryRun();
                return;
            }

            EditorUtility.DisplayProgressBar("Mesh Combiner", "Combining meshes...", 0.2f);

            Mesh combinedMesh = CombineMeshes();
            if (combinedMesh == null)
            {
                EditorUtility.ClearProgressBar();
                return;
            }

            EditorUtility.DisplayProgressBar("Mesh Combiner", "Saving mesh asset...", 0.6f);

            string savedPath = SaveMeshToDisk(combinedMesh);
            if (string.IsNullOrEmpty(savedPath))
            {
                EditorUtility.ClearProgressBar();
                return;
            }

            Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(savedPath);

            EditorUtility.DisplayProgressBar("Mesh Combiner", "Applying to root...", 0.8f);

            ApplyToRoot(savedMesh);

            EditorUtility.DisplayProgressBar("Mesh Combiner", "Done!", 1f);
            EditorUtility.ClearProgressBar();

            Debug.Log($"[MeshCombiner] Combined {analysisEntries.Count} meshes into '{savedPath}' " +
                      $"({combinedMesh.vertexCount} vertices, {combinedMesh.subMeshCount} sub-meshes).");
        }

        /// <summary>
        /// Validates that the combine operation can proceed (readable meshes, overwrite check).
        /// </summary>
        private bool ValidateBeforeCombine()
        {
            bool hasUnreadable = analysisEntries.Any(e => !e.IsReadable);
            if (hasUnreadable)
            {
                EditorUtility.DisplayDialog("Non-Readable Meshes",
                    "One or more source meshes are not readable. Enable Read/Write in their import settings before combining.",
                    "OK");
                return false;
            }

            bool rootHasMeshFilter = targetObject.GetComponent<MeshFilter>() != null;
            bool rootHasMeshRenderer = targetObject.GetComponent<MeshRenderer>() != null;
            if (rootHasMeshFilter || rootHasMeshRenderer)
            {
                bool proceed = EditorUtility.DisplayDialog("Overwrite Warning",
                    "The root GameObject already has a MeshFilter and/or MeshRenderer. These will be overwritten. Continue?",
                    "Continue", "Cancel");
                if (!proceed)
                    return false;
            }

            string assetPath = Path.Combine(outputDirectory, outputMeshName + ".asset");
            if (File.Exists(assetPath))
            {
                bool overwrite = EditorUtility.DisplayDialog("Asset Exists",
                    $"A mesh asset already exists at '{assetPath}'. Overwrite?",
                    "Overwrite", "Cancel");
                if (!overwrite)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Logs what the combine operation would do without modifying anything.
        /// </summary>
        private void LogDryRun()
        {
            Debug.Log("[MeshCombiner] DRY RUN -- no changes will be made.");
            Debug.Log($"[MeshCombiner] Would combine {analysisEntries.Count} mesh entries " +
                      $"({totalVertexCount} vertices, {totalTriangleCount} triangles).");
            Debug.Log($"[MeshCombiner] Would create {discoveredMaterials.Count} sub-meshes " +
                      $"(one per material slot).");
            Debug.Log($"[MeshCombiner] Would save to: {Path.Combine(outputDirectory, outputMeshName + ".asset")}");
            Debug.Log($"[MeshCombiner] Index format: " +
                      (totalVertexCount > MAX_UINT16_VERTEX_COUNT ? "UInt32" : "UInt16"));

            for (int i = 0; i < discoveredMaterials.Count; i++)
            {
                string matName = discoveredMaterials[i] != null ? discoveredMaterials[i].name : "null";
                Debug.Log($"[MeshCombiner]   Sub-mesh [{i}] -> Material: {matName}");
            }

            if (removeChildRenderers)
            {
                Debug.Log("[MeshCombiner] Would disable child MeshRenderer + MeshFilter components.");
            }
        }

        /// <summary>
        /// Combines all analyzed mesh entries into a single mesh with one sub-mesh per material slot.
        /// </summary>
        private Mesh CombineMeshes()
        {
            var combineInstances = new List<CombineInstance>();
            var materials = new List<Material>();

            foreach (CombineEntry entry in analysisEntries)
            {
                for (int i = 0; i < entry.SubMeshCount; i++)
                {
                    var ci = new CombineInstance
                    {
                        mesh = entry.SharedMesh,
                        subMeshIndex = i,
                        transform = entry.RelativeTransform
                    };
                    combineInstances.Add(ci);

                    Material mat = (i < entry.SharedMaterials.Length) ? entry.SharedMaterials[i] : null;
                    materials.Add(mat);
                }
            }

            var combinedMesh = new Mesh();
            combinedMesh.name = outputMeshName;

            if (totalVertexCount > MAX_UINT16_VERTEX_COUNT)
            {
                combinedMesh.indexFormat = IndexFormat.UInt32;
            }

            combinedMesh.CombineMeshes(combineInstances.ToArray(), false, true);
            combinedMesh.RecalculateBounds();

            discoveredMaterials = materials;
            return combinedMesh;
        }

        /// <summary>
        /// Saves the combined mesh to disk as a .asset file.
        /// </summary>
        private string SaveMeshToDisk(Mesh mesh)
        {
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                AssetDatabase.Refresh();
            }

            string assetPath = Path.Combine(outputDirectory, outputMeshName + ".asset");

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                AssetDatabase.SaveAssets();
                Debug.Log($"[MeshCombiner] Overwritten existing mesh asset at '{assetPath}'.");
                return assetPath;
            }

            AssetDatabase.CreateAsset(mesh, assetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MeshCombiner] Saved combined mesh to '{assetPath}'.");
            return assetPath;
        }

        /// <summary>
        /// Applies the combined mesh and materials to the root GameObject with full Undo support.
        /// Optionally disables child renderers.
        /// </summary>
        private void ApplyToRoot(Mesh savedMesh)
        {
            Undo.SetCurrentGroupName("Mesh Combiner - Apply Combined Mesh");
            int undoGroup = Undo.GetCurrentGroup();

            MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = Undo.AddComponent<MeshFilter>(targetObject);
            }
            else
            {
                Undo.RecordObject(meshFilter, "Assign combined mesh");
            }
            meshFilter.sharedMesh = savedMesh;

            MeshRenderer meshRenderer = targetObject.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = Undo.AddComponent<MeshRenderer>(targetObject);
            }
            else
            {
                Undo.RecordObject(meshRenderer, "Assign combined materials");
            }
            meshRenderer.sharedMaterials = discoveredMaterials.ToArray();

            if (removeChildRenderers)
            {
                DisableChildRenderers();
            }

            EditorUtility.SetDirty(targetObject);
            Undo.CollapseUndoOperations(undoGroup);
        }

        /// <summary>
        /// Disables (not destroys) all child MeshRenderer and MeshFilter components to preserve
        /// colliders and other components on those GameObjects.
        /// </summary>
        private void DisableChildRenderers()
        {
            MeshFilter[] childFilters = targetObject.GetComponentsInChildren<MeshFilter>();
            MeshRenderer[] childRenderers = targetObject.GetComponentsInChildren<MeshRenderer>();

            foreach (MeshRenderer renderer in childRenderers)
            {
                if (renderer.gameObject == targetObject)
                    continue;

                Undo.RecordObject(renderer, "Disable child MeshRenderer");
                renderer.enabled = false;
            }

            foreach (MeshFilter filter in childFilters)
            {
                if (filter.gameObject == targetObject)
                    continue;

                Undo.DestroyObjectImmediate(filter);
            }
        }
    }
}
