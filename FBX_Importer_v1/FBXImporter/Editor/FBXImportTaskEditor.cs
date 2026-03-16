using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FBXImporter.Editor
{
    /// <summary>
    /// Sort modes for displaying root objects in the Inspector.
    /// </summary>
    public enum RootObjectSortMode
    {
        /// <summary>Original order from the FBX hierarchy.</summary>
        HierarchyOrder,

        /// <summary>Grouped by visual appearance (shared mesh/materials).</summary>
        ByAppearance,

        /// <summary>Alphabetical by name.</summary>
        ByName
    }

    /// <summary>
    /// Custom Inspector for FBXImportTask ScriptableObject.
    /// Provides the decision UI for how to split each FBX into prefabs.
    /// </summary>
    [CustomEditor(typeof(FBXImportTask))]
    public class FBXImportTaskEditor : UnityEditor.Editor
    {
        private const float PROCESS_BUTTON_HEIGHT = 30f;
        private const float TOGGLE_BUTTON_HEIGHT = 22f;
        private const float PREVIEW_SIZE = 48f;

        private static readonly Color SUCCESS_COLOR = new Color(0.2f, 0.8f, 0.3f, 1f);
        private static readonly Color GROUP_HEADER_COLOR = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color UNIQUE_SECTION_COLOR = new Color(0.5f, 0.5f, 0.5f, 0.06f);

        /// <summary>Rotating palette for distinguishing duplicate groups visually.</summary>
        private static readonly Color[] GROUP_COLORS =
        {
            new Color(0.4f, 0.7f, 1f, 0.12f),
            new Color(1f, 0.5f, 0.3f, 0.12f),
            new Color(0.5f, 1f, 0.5f, 0.12f),
            new Color(1f, 0.4f, 0.8f, 0.12f),
            new Color(1f, 1f, 0.4f, 0.12f),
            new Color(0.6f, 0.4f, 1f, 0.12f),
        };

        private static readonly string[] SORT_MODE_LABELS =
        {
            "Hierarchy Order",
            "By Appearance",
            "By Name"
        };

        private bool showDuplicateGroups = true;
        private bool showGeneratedPrefabs = true;
        private RootObjectSortMode sortMode = RootObjectSortMode.HierarchyOrder;

        public override void OnInspectorGUI()
        {
            var task = (FBXImportTask)target;

            DrawStatusBadge(task);
            EditorGUILayout.Space(8);

            DrawSourceFBXField(task);
            EditorGUILayout.Space(4);

            DrawFolderPaths(task);
            EditorGUILayout.Space(8);

            DrawDuplicateGroupsSummary(task);
            EditorGUILayout.Space(4);

            DrawModeToggle(task);
            EditorGUILayout.Space(8);

            if (!task.KeepAsSinglePrefab)
            {
                DrawRootObjectsList(task);
                EditorGUILayout.Space(4);
            }

            DrawProcessButton(task);

            if (task.IsProcessed)
            {
                EditorGUILayout.Space(8);
                DrawGeneratedPrefabsList(task);
                EditorGUILayout.Space(4);
                DrawResetButton(task);
            }
        }

        /// <summary>
        /// Draws a status badge showing "Processed" or "Pending".
        /// </summary>
        private static void DrawStatusBadge(FBXImportTask task)
        {
            if (task.IsProcessed)
            {
                var prevColor = GUI.contentColor;
                GUI.contentColor = SUCCESS_COLOR;
                EditorGUILayout.HelpBox(
                    $"Status: Processed — {task.GeneratedPrefabPaths.Count} prefab(s) generated.",
                    MessageType.Info);
                GUI.contentColor = prevColor;
            }
            else
            {
                EditorGUILayout.HelpBox("Status: Pending — configure options below, then Process.", MessageType.Warning);
            }
        }

        /// <summary>
        /// Draws the source FBX object field (read-only).
        /// </summary>
        private static void DrawSourceFBXField(FBXImportTask task)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("Source FBX", task.SourceFBX, typeof(GameObject), false);
            EditorGUI.EndDisabledGroup();

            // Show FBX preview if available
            if (task.SourceFBX != null)
            {
                Texture2D preview = AssetPreview.GetAssetPreview(task.SourceFBX);
                if (preview != null)
                {
                    Rect previewRect = GUILayoutUtility.GetRect(
                        PREVIEW_SIZE * 2, PREVIEW_SIZE * 2, GUILayout.ExpandWidth(false));
                    GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
                }
            }
        }

        /// <summary>
        /// Draws the folder paths (read-only).
        /// </summary>
        private static void DrawFolderPaths(FBXImportTask task)
        {
            EditorGUILayout.LabelField("Folder Paths", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Prefab Output", task.PrefabOutputFolder);
            EditorGUILayout.TextField("Materials", task.MaterialsFolder);
            EditorGUILayout.TextField("Textures", task.TexturesFolder);
            EditorGUI.EndDisabledGroup();
        }

        /// <summary>
        /// Draws the duplicate groups summary section.
        /// </summary>
        private void DrawDuplicateGroupsSummary(FBXImportTask task)
        {
            if (task.DuplicateGroups == null || task.DuplicateGroups.Count == 0)
            {
                EditorGUILayout.HelpBox("No duplicate objects detected.", MessageType.None);

                if (GUILayout.Button("Re-scan for Duplicates", GUILayout.Height(TOGGLE_BUTTON_HEIGHT)))
                {
                    FBXImportPipeline.DetectDuplicates(task);
                }

                return;
            }

            int totalDuplicates = task.DuplicateGroups.Sum(g => g.MemberNames.Count);
            showDuplicateGroups = EditorGUILayout.Foldout(
                showDuplicateGroups,
                $"Duplicate Groups ({task.DuplicateGroups.Count} groups, {totalDuplicates} objects)",
                true,
                EditorStyles.foldoutHeader);

            if (!showDuplicateGroups)
                return;

            EditorGUI.indentLevel++;

            for (int i = 0; i < task.DuplicateGroups.Count; i++)
            {
                var group = task.DuplicateGroups[i];
                Color bgColor = GROUP_COLORS[i % GROUP_COLORS.Length];

                Rect groupRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.DrawRect(groupRect, bgColor);

                EditorGUILayout.LabelField(
                    $"Mesh: \"{group.SharedMeshName}\" ({group.VertexCount} verts) — {group.MemberNames.Count} instances",
                    EditorStyles.boldLabel);

                string members = string.Join(", ", group.MemberNames);
                EditorGUILayout.LabelField(members, EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.EndVertical();
            }

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(2);

            if (GUILayout.Button("Re-scan for Duplicates", GUILayout.Height(TOGGLE_BUTTON_HEIGHT)))
            {
                FBXImportPipeline.DetectDuplicates(task);
            }
        }

        /// <summary>
        /// Draws the mode toggle for single vs split prefab.
        /// </summary>
        private void DrawModeToggle(FBXImportTask task)
        {
            EditorGUI.BeginDisabledGroup(task.IsProcessed);

            EditorGUI.BeginChangeCheck();
            bool newValue = EditorGUILayout.Toggle("Keep as Single Prefab", task.KeepAsSinglePrefab);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(task, "Toggle Keep as Single Prefab");
                task.KeepAsSinglePrefab = newValue;
                EditorUtility.SetDirty(task);
            }

            EditorGUI.EndDisabledGroup();
        }

        /// <summary>
        /// Draws the list of root objects with per-object split toggles.
        /// Supports sorting by hierarchy order, appearance, or name.
        /// </summary>
        private void DrawRootObjectsList(FBXImportTask task)
        {
            EditorGUILayout.LabelField("Root Objects", EditorStyles.boldLabel);

            if (task.RootObjects == null || task.RootObjects.Count == 0)
            {
                EditorGUILayout.HelpBox("No root objects found in this FBX.", MessageType.Info);
                return;
            }

            EditorGUI.BeginDisabledGroup(task.IsProcessed);

            // Sort mode toolbar
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Sort", GUILayout.Width(30));
            sortMode = (RootObjectSortMode)GUILayout.Toolbar(
                (int)sortMode, SORT_MODE_LABELS, GUILayout.Height(TOGGLE_BUTTON_HEIGHT));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // Select All / Deselect All buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All", GUILayout.Height(TOGGLE_BUTTON_HEIGHT)))
            {
                Undo.RecordObject(task, "Select All Root Objects");
                foreach (var entry in task.RootObjects)
                    entry.SplitAsIndividualPrefab = true;
                EditorUtility.SetDirty(task);
            }

            if (GUILayout.Button("Deselect All", GUILayout.Height(TOGGLE_BUTTON_HEIGHT)))
            {
                Undo.RecordObject(task, "Deselect All Root Objects");
                foreach (var entry in task.RootObjects)
                    entry.SplitAsIndividualPrefab = false;
                EditorUtility.SetDirty(task);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Build a lookup for group colors and group info
            var groupColorMap = new Dictionary<string, Color>();
            var groupInfoMap = new Dictionary<string, DuplicateGroup>();
            if (task.DuplicateGroups != null)
            {
                for (int i = 0; i < task.DuplicateGroups.Count; i++)
                {
                    var group = task.DuplicateGroups[i];
                    groupColorMap[group.GroupId] = GROUP_COLORS[i % GROUP_COLORS.Length];
                    groupInfoMap[group.GroupId] = group;
                }
            }

            // Build indexed entries (preserving original index for preview lookups)
            var indexedEntries = new List<(RootObjectEntry entry, int originalIndex)>();
            for (int i = 0; i < task.RootObjects.Count; i++)
            {
                indexedEntries.Add((task.RootObjects[i], i));
            }

            // Draw based on current sort mode
            switch (sortMode)
            {
                case RootObjectSortMode.HierarchyOrder:
                    DrawEntriesFlat(task, indexedEntries, groupColorMap);
                    break;

                case RootObjectSortMode.ByAppearance:
                    DrawEntriesByAppearance(task, indexedEntries, groupColorMap, groupInfoMap);
                    break;

                case RootObjectSortMode.ByName:
                    var sorted = indexedEntries
                        .OrderBy(e => e.entry.Name)
                        .ToList();
                    DrawEntriesFlat(task, sorted, groupColorMap);
                    break;
            }

            EditorGUI.EndDisabledGroup();
        }

        /// <summary>
        /// Draws entries in a flat list with optional duplicate color tinting.
        /// </summary>
        private void DrawEntriesFlat(
            FBXImportTask task,
            List<(RootObjectEntry entry, int originalIndex)> entries,
            Dictionary<string, Color> groupColorMap)
        {
            foreach (var (entry, originalIndex) in entries)
            {
                Color? entryColor = null;
                if (!string.IsNullOrEmpty(entry.DuplicateGroupId) &&
                    groupColorMap.TryGetValue(entry.DuplicateGroupId, out Color color))
                {
                    entryColor = color;
                }

                DrawRootObjectEntry(task, entry, originalIndex, entryColor);
            }
        }

        /// <summary>
        /// Draws entries grouped by visual appearance.
        /// Duplicate groups appear as collapsible sections with a header.
        /// Unique objects appear at the end under their own section.
        /// </summary>
        private void DrawEntriesByAppearance(
            FBXImportTask task,
            List<(RootObjectEntry entry, int originalIndex)> allEntries,
            Dictionary<string, Color> groupColorMap,
            Dictionary<string, DuplicateGroup> groupInfoMap)
        {
            // Separate grouped vs unique entries
            var groupedByGroupId = new Dictionary<string, List<(RootObjectEntry entry, int originalIndex)>>();
            var uniqueEntries = new List<(RootObjectEntry entry, int originalIndex)>();

            foreach (var item in allEntries)
            {
                string gid = item.entry.DuplicateGroupId;
                if (!string.IsNullOrEmpty(gid) && groupInfoMap.ContainsKey(gid))
                {
                    if (!groupedByGroupId.ContainsKey(gid))
                        groupedByGroupId[gid] = new List<(RootObjectEntry, int)>();
                    groupedByGroupId[gid].Add(item);
                }
                else
                {
                    uniqueEntries.Add(item);
                }
            }

            // Draw each duplicate group as a section
            foreach (var kvp in groupedByGroupId)
            {
                var groupId = kvp.Key;
                var members = kvp.Value;
                var groupInfo = groupInfoMap[groupId];
                Color groupColor = groupColorMap.ContainsKey(groupId)
                    ? groupColorMap[groupId]
                    : GROUP_HEADER_COLOR;

                // Group header
                Rect headerRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.DrawRect(headerRect, groupColor);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(
                    $"  \"{groupInfo.SharedMeshName}\" ({groupInfo.VertexCount} verts) — {members.Count} instances",
                    EditorStyles.boldLabel);

                // Batch toggle for the group
                if (GUILayout.Button("All", GUILayout.Width(35)))
                {
                    Undo.RecordObject(task, $"Select Group {groupId}");
                    foreach (var (entry, _) in members)
                        entry.SplitAsIndividualPrefab = true;
                    EditorUtility.SetDirty(task);
                }

                if (GUILayout.Button("None", GUILayout.Width(40)))
                {
                    Undo.RecordObject(task, $"Deselect Group {groupId}");
                    foreach (var (entry, _) in members)
                        entry.SplitAsIndividualPrefab = false;
                    EditorUtility.SetDirty(task);
                }

                EditorGUILayout.EndHorizontal();

                // Draw members
                EditorGUI.indentLevel++;
                foreach (var (entry, originalIndex) in members)
                {
                    DrawRootObjectEntry(task, entry, originalIndex, groupColor);
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            // Draw unique (non-duplicate) entries
            if (uniqueEntries.Count > 0)
            {
                Rect uniqueHeaderRect = EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUI.DrawRect(uniqueHeaderRect, UNIQUE_SECTION_COLOR);

                EditorGUILayout.LabelField(
                    $"  Unique Objects — {uniqueEntries.Count} item(s)",
                    EditorStyles.boldLabel);

                EditorGUI.indentLevel++;
                foreach (var (entry, originalIndex) in uniqueEntries)
                {
                    DrawRootObjectEntry(task, entry, originalIndex, null);
                }
                EditorGUI.indentLevel--;

                EditorGUILayout.EndVertical();
            }
        }

        /// <summary>
        /// Draws a single root object entry with name label, split toggle,
        /// and optional duplicate highlight.
        /// </summary>
        private void DrawRootObjectEntry(
            FBXImportTask task,
            RootObjectEntry entry,
            int index,
            Color? duplicateColor)
        {
            Rect entryRect = EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            // Tint duplicates with their group color
            if (duplicateColor.HasValue)
            {
                EditorGUI.DrawRect(entryRect, duplicateColor.Value);
            }

            // Preview icon — always reserve space to avoid IMGUI layout mismatch
            Rect previewRect = GUILayoutUtility.GetRect(
                PREVIEW_SIZE, PREVIEW_SIZE, GUILayout.Width(PREVIEW_SIZE), GUILayout.Height(PREVIEW_SIZE));

            if (task.SourceFBX != null && index < task.SourceFBX.transform.childCount)
            {
                Transform child = task.SourceFBX.transform.GetChild(index);
                Texture2D preview = AssetPreview.GetAssetPreview(child.gameObject);
                if (preview != null)
                {
                    GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
                }
            }

            // Name + duplicate badge
            string label = entry.Name;
            if (!string.IsNullOrEmpty(entry.DuplicateGroupId))
            {
                label += $"  [{entry.DuplicateGroupId}]";
            }

            EditorGUILayout.LabelField(label, EditorStyles.boldLabel, GUILayout.MinWidth(100));

            EditorGUI.BeginChangeCheck();
            bool splitValue = EditorGUILayout.Toggle("Split as Prefab", entry.SplitAsIndividualPrefab);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(task, $"Toggle Split: {entry.Name}");
                entry.SplitAsIndividualPrefab = splitValue;
                EditorUtility.SetDirty(task);
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws the Process button that triggers prefab generation.
        /// </summary>
        private static void DrawProcessButton(FBXImportTask task)
        {
            EditorGUI.BeginDisabledGroup(task.IsProcessed || task.SourceFBX == null);

            if (GUILayout.Button("Process Task", GUILayout.Height(PROCESS_BUTTON_HEIGHT)))
            {
                FBXImportPipeline.ProcessTask(task);
            }

            EditorGUI.EndDisabledGroup();
        }

        /// <summary>
        /// Draws the list of generated prefab paths with clickable links.
        /// </summary>
        private void DrawGeneratedPrefabsList(FBXImportTask task)
        {
            if (task.GeneratedPrefabPaths == null || task.GeneratedPrefabPaths.Count == 0)
                return;

            showGeneratedPrefabs = EditorGUILayout.Foldout(
                showGeneratedPrefabs,
                $"Generated Prefabs ({task.GeneratedPrefabPaths.Count})",
                true,
                EditorStyles.foldoutHeader);

            if (!showGeneratedPrefabs)
                return;

            EditorGUI.indentLevel++;

            foreach (string prefabPath in task.GeneratedPrefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                EditorGUILayout.BeginHorizontal();

                if (prefab != null)
                {
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.ObjectField(prefab, typeof(GameObject), false);
                    EditorGUI.EndDisabledGroup();

                    if (GUILayout.Button("Ping", GUILayout.Width(40)))
                    {
                        EditorGUIUtility.PingObject(prefab);
                        Selection.activeObject = prefab;
                    }
                }
                else
                {
                    EditorGUILayout.LabelField(prefabPath, EditorStyles.miniLabel);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// Draws the Reset button to allow re-processing a task.
        /// </summary>
        private static void DrawResetButton(FBXImportTask task)
        {
            EditorGUILayout.Space(4);

            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.6f, 0.3f, 1f);

            if (GUILayout.Button("Reset Task (allow re-processing)", GUILayout.Height(TOGGLE_BUTTON_HEIGHT)))
            {
                if (EditorUtility.DisplayDialog(
                        "Reset Import Task",
                        "This will mark the task as unprocessed so you can change settings and process again. " +
                        "Previously generated prefabs will NOT be deleted automatically.",
                        "Reset",
                        "Cancel"))
                {
                    FBXImportPipeline.ResetTask(task);
                }
            }

            GUI.backgroundColor = prevColor;
        }
    }
}
