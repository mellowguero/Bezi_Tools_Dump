using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TextureAtlas.Editor
{
    /// <summary>
    /// Standalone EditorWindow for texture atlas generation and prefab remapping.
    /// Provides a simplified workflow without needing to manually create ScriptableObjects.
    /// </summary>
    public class TextureAtlasWindow : EditorWindow
    {
        private const string PREFS_OUTPUT_FOLDER = "TextureAtlas_OutputFolder";
        private const string PREFS_ASSET_PREFIX = "TextureAtlas_AssetPrefix";

        private const string DEFAULT_OUTPUT_FOLDER = "Assets/Generated/Atlas";
        private const string DEFAULT_ASSET_PREFIX = "Atlas";

        private const float BROWSE_BUTTON_WIDTH = 80f;
        private const float ACTION_BUTTON_HEIGHT = 30f;

        // Atlas generation
        private TextureAtlasTask atlasTask;
        private List<Material> inlineMaterials = new List<Material>();
        private int maxAtlasSize = 2048;
        private int cellSize = 64;
        private int padding = 2;
        private bool deleteOriginalMaterials;
        private string outputFolder;
        private string assetPrefix;

        // Remap
        private AtlasRemapTask remapTask;
        private List<GameObject> inlinePrefabs = new List<GameObject>();
        private bool createMeshCopies = true;

        private string lastOperationResult = "";
        private Vector2 scrollPosition;
        private int activeTab;

        /// <summary>Opens the window from the menu bar.</summary>
        [MenuItem("Tools/Texture Atlas Generator")]
        public static void ShowWindow()
        {
            var window = GetWindow<TextureAtlasWindow>("Texture Atlas Generator");
            window.minSize = new Vector2(420, 500);
        }

        private void OnEnable()
        {
            outputFolder = EditorPrefs.GetString(PREFS_OUTPUT_FOLDER, DEFAULT_OUTPUT_FOLDER);
            assetPrefix = EditorPrefs.GetString(PREFS_ASSET_PREFIX, DEFAULT_ASSET_PREFIX);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.LabelField("Texture Atlas Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            activeTab = GUILayout.Toolbar(activeTab, new[] { "Generate Atlas", "Remap Prefabs" });
            EditorGUILayout.Space(8);

            if (activeTab == 0)
            {
                DrawAtlasTab();
            }
            else
            {
                DrawRemapTab();
            }

            if (!string.IsNullOrEmpty(lastOperationResult))
            {
                EditorGUILayout.Space(8);
                DrawSeparator();
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(lastOperationResult, MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
        }

        // ──────────────────────────────────────────────────────────────
        //  Atlas Tab
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Draws the atlas generation tab.
        /// </summary>
        private void DrawAtlasTab()
        {
            atlasTask = (TextureAtlasTask)EditorGUILayout.ObjectField(
                "Atlas Task (Optional)", atlasTask, typeof(TextureAtlasTask), false);

            EditorGUILayout.Space(4);
            DrawSeparator();
            EditorGUILayout.Space(4);

            if (atlasTask != null)
            {
                DrawAtlasTaskFields();
            }
            else
            {
                DrawInlineAtlasFields();
            }

            EditorGUILayout.Space(8);
            DrawAtlasActionButtons();
        }

        /// <summary>
        /// Draws editable fields for an assigned atlas task.
        /// </summary>
        private void DrawAtlasTaskFields()
        {
            EditorGUILayout.HelpBox("Editing task asset directly.", MessageType.Info);
            EditorGUILayout.Space(4);

            DrawMaterialList(atlasTask.SourceMaterials, atlasTask);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Atlas Settings", EditorStyles.boldLabel);

            int[] sizeOptions = { 256, 512, 1024, 2048, 4096 };
            string[] sizeLabels = { "256", "512", "1024", "2048", "4096" };
            int sizeIndex = System.Array.IndexOf(sizeOptions, atlasTask.MaxAtlasSize);
            if (sizeIndex < 0) sizeIndex = 3;
            int newSizeIndex = EditorGUILayout.Popup("Max Atlas Size", sizeIndex, sizeLabels);
            if (newSizeIndex != sizeIndex) { atlasTask.MaxAtlasSize = sizeOptions[newSizeIndex]; EditorUtility.SetDirty(atlasTask); }

            int newCellSize = EditorGUILayout.IntField("Cell Size", atlasTask.CellSize);
            if (newCellSize != atlasTask.CellSize) { atlasTask.CellSize = newCellSize; EditorUtility.SetDirty(atlasTask); }

            int newPadding = EditorGUILayout.IntSlider("Padding", atlasTask.Padding, 0, 8);
            if (newPadding != atlasTask.Padding) { atlasTask.Padding = newPadding; EditorUtility.SetDirty(atlasTask); }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            string newFolder = EditorGUILayout.TextField("Output Folder", atlasTask.OutputFolder);
            if (newFolder != atlasTask.OutputFolder) { atlasTask.OutputFolder = newFolder; EditorUtility.SetDirty(atlasTask); }
            if (GUILayout.Button("Browse...", GUILayout.Width(BROWSE_BUTTON_WIDTH)))
            {
                string path = BrowseForFolder();
                if (path != null) { atlasTask.OutputFolder = path; EditorUtility.SetDirty(atlasTask); }
            }
            EditorGUILayout.EndHorizontal();

            string newPrefix = EditorGUILayout.TextField("Asset Prefix", atlasTask.AssetPrefix);
            if (newPrefix != atlasTask.AssetPrefix) { atlasTask.AssetPrefix = newPrefix; EditorUtility.SetDirty(atlasTask); }
        }

        /// <summary>
        /// Draws inline atlas configuration fields.
        /// </summary>
        private void DrawInlineAtlasFields()
        {
            DrawMaterialList(inlineMaterials, null);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Atlas Settings", EditorStyles.boldLabel);

            int[] sizeOptions = { 256, 512, 1024, 2048, 4096 };
            string[] sizeLabels = { "256", "512", "1024", "2048", "4096" };
            int sizeIndex = System.Array.IndexOf(sizeOptions, maxAtlasSize);
            if (sizeIndex < 0) sizeIndex = 3;
            sizeIndex = EditorGUILayout.Popup("Max Atlas Size", sizeIndex, sizeLabels);
            maxAtlasSize = sizeOptions[sizeIndex];

            cellSize = EditorGUILayout.IntField("Cell Size", cellSize);
            padding = EditorGUILayout.IntSlider("Padding", padding, 0, 8);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);
            if (GUILayout.Button("Browse...", GUILayout.Width(BROWSE_BUTTON_WIDTH)))
            {
                string path = BrowseForFolder();
                if (path != null) { outputFolder = path; EditorPrefs.SetString(PREFS_OUTPUT_FOLDER, outputFolder); }
            }
            EditorGUILayout.EndHorizontal();

            assetPrefix = EditorGUILayout.TextField("Asset Prefix", assetPrefix);
            EditorPrefs.SetString(PREFS_ASSET_PREFIX, assetPrefix);
        }

        /// <summary>
        /// Draws atlas generation action buttons.
        /// </summary>
        private void DrawAtlasActionButtons()
        {
            DrawSeparator();
            EditorGUILayout.Space(4);

            if (atlasTask != null)
            {
                bool canGenerate = atlasTask.SourceMaterials.Count > 0;
                EditorGUI.BeginDisabledGroup(!canGenerate);
                if (GUILayout.Button("Generate Atlas", GUILayout.Height(ACTION_BUTTON_HEIGHT)))
                {
                    TextureAtlasGenerator.ProcessTask(atlasTask);
                    lastOperationResult = atlasTask.IsProcessed
                        ? "Atlas generated successfully. Switch to the Remap tab to apply it to prefabs."
                        : "Atlas generation failed. Check console for details.";
                }
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                bool canCreate = inlineMaterials.Count > 0;
                EditorGUI.BeginDisabledGroup(!canCreate);

                if (GUILayout.Button("Create Task Asset & Generate", GUILayout.Height(ACTION_BUTTON_HEIGHT)))
                {
                    string savePath = EditorUtility.SaveFilePanelInProject(
                        "Save Atlas Task", $"{assetPrefix}_AtlasTask", "asset",
                        "Choose where to save the atlas task asset.");

                    if (!string.IsNullOrEmpty(savePath))
                    {
                        TextureAtlasTask task = CreateInstance<TextureAtlasTask>();
                        task.SourceMaterials = new List<Material>(inlineMaterials);
                        task.MaxAtlasSize = maxAtlasSize;
                        task.CellSize = cellSize;
                        task.Padding = padding;
                        task.DeleteOriginalMaterials = deleteOriginalMaterials;
                        task.OutputFolder = outputFolder;
                        task.AssetPrefix = assetPrefix;

                        AssetDatabase.CreateAsset(task, savePath);
                        AssetDatabase.SaveAssets();

                        TextureAtlasGenerator.ProcessTask(task);
                        atlasTask = task;
                        Selection.activeObject = task;

                        lastOperationResult = task.IsProcessed
                            ? $"Atlas generated at {savePath}. Switch to Remap tab to apply."
                            : "Atlas generation failed. Check console.";
                    }
                }

                EditorGUI.EndDisabledGroup();
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  Remap Tab
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Draws the prefab remap tab.
        /// </summary>
        private void DrawRemapTab()
        {
            EditorGUILayout.LabelField("Atlas Source", EditorStyles.boldLabel);

            TextureAtlasTask atlasSource = atlasTask;
            if (remapTask != null && remapTask.AtlasTask != null)
            {
                atlasSource = remapTask.AtlasTask;
            }

            atlasSource = (TextureAtlasTask)EditorGUILayout.ObjectField(
                "Atlas Task", atlasSource, typeof(TextureAtlasTask), false);

            if (remapTask != null)
            {
                remapTask.AtlasTask = atlasSource;
                EditorUtility.SetDirty(remapTask);
            }
            atlasTask = atlasSource;

            if (atlasSource == null || !atlasSource.IsProcessed)
            {
                EditorGUILayout.HelpBox(
                    "Assign a processed atlas task. Generate one in the 'Generate Atlas' tab first.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField("Atlas Material", atlasSource.GeneratedMaterial, typeof(Material), false);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.LabelField("Mapped Materials", atlasSource.AtlasMapping.Count.ToString());
            }

            EditorGUILayout.Space(4);
            DrawSeparator();
            EditorGUILayout.Space(4);

            remapTask = (AtlasRemapTask)EditorGUILayout.ObjectField(
                "Remap Task (Optional)", remapTask, typeof(AtlasRemapTask), false);

            EditorGUILayout.Space(4);

            if (remapTask != null)
            {
                DrawRemapTaskFields();
            }
            else
            {
                DrawInlineRemapFields();
            }

            EditorGUILayout.Space(8);
            DrawRemapActionButtons(atlasSource);
        }

        /// <summary>
        /// Draws editable fields for an assigned remap task.
        /// </summary>
        private void DrawRemapTaskFields()
        {
            DrawPrefabList(remapTask.TargetPrefabs, remapTask, remapTask.AtlasTask);

            EditorGUILayout.Space(4);
            remapTask.CreateMeshCopies = EditorGUILayout.Toggle("Create Mesh Copies", remapTask.CreateMeshCopies);

            EditorGUILayout.BeginHorizontal();
            remapTask.MeshOutputFolder = EditorGUILayout.TextField("Mesh Output Folder", remapTask.MeshOutputFolder);
            if (GUILayout.Button("Browse...", GUILayout.Width(BROWSE_BUTTON_WIDTH)))
            {
                string path = BrowseForFolder();
                if (path != null) { remapTask.MeshOutputFolder = path; EditorUtility.SetDirty(remapTask); }
            }
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(remapTask.MeshOutputFolder))
            {
                EditorGUILayout.LabelField($"Using: {remapTask.GetEffectiveOutputFolder()}", EditorStyles.miniLabel);
            }
        }

        /// <summary>
        /// Draws inline remap fields when no remap task is assigned.
        /// </summary>
        private void DrawInlineRemapFields()
        {
            DrawPrefabList(inlinePrefabs, null, atlasTask);

            EditorGUILayout.Space(4);
            createMeshCopies = EditorGUILayout.Toggle("Create Mesh Copies", createMeshCopies);
        }

        /// <summary>
        /// Draws remap action buttons.
        /// </summary>
        private void DrawRemapActionButtons(TextureAtlasTask atlasSource)
        {
            DrawSeparator();
            EditorGUILayout.Space(4);

            bool hasAtlas = atlasSource != null && atlasSource.IsProcessed;

            if (remapTask != null)
            {
                bool canRemap = hasAtlas && remapTask.TargetPrefabs.Count > 0;
                EditorGUI.BeginDisabledGroup(!canRemap);
                if (GUILayout.Button("Remap Prefabs", GUILayout.Height(ACTION_BUTTON_HEIGHT)))
                {
                    AtlasRemapGenerator.ProcessTask(remapTask);
                    lastOperationResult = remapTask.IsProcessed
                        ? "Prefabs remapped successfully."
                        : "Remap failed. Check console.";
                }
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                bool canRemap = hasAtlas && inlinePrefabs.Count > 0;
                EditorGUI.BeginDisabledGroup(!canRemap);

                if (GUILayout.Button("Create Remap Task & Apply", GUILayout.Height(ACTION_BUTTON_HEIGHT)))
                {
                    string savePath = EditorUtility.SaveFilePanelInProject(
                        "Save Remap Task", $"{(atlasSource != null ? atlasSource.AssetPrefix : "Atlas")}_RemapTask",
                        "asset", "Choose where to save the remap task asset.");

                    if (!string.IsNullOrEmpty(savePath))
                    {
                        AtlasRemapTask task = CreateInstance<AtlasRemapTask>();
                        task.AtlasTask = atlasSource;
                        task.TargetPrefabs = new List<GameObject>(inlinePrefabs);
                        task.CreateMeshCopies = createMeshCopies;

                        AssetDatabase.CreateAsset(task, savePath);
                        AssetDatabase.SaveAssets();

                        AtlasRemapGenerator.ProcessTask(task);
                        remapTask = task;

                        lastOperationResult = task.IsProcessed
                            ? "Prefabs remapped successfully."
                            : "Remap failed. Check console.";
                    }
                }

                EditorGUI.EndDisabledGroup();
            }
        }

        // ──────────────────────────────────────────────────────────────
        //  Shared UI Helpers
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Draws a material list with Add Selected / Remove controls.
        /// </summary>
        private static void DrawMaterialList(List<Material> materials, Object undoTarget)
        {
            EditorGUILayout.LabelField("Source Materials", EditorStyles.boldLabel);

            if (GUILayout.Button("Add Selected Materials"))
            {
                if (undoTarget != null) Undo.RecordObject(undoTarget, "Add Selected Materials");
                foreach (Object obj in Selection.objects)
                {
                    if (obj is Material mat && !materials.Contains(mat))
                    {
                        materials.Add(mat);
                    }
                }
                if (undoTarget != null) EditorUtility.SetDirty(undoTarget);
            }

            for (int i = materials.Count - 1; i >= 0; i--)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(materials[i], typeof(Material), false);
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    if (undoTarget != null) Undo.RecordObject(undoTarget, "Remove Material");
                    materials.RemoveAt(i);
                    if (undoTarget != null) EditorUtility.SetDirty(undoTarget);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField($"Count: {materials.Count}", EditorStyles.miniLabel);
        }

        /// <summary>
        /// Draws a prefab list with Add Selected / Remove controls and match count.
        /// </summary>
        private static void DrawPrefabList(List<GameObject> prefabs, Object undoTarget, TextureAtlasTask atlasTask)
        {
            EditorGUILayout.LabelField("Target Prefabs", EditorStyles.boldLabel);

            if (GUILayout.Button("Add Selected Prefabs"))
            {
                if (undoTarget != null) Undo.RecordObject(undoTarget, "Add Selected Prefabs");
                foreach (Object obj in Selection.objects)
                {
                    if (obj is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go) && !prefabs.Contains(go))
                    {
                        prefabs.Add(go);
                    }
                }
                if (undoTarget != null) EditorUtility.SetDirty(undoTarget);
            }

            for (int i = prefabs.Count - 1; i >= 0; i--)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(prefabs[i], typeof(GameObject), false);
                EditorGUI.EndDisabledGroup();

                if (prefabs[i] != null && atlasTask != null && atlasTask.IsProcessed)
                {
                    var renderers = prefabs[i].GetComponentsInChildren<Renderer>(true);
                    int matchCount = 0;
                    int totalMats = 0;
                    foreach (var r in renderers)
                    {
                        foreach (Material mat in r.sharedMaterials)
                        {
                            totalMats++;
                            if (mat != null && atlasTask.TryGetAtlasRect(mat, out _))
                                matchCount++;
                        }
                    }
                    EditorGUILayout.LabelField($"{matchCount}/{totalMats}", EditorStyles.miniLabel, GUILayout.Width(40));
                }

                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    if (undoTarget != null) Undo.RecordObject(undoTarget, "Remove Prefab");
                    prefabs.RemoveAt(i);
                    if (undoTarget != null) EditorUtility.SetDirty(undoTarget);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField($"Count: {prefabs.Count}", EditorStyles.miniLabel);
        }

        /// <summary>
        /// Opens a folder browser and returns the relative Assets path, or null if cancelled.
        /// </summary>
        private static string BrowseForFolder()
        {
            string selectedPath = EditorUtility.OpenFolderPanel("Select Folder", Application.dataPath, "");
            if (string.IsNullOrEmpty(selectedPath)) return null;

            string dataPath = Application.dataPath.Replace("\\", "/");
            selectedPath = selectedPath.Replace("\\", "/");

            if (selectedPath.StartsWith(dataPath))
            {
                return "Assets" + selectedPath.Substring(dataPath.Length);
            }

            Debug.LogWarning("[TextureAtlasWindow] Folder must be inside the Assets directory.");
            return null;
        }

        /// <summary>
        /// Draws a horizontal line separator.
        /// </summary>
        private static void DrawSeparator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.5f));
        }
    }
}
