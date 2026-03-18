using UnityEditor;
using UnityEngine;

/// <summary>
/// EditorWindow that exposes the Zoo Scene Generator's full UI.
/// Open via Window > Zoo Scene Generator.
/// </summary>
public class ZooSceneGeneratorWindow : EditorWindow
{
    private ZooSceneConfig _config;
    private SerializedObject _serializedConfig;
    private SerializedProperty _categoriesProp;
    private SerializedProperty _layoutModeProp;
    private SerializedProperty _gridSettingsProp;
    private SerializedProperty _autoFitSettingsProp;
    private SerializedProperty _categorySpacingProp;
    private SerializedProperty _previewModeProp;

    private Vector2 _scrollPosition;

    [MenuItem("Window/Zoo Scene Generator")]
    public static void Open()
    {
        var window = GetWindow<ZooSceneGeneratorWindow>("Zoo Scene Generator");
        window.minSize = new Vector2(400f, 500f);
        window.Show();
    }

    private void OnGUI()
    {
        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

        DrawConfigHeader();

        if (_config == null)
        {
            EditorGUILayout.HelpBox("No config loaded. Create or assign a ZooSceneConfig asset above.", MessageType.Info);
            EditorGUILayout.EndScrollView();
            return;
        }

        EnsureSerializedObject();

        _serializedConfig.Update();

        EditorGUILayout.Space(4f);
        DrawCategoryList();

        EditorGUILayout.Space(4f);
        DrawLayoutSettings();

        EditorGUILayout.Space(4f);
        EditorGUILayout.PropertyField(_categorySpacingProp, new GUIContent("Category Spacing"));
        EditorGUILayout.PropertyField(_previewModeProp, new GUIContent("Preview Mode"));

        EditorGUILayout.Space(8f);
        DrawActionButtons();

        _serializedConfig.ApplyModifiedProperties();

        EditorGUILayout.EndScrollView();
    }

    // -------------------------------------------------------------------------
    // Config header row
    // -------------------------------------------------------------------------

    private void DrawConfigHeader()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        var newConfig = (ZooSceneConfig)EditorGUILayout.ObjectField("Config Asset", _config, typeof(ZooSceneConfig), false);
        if (newConfig != _config)
            LoadConfig(newConfig);

        if (GUILayout.Button("New Config", GUILayout.Width(90f)))
            CreateNewConfig();

        if (GUILayout.Button("Save As", GUILayout.Width(70f)))
            SaveConfigAs();

        EditorGUILayout.EndHorizontal();
    }

    // -------------------------------------------------------------------------
    // Category list
    // -------------------------------------------------------------------------

    private void DrawCategoryList()
    {
        EditorGUILayout.LabelField("Categories", EditorStyles.boldLabel);

        int removeIndex = -1;

        for (int i = 0; i < _categoriesProp.arraySize; i++)
        {
            SerializedProperty entry = _categoriesProp.GetArrayElementAtIndex(i);
            SerializedProperty displayName = entry.FindPropertyRelative("displayName");
            SerializedProperty folderPath = entry.FindPropertyRelative("folderPath");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Category {i + 1}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("✕", GUILayout.Width(24f)))
                removeIndex = i;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(displayName, new GUIContent("Display Name"));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(folderPath, new GUIContent("Folder Path"));
            if (GUILayout.Button("Browse", GUILayout.Width(60f)))
            {
                string selected = EditorUtility.OpenFolderPanel("Select Prefab Folder", "Assets", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    // Convert absolute path to project-relative path
                    if (selected.StartsWith(Application.dataPath))
                        selected = "Assets" + selected.Substring(Application.dataPath.Length);

                    folderPath.stringValue = selected;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }

        if (removeIndex >= 0)
            _categoriesProp.DeleteArrayElementAtIndex(removeIndex);

        if (GUILayout.Button("+ Add Category"))
        {
            _categoriesProp.InsertArrayElementAtIndex(_categoriesProp.arraySize);
            var newEntry = _categoriesProp.GetArrayElementAtIndex(_categoriesProp.arraySize - 1);
            newEntry.FindPropertyRelative("displayName").stringValue = string.Empty;
            newEntry.FindPropertyRelative("folderPath").stringValue = string.Empty;
        }
    }

    // -------------------------------------------------------------------------
    // Layout settings
    // -------------------------------------------------------------------------

    private void DrawLayoutSettings()
    {
        EditorGUILayout.LabelField("Layout Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_layoutModeProp, new GUIContent("Layout Mode"));

        var mode = (LayoutMode)_layoutModeProp.enumValueIndex;

        if (mode == LayoutMode.Grid)
        {
            EditorGUI.indentLevel++;
            SerializedProperty spacingX = _gridSettingsProp.FindPropertyRelative("spacingX");
            SerializedProperty spacingZ = _gridSettingsProp.FindPropertyRelative("spacingZ");
            SerializedProperty maxColumns = _gridSettingsProp.FindPropertyRelative("maxColumns");
            EditorGUILayout.PropertyField(spacingX, new GUIContent("Spacing X"));
            EditorGUILayout.PropertyField(spacingZ, new GUIContent("Spacing Z"));
            EditorGUILayout.PropertyField(maxColumns, new GUIContent("Max Columns"));
            EditorGUI.indentLevel--;
        }
        else
        {
            EditorGUI.indentLevel++;
            SerializedProperty padding = _autoFitSettingsProp.FindPropertyRelative("padding");
            SerializedProperty maxRowWidth = _autoFitSettingsProp.FindPropertyRelative("maxRowWidth");
            EditorGUILayout.PropertyField(padding, new GUIContent("Padding"));
            EditorGUILayout.PropertyField(maxRowWidth, new GUIContent("Max Row Width"));
            EditorGUI.indentLevel--;
        }
    }

    // -------------------------------------------------------------------------
    // Action buttons
    // -------------------------------------------------------------------------

    private void DrawActionButtons()
    {
        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("Generate Scene", GUILayout.Height(32f)))
            ZooSceneGenerator.Generate(_config);

        GUI.backgroundColor = new Color(0.9f, 0.4f, 0.4f);
        if (GUILayout.Button("Clear Scene", GUILayout.Height(32f)))
            ZooSceneGenerator.Clear();

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void LoadConfig(ZooSceneConfig config)
    {
        _config = config;
        _serializedConfig = null;
        _categoriesProp = null;
    }

    private void EnsureSerializedObject()
    {
        if (_serializedConfig == null || _serializedConfig.targetObject != _config)
        {
            _serializedConfig = new SerializedObject(_config);
            _categoriesProp = _serializedConfig.FindProperty("categories");
            _layoutModeProp = _serializedConfig.FindProperty("layoutMode");
            _gridSettingsProp = _serializedConfig.FindProperty("gridSettings");
            _autoFitSettingsProp = _serializedConfig.FindProperty("autoFitSettings");
            _categorySpacingProp = _serializedConfig.FindProperty("categorySpacing");
            _previewModeProp = _serializedConfig.FindProperty("previewMode");
        }
    }

    private void CreateNewConfig()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Create Zoo Scene Config",
            "ZooSceneConfig",
            "asset",
            "Choose a location to save the new config asset.");

        if (string.IsNullOrEmpty(path))
            return;

        var newConfig = CreateInstance<ZooSceneConfig>();
        AssetDatabase.CreateAsset(newConfig, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        LoadConfig(AssetDatabase.LoadAssetAtPath<ZooSceneConfig>(path));
    }

    private void SaveConfigAs()
    {
        if (_config == null)
            return;

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Zoo Scene Config As",
            _config.name,
            "asset",
            "Choose a location to save a copy of the config.");

        if (string.IsNullOrEmpty(path))
            return;

        var copy = Instantiate(_config);
        AssetDatabase.CreateAsset(copy, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        LoadConfig(AssetDatabase.LoadAssetAtPath<ZooSceneConfig>(path));
    }
}
