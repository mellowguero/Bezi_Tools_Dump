using UnityEditor;
using UnityEngine;

namespace TextureAtlas.Editor
{
    /// <summary>
    /// Custom Inspector for AtlasRemapTask providing atlas task reference,
    /// prefab list management, and remap execution.
    /// </summary>
    [CustomEditor(typeof(AtlasRemapTask))]
    public class AtlasRemapTaskEditor : UnityEditor.Editor
    {
        private const float REMOVE_BUTTON_WIDTH = 22f;
        private const float ACTION_BUTTON_HEIGHT = 28f;

        private bool showPrefabsFoldout = true;
        private bool showSettingsFoldout = true;

        public override void OnInspectorGUI()
        {
            AtlasRemapTask task = (AtlasRemapTask)target;

            DrawHeader(task);
            EditorGUILayout.Space(8);

            DrawAtlasTaskSection(task);
            EditorGUILayout.Space(4);

            DrawTargetPrefabsSection(task);
            EditorGUILayout.Space(4);

            DrawSettingsSection(task);
            EditorGUILayout.Space(8);

            DrawActionButtons(task);

            if (GUI.changed)
            {
                EditorUtility.SetDirty(task);
            }
        }

        /// <summary>
        /// Draws the header with title and status badge.
        /// </summary>
        private static void DrawHeader(AtlasRemapTask task)
        {
            EditorGUILayout.LabelField("Atlas Remap Task", EditorStyles.boldLabel);

            string statusText = task.IsProcessed ? "Remapped" : "Pending";
            Color statusColor = task.IsProcessed ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.9f, 0.7f, 0.2f);

            Rect rect = EditorGUILayout.GetControlRect(false, 20);
            Rect badgeRect = new Rect(rect.x, rect.y, 90, 18);
            EditorGUI.DrawRect(badgeRect, statusColor);
            GUI.Label(badgeRect, statusText, new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.black }
            });
        }

        /// <summary>
        /// Draws the atlas task reference with validation info.
        /// </summary>
        private static void DrawAtlasTaskSection(AtlasRemapTask task)
        {
            EditorGUILayout.LabelField("Atlas Source", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            task.AtlasTask = (TextureAtlasTask)EditorGUILayout.ObjectField(
                "Atlas Task", task.AtlasTask, typeof(TextureAtlasTask), false);

            if (task.AtlasTask == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a processed Texture Atlas Task to use as the atlas source.",
                    MessageType.Info);
            }
            else if (!task.AtlasTask.IsProcessed)
            {
                EditorGUILayout.HelpBox(
                    "The assigned atlas task has not been processed yet. Generate the atlas first.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField("Atlas Material", task.AtlasTask.GeneratedMaterial, typeof(Material), false);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.LabelField("Mapped Materials", task.AtlasTask.AtlasMapping.Count.ToString());
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Draws the target prefabs list with add/remove controls.
        /// </summary>
        private void DrawTargetPrefabsSection(AtlasRemapTask task)
        {
            showPrefabsFoldout = EditorGUILayout.Foldout(showPrefabsFoldout, "Target Prefabs", true, EditorStyles.foldoutHeader);
            if (!showPrefabsFoldout) return;

            EditorGUI.indentLevel++;

            if (GUILayout.Button("Add Selected Prefabs"))
            {
                Undo.RecordObject(task, "Add Selected Prefabs");
                foreach (Object obj in Selection.objects)
                {
                    if (obj is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go) && !task.TargetPrefabs.Contains(go))
                    {
                        task.TargetPrefabs.Add(go);
                    }
                }
                EditorUtility.SetDirty(task);
            }

            for (int i = task.TargetPrefabs.Count - 1; i >= 0; i--)
            {
                EditorGUILayout.BeginHorizontal();

                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(task.TargetPrefabs[i], typeof(GameObject), false);
                EditorGUI.EndDisabledGroup();

                if (task.TargetPrefabs[i] != null && task.AtlasTask != null && task.AtlasTask.IsProcessed)
                {
                    int matchCount = CountMatchingMaterials(task.TargetPrefabs[i], task.AtlasTask);
                    var renderers = task.TargetPrefabs[i].GetComponentsInChildren<Renderer>(true);
                    int totalMats = 0;
                    foreach (var r in renderers) totalMats += r.sharedMaterials.Length;
                    EditorGUILayout.LabelField($"{matchCount}/{totalMats}", EditorStyles.miniLabel, GUILayout.Width(40));
                }

                if (GUILayout.Button("X", GUILayout.Width(REMOVE_BUTTON_WIDTH)))
                {
                    Undo.RecordObject(task, "Remove Prefab");
                    task.TargetPrefabs.RemoveAt(i);
                    EditorUtility.SetDirty(task);
                }

                EditorGUILayout.EndHorizontal();
            }

            if (task.TargetPrefabs.Count > 0)
            {
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Clear All"))
                {
                    Undo.RecordObject(task, "Clear All Prefabs");
                    task.TargetPrefabs.Clear();
                    EditorUtility.SetDirty(task);
                }
            }

            EditorGUILayout.LabelField($"Count: {task.TargetPrefabs.Count}", EditorStyles.miniLabel);

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Draws the settings section with mesh copy toggle and output folder.
        /// </summary>
        private void DrawSettingsSection(AtlasRemapTask task)
        {
            showSettingsFoldout = EditorGUILayout.Foldout(showSettingsFoldout, "Settings", true, EditorStyles.foldoutHeader);
            if (!showSettingsFoldout) return;

            EditorGUI.indentLevel++;

            task.CreateMeshCopies = EditorGUILayout.Toggle("Create Mesh Copies", task.CreateMeshCopies);

            if (!task.CreateMeshCopies)
            {
                EditorGUILayout.HelpBox(
                    "Meshes will be modified in-place. This cannot be undone for shared mesh assets.",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();
            task.MeshOutputFolder = EditorGUILayout.TextField("Mesh Output Folder", task.MeshOutputFolder);
            if (GUILayout.Button("Browse...", GUILayout.Width(80)))
            {
                string selectedPath = EditorUtility.OpenFolderPanel("Select Mesh Output Folder", Application.dataPath, "");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    string dataPath = Application.dataPath.Replace("\\", "/");
                    selectedPath = selectedPath.Replace("\\", "/");
                    if (selectedPath.StartsWith(dataPath))
                    {
                        task.MeshOutputFolder = "Assets" + selectedPath.Substring(dataPath.Length);
                        EditorUtility.SetDirty(task);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrEmpty(task.MeshOutputFolder))
            {
                EditorGUILayout.LabelField($"Using: {task.GetEffectiveOutputFolder()}", EditorStyles.miniLabel);
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Draws the Remap action button. Always enabled when prerequisites are met.
        /// </summary>
        private static void DrawActionButtons(AtlasRemapTask task)
        {
            DrawSeparator();
            EditorGUILayout.Space(4);

            bool canRemap = task.AtlasTask != null
                         && task.AtlasTask.IsProcessed
                         && task.TargetPrefabs.Count > 0;

            EditorGUI.BeginDisabledGroup(!canRemap);
            if (GUILayout.Button("Remap Prefabs", GUILayout.Height(ACTION_BUTTON_HEIGHT)))
            {
                AtlasRemapGenerator.ProcessTask(task);
            }
            EditorGUI.EndDisabledGroup();

            if (task.IsProcessed)
            {
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Reset Task", GUILayout.Height(ACTION_BUTTON_HEIGHT)))
                {
                    Undo.RecordObject(task, "Reset Remap Task");
                    task.IsProcessed = false;
                    EditorUtility.SetDirty(task);
                }
            }
        }

        /// <summary>
        /// Counts how many materials on a prefab match the atlas mapping.
        /// </summary>
        private static int CountMatchingMaterials(GameObject prefab, TextureAtlasTask atlasTask)
        {
            int count = 0;
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                foreach (Material mat in r.sharedMaterials)
                {
                    if (mat != null && atlasTask.TryGetAtlasRect(mat, out _))
                    {
                        count++;
                    }
                }
            }
            return count;
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
