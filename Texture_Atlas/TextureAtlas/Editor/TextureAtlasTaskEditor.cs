using UnityEditor;
using UnityEngine;

namespace TextureAtlas.Editor
{
    /// <summary>
    /// Custom Inspector for TextureAtlasTask providing a polished UI with
    /// material list, atlas settings, preview, and process button.
    /// </summary>
    [CustomEditor(typeof(TextureAtlasTask))]
    public class TextureAtlasTaskEditor : UnityEditor.Editor
    {
        private const float REMOVE_BUTTON_WIDTH = 22f;
        private const float ACTION_BUTTON_HEIGHT = 28f;
        private const float PREVIEW_THUMBNAIL_SIZE = 128f;

        private static readonly int[] ATLAS_SIZE_OPTIONS = { 256, 512, 1024, 2048, 4096 };
        private static readonly string[] ATLAS_SIZE_LABELS = { "256", "512", "1024", "2048", "4096" };

        private bool showMaterialsFoldout = true;
        private bool showSettingsFoldout = true;
        private bool showOutputFoldout = true;
        private bool showPreviewFoldout = true;

        public override void OnInspectorGUI()
        {
            TextureAtlasTask task = (TextureAtlasTask)target;

            DrawHeader(task);
            EditorGUILayout.Space(8);

            DrawSourceMaterialsSection(task);
            EditorGUILayout.Space(4);

            DrawAtlasSettingsSection(task);
            EditorGUILayout.Space(4);

            DrawOutputSection(task);
            EditorGUILayout.Space(8);

            DrawActionButtons(task);

            if (task.IsProcessed)
            {
                EditorGUILayout.Space(8);
                DrawPreviewSection(task);
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(task);
            }
        }

        /// <summary>
        /// Draws the header with title and status badge.
        /// </summary>
        private static void DrawHeader(TextureAtlasTask task)
        {
            EditorGUILayout.LabelField("Texture Atlas Task", EditorStyles.boldLabel);

            string statusText = task.IsProcessed ? "Processed" : "Pending";
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
        /// Draws the Source Materials section.
        /// </summary>
        private void DrawSourceMaterialsSection(TextureAtlasTask task)
        {
            showMaterialsFoldout = EditorGUILayout.Foldout(showMaterialsFoldout, "Source Materials", true, EditorStyles.foldoutHeader);
            if (!showMaterialsFoldout) return;

            EditorGUI.indentLevel++;

            if (GUILayout.Button("Add Selected Materials"))
            {
                Undo.RecordObject(task, "Add Selected Materials");
                foreach (Object obj in Selection.objects)
                {
                    if (obj is Material mat && !task.SourceMaterials.Contains(mat))
                    {
                        task.SourceMaterials.Add(mat);
                    }
                }
                EditorUtility.SetDirty(task);
            }

            for (int i = task.SourceMaterials.Count - 1; i >= 0; i--)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(task.SourceMaterials[i], typeof(Material), false);
                EditorGUI.EndDisabledGroup();
                if (GUILayout.Button("X", GUILayout.Width(REMOVE_BUTTON_WIDTH)))
                {
                    Undo.RecordObject(task, "Remove Material");
                    task.SourceMaterials.RemoveAt(i);
                    EditorUtility.SetDirty(task);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField($"Count: {task.SourceMaterials.Count}", EditorStyles.miniLabel);

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Draws the Atlas Settings section.
        /// </summary>
        private void DrawAtlasSettingsSection(TextureAtlasTask task)
        {
            showSettingsFoldout = EditorGUILayout.Foldout(showSettingsFoldout, "Atlas Settings", true, EditorStyles.foldoutHeader);
            if (!showSettingsFoldout) return;

            EditorGUI.indentLevel++;

            int sizeIndex = System.Array.IndexOf(ATLAS_SIZE_OPTIONS, task.MaxAtlasSize);
            if (sizeIndex < 0) sizeIndex = 3;
            sizeIndex = EditorGUILayout.Popup("Max Atlas Size", sizeIndex, ATLAS_SIZE_LABELS);
            task.MaxAtlasSize = ATLAS_SIZE_OPTIONS[sizeIndex];

            task.CellSize = EditorGUILayout.IntField("Cell Size", task.CellSize);
            task.Padding = EditorGUILayout.IntSlider("Padding", task.Padding, 0, 8);

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Draws the Output section.
        /// </summary>
        private void DrawOutputSection(TextureAtlasTask task)
        {
            showOutputFoldout = EditorGUILayout.Foldout(showOutputFoldout, "Output", true, EditorStyles.foldoutHeader);
            if (!showOutputFoldout) return;

            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            task.OutputFolder = EditorGUILayout.TextField("Output Folder", task.OutputFolder);
            if (GUILayout.Button("Browse...", GUILayout.Width(80)))
            {
                string selectedPath = EditorUtility.OpenFolderPanel("Select Output Folder", Application.dataPath, "");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    string dataPath = Application.dataPath.Replace("\\", "/");
                    selectedPath = selectedPath.Replace("\\", "/");
                    if (selectedPath.StartsWith(dataPath))
                    {
                        task.OutputFolder = "Assets" + selectedPath.Substring(dataPath.Length);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            task.AssetPrefix = EditorGUILayout.TextField("Asset Prefix", task.AssetPrefix);

            task.DeleteOriginalMaterials = EditorGUILayout.Toggle("Delete Original Materials", task.DeleteOriginalMaterials);
            if (task.DeleteOriginalMaterials)
            {
                EditorGUILayout.HelpBox("Original material assets will be permanently deleted.", MessageType.Warning);
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Draws the Generate and Reset action buttons.
        /// </summary>
        private static void DrawActionButtons(TextureAtlasTask task)
        {
            DrawSeparator();
            EditorGUILayout.Space(4);

            bool canGenerate = task.SourceMaterials.Count > 0;
            EditorGUI.BeginDisabledGroup(!canGenerate);
            if (GUILayout.Button("Generate Atlas", GUILayout.Height(ACTION_BUTTON_HEIGHT)))
            {
                TextureAtlasGenerator.ProcessTask(task);
            }
            EditorGUI.EndDisabledGroup();

            if (task.IsProcessed)
            {
                EditorGUILayout.Space(2);
                if (GUILayout.Button("Reset Task", GUILayout.Height(ACTION_BUTTON_HEIGHT)))
                {
                    Undo.RecordObject(task, "Reset Atlas Task");
                    task.IsProcessed = false;
                    task.GeneratedMaterial = null;
                    task.AtlasMapping.Clear();
                    EditorUtility.SetDirty(task);
                }
            }
        }

        /// <summary>
        /// Draws the preview section showing generated atlas material.
        /// </summary>
        private void DrawPreviewSection(TextureAtlasTask task)
        {
            showPreviewFoldout = EditorGUILayout.Foldout(showPreviewFoldout, "Preview", true, EditorStyles.foldoutHeader);
            if (!showPreviewFoldout) return;

            EditorGUI.indentLevel++;

            if (task.GeneratedMaterial != null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField("Atlas Material", task.GeneratedMaterial, typeof(Material), false);
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.LabelField($"Mapped Materials: {task.AtlasMapping.Count}");

                // Show atlas texture preview
                Texture baseMap = task.GeneratedMaterial.GetTexture("_BaseMap");
                if (baseMap != null)
                {
                    EditorGUILayout.Space(4);
                    Rect previewRect = EditorGUILayout.GetControlRect(false, PREVIEW_THUMBNAIL_SIZE);
                    previewRect.width = PREVIEW_THUMBNAIL_SIZE;
                    EditorGUI.DrawPreviewTexture(previewRect, baseMap);
                }
            }

            EditorGUI.indentLevel--;
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
