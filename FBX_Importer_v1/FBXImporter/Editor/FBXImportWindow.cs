using UnityEditor;
using UnityEngine;

namespace FBXImporter.Editor
{
    /// <summary>
    /// EditorWindow providing the UI for Phase 1 of the FBX Import Pipeline.
    /// Allows selecting an external folder, triggering import, and batch-processing tasks.
    /// </summary>
    public class FBXImportWindow : EditorWindow
    {
        private const string PREFS_EXTERNAL_FOLDER = "FBXImportPipeline_ExternalFolder";
        private const string PREFS_DESTINATION_FOLDER = "FBXImportPipeline_DestinationFolder";
        private const string PREFS_PREFAB_OUTPUT_FOLDER = "FBXImportPipeline_PrefabOutputFolder";

        private const string DEFAULT_DESTINATION_FOLDER = "Assets/Imported";
        private const string DEFAULT_PREFAB_OUTPUT_FOLDER = "Assets/Prefabs/Imported";

        private const float BROWSE_BUTTON_WIDTH = 80f;
        private const float ACTION_BUTTON_HEIGHT = 30f;

        private string externalFolderPath;
        private string destinationFolder;
        private string prefabOutputFolder;

        /// <summary>Opens the window from the menu bar.</summary>
        [MenuItem("Tools/FBX Import Pipeline")]
        public static void ShowWindow()
        {
            var window = GetWindow<FBXImportWindow>("FBX Import Pipeline");
            window.minSize = new Vector2(400, 320);
        }

        private void OnEnable()
        {
            externalFolderPath = EditorPrefs.GetString(PREFS_EXTERNAL_FOLDER, string.Empty);
            destinationFolder = EditorPrefs.GetString(PREFS_DESTINATION_FOLDER, DEFAULT_DESTINATION_FOLDER);
            prefabOutputFolder = EditorPrefs.GetString(PREFS_PREFAB_OUTPUT_FOLDER, DEFAULT_PREFAB_OUTPUT_FOLDER);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("FBX Import Pipeline", EditorStyles.boldLabel);
            EditorGUILayout.Space(8);

            DrawFolderPathField("External Folder", ref externalFolderPath, PREFS_EXTERNAL_FOLDER, false);
            DrawFolderPathField("Destination Folder", ref destinationFolder, PREFS_DESTINATION_FOLDER, true);
            DrawFolderPathField("Prefab Output Folder", ref prefabOutputFolder, PREFS_PREFAB_OUTPUT_FOLDER, true);

            EditorGUILayout.Space(12);

            DrawImportButton();

            EditorGUILayout.Space(4);
            DrawSeparator();
            EditorGUILayout.Space(4);

            DrawProcessAllButton();

            EditorGUILayout.Space(8);

            DrawTaskStatus();
        }

        /// <summary>
        /// Draws a folder path text field with a Browse button.
        /// </summary>
        private void DrawFolderPathField(
            string label,
            ref string folderPath,
            string prefsKey,
            bool constrainToAssets)
        {
            EditorGUILayout.BeginHorizontal();
            folderPath = EditorGUILayout.TextField(label, folderPath);

            if (GUILayout.Button("Browse...", GUILayout.Width(BROWSE_BUTTON_WIDTH)))
            {
                string defaultPath = constrainToAssets ? Application.dataPath : string.Empty;
                string title = constrainToAssets ? $"Select {label} (inside Assets)" : $"Select {label}";
                string selectedPath = EditorUtility.OpenFolderPanel(title, defaultPath, string.Empty);

                if (!string.IsNullOrEmpty(selectedPath))
                {
                    if (constrainToAssets)
                    {
                        // Convert absolute path to Assets-relative path
                        string dataPath = Application.dataPath.Replace("\\", "/");
                        selectedPath = selectedPath.Replace("\\", "/");

                        if (selectedPath.StartsWith(dataPath))
                        {
                            folderPath = "Assets" + selectedPath.Substring(dataPath.Length);
                        }
                        else
                        {
                            Debug.LogWarning(
                                "[FBXImportWindow] Selected folder must be inside the Assets directory.");
                        }
                    }
                    else
                    {
                        folderPath = selectedPath;
                    }

                    EditorPrefs.SetString(prefsKey, folderPath);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws the Import button that triggers Phase 1.
        /// </summary>
        private void DrawImportButton()
        {
            bool canImport = !string.IsNullOrEmpty(externalFolderPath) &&
                             !string.IsNullOrEmpty(destinationFolder) &&
                             !string.IsNullOrEmpty(prefabOutputFolder);

            EditorGUI.BeginDisabledGroup(!canImport);

            if (GUILayout.Button("Import FBX Files", GUILayout.Height(ACTION_BUTTON_HEIGHT)))
            {
                // Persist current values
                EditorPrefs.SetString(PREFS_EXTERNAL_FOLDER, externalFolderPath);
                EditorPrefs.SetString(PREFS_DESTINATION_FOLDER, destinationFolder);
                EditorPrefs.SetString(PREFS_PREFAB_OUTPUT_FOLDER, prefabOutputFolder);

                var tasks = FBXImportPipeline.ImportFromExternalFolder(
                    externalFolderPath, destinationFolder, prefabOutputFolder);

                if (tasks.Count > 0)
                {
                    // Select the first created task for convenience
                    Selection.activeObject = tasks[0];
                }
            }

            EditorGUI.EndDisabledGroup();
        }

        /// <summary>
        /// Draws the Process All button for batch-processing all pending tasks.
        /// </summary>
        private static void DrawProcessAllButton()
        {
            int pendingCount = GetPendingTaskCount();

            EditorGUI.BeginDisabledGroup(pendingCount == 0);

            if (GUILayout.Button($"Process All Pending Tasks ({pendingCount})",
                    GUILayout.Height(ACTION_BUTTON_HEIGHT)))
            {
                FBXImportPipeline.ProcessAllTasks();
            }

            EditorGUI.EndDisabledGroup();
        }

        /// <summary>
        /// Draws a status summary of all FBXImportTask assets.
        /// </summary>
        private static void DrawTaskStatus()
        {
            string[] guids = AssetDatabase.FindAssets("t:FBXImportTask");
            int totalCount = guids.Length;
            int processedCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var task = AssetDatabase.LoadAssetAtPath<FBXImportTask>(path);
                if (task != null && task.IsProcessed)
                    processedCount++;
            }

            int pendingCount = totalCount - processedCount;

            EditorGUILayout.HelpBox(
                $"Total Tasks: {totalCount}  |  Pending: {pendingCount}  |  Processed: {processedCount}",
                pendingCount > 0 ? MessageType.Info : MessageType.None);
        }

        /// <summary>
        /// Returns the number of unprocessed FBXImportTask assets.
        /// </summary>
        private static int GetPendingTaskCount()
        {
            string[] guids = AssetDatabase.FindAssets("t:FBXImportTask");
            int pendingCount = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var task = AssetDatabase.LoadAssetAtPath<FBXImportTask>(path);
                if (task != null && !task.IsProcessed)
                    pendingCount++;
            }

            return pendingCount;
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
