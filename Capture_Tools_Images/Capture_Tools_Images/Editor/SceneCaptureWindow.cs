using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Tools.Editor
{
    /// <summary>
    /// Editor window for capturing the current SceneView camera angle as a PNG image.
    /// Allows configuring resolution, output directory, and filename prefix.
    /// </summary>
    public class SceneCaptureWindow : EditorWindow
    {
        private const string DEFAULT_OUTPUT_DIRECTORY = "Assets/Captures/Scene";
        private const string DEFAULT_FILENAME_PREFIX = "SceneCapture";
        private const int MIN_RESOLUTION = 64;
        private const int MAX_RESOLUTION = 8192;
        private const int DEFAULT_WIDTH = 1920;
        private const int DEFAULT_HEIGHT = 1080;
        private const int RENDER_TEXTURE_DEPTH = 24;

        private string outputDirectory = DEFAULT_OUTPUT_DIRECTORY;
        private int captureWidth = DEFAULT_WIDTH;
        private int captureHeight = DEFAULT_HEIGHT;
        private string filenamePrefix = DEFAULT_FILENAME_PREFIX;
        private bool includeUI;

        [MenuItem("Tools/Capture/Scene Capture Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<SceneCaptureWindow>("Scene Capture");
            window.minSize = new Vector2(350, 220);
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Scene Capture Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            DrawOutputDirectoryField();
            EditorGUILayout.Space(2);

            filenamePrefix = EditorGUILayout.TextField("Filename Prefix", filenamePrefix);
            captureWidth = Mathf.Clamp(EditorGUILayout.IntField("Width", captureWidth), MIN_RESOLUTION, MAX_RESOLUTION);
            captureHeight = Mathf.Clamp(EditorGUILayout.IntField("Height", captureHeight), MIN_RESOLUTION, MAX_RESOLUTION);
            includeUI = EditorGUILayout.Toggle("Include Gizmos/Overlays", includeUI);

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Capture", GUILayout.Height(30)))
            {
                CaptureSceneView();
            }
        }

        /// <summary>
        /// Draws the output directory field with a folder picker button.
        /// </summary>
        private void DrawOutputDirectoryField()
        {
            EditorGUILayout.BeginHorizontal();
            outputDirectory = EditorGUILayout.TextField("Output Directory", outputDirectory);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string selected = EditorUtility.OpenFolderPanel("Select Output Folder", outputDirectory, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    // Convert absolute path to project-relative path if inside the project
                    string projectPath = Application.dataPath;
                    if (selected.StartsWith(projectPath))
                    {
                        outputDirectory = "Assets" + selected.Substring(projectPath.Length);
                    }
                    else
                    {
                        outputDirectory = selected;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Captures the current SceneView camera and saves it as a PNG.
        /// </summary>
        private void CaptureSceneView()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                EditorUtility.DisplayDialog("Scene Capture", "No active SceneView found.", "OK");
                return;
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                EditorUtility.DisplayDialog("Scene Capture", "Output directory is empty or invalid.", "OK");
                return;
            }

            GameObject tempCameraGO = null;
            RenderTexture renderTexture = null;
            Texture2D captureTexture = null;

            try
            {
                Camera sceneCamera = sceneView.camera;

                // Create temporary camera matching the SceneView camera
                tempCameraGO = new GameObject("__TempSceneCaptureCam__");
                tempCameraGO.hideFlags = HideFlags.HideAndDontSave;

                Camera tempCamera = tempCameraGO.AddComponent<Camera>();
                tempCameraGO.transform.position = sceneCamera.transform.position;
                tempCameraGO.transform.rotation = sceneCamera.transform.rotation;
                tempCamera.fieldOfView = sceneCamera.fieldOfView;
                tempCamera.nearClipPlane = sceneCamera.nearClipPlane;
                tempCamera.farClipPlane = sceneCamera.farClipPlane;
                tempCamera.orthographic = sceneCamera.orthographic;
                tempCamera.orthographicSize = sceneCamera.orthographicSize;

                // Configure URP camera data
                var urpData = tempCameraGO.AddComponent<UniversalAdditionalCameraData>();
                urpData.renderType = CameraRenderType.Base;

                if (!includeUI)
                {
                    tempCamera.clearFlags = CameraClearFlags.Skybox;
                }

                // Create RenderTexture
                renderTexture = new RenderTexture(captureWidth, captureHeight, RENDER_TEXTURE_DEPTH,
                    RenderTextureFormat.ARGBHalf);
                renderTexture.Create();

                tempCamera.targetTexture = renderTexture;
                tempCamera.Render();

                // Read pixels from RenderTexture
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = renderTexture;

                captureTexture = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false);
                captureTexture.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                captureTexture.Apply();

                RenderTexture.active = previousActive;

                // Encode and save
                byte[] pngData = ImageConversion.EncodeToPNG(captureTexture);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = $"{filenamePrefix}_{timestamp}.png";

                Directory.CreateDirectory(outputDirectory);
                string fullPath = Path.Combine(outputDirectory, filename);
                File.WriteAllBytes(fullPath, pngData);

                AssetDatabase.Refresh();

                // Ping the newly created asset
                var savedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fullPath);
                if (savedAsset != null)
                {
                    EditorGUIUtility.PingObject(savedAsset);
                }

                Debug.Log($"[SceneCapture] Saved capture to: {fullPath} ({captureWidth}x{captureHeight})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SceneCapture] Capture failed: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                if (tempCameraGO != null)
                    DestroyImmediate(tempCameraGO);

                if (renderTexture != null)
                {
                    renderTexture.Release();
                    DestroyImmediate(renderTexture);
                }

                if (captureTexture != null)
                    DestroyImmediate(captureTexture);
            }
        }
    }
}
