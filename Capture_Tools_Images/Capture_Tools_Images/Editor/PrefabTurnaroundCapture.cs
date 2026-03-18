using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Tools.Editor
{
    /// <summary>
    /// Context menu action on prefab assets that captures 4 turnaround views
    /// (Front, Right, Back, Left) with transparent backgrounds and saves them as PNGs.
    /// </summary>
    public static class PrefabTurnaroundCapture
    {
        private const string DEFAULT_OUTPUT_DIRECTORY = "Assets/Captures/Prefabs";
        private const int CAPTURE_SIZE = 1024;
        private const float CAMERA_DISTANCE_MULTIPLIER = 3.5f;
        private const float CAMERA_FOV = 30f;
        private const float CAMERA_PITCH = 15f;
        private const float BOUNDS_PADDING = 1.2f;
        private const int RENDER_TEXTURE_DEPTH = 24;
        private const int ISOLATION_LAYER = 31;

        private static readonly (string label, float angle)[] CaptureAngles =
        {
            ("Front", 0f),
            ("Right", 90f),
            ("Back", 180f),
            ("Left", 270f)
        };

        [MenuItem("Assets/Capture Prefab Turnaround", true)]
        private static bool ValidateCapture()
        {
            GameObject selected = Selection.activeObject as GameObject;
            if (selected == null)
                return false;

            PrefabAssetType prefabType = PrefabUtility.GetPrefabAssetType(selected);
            return prefabType != PrefabAssetType.NotAPrefab;
        }

        [MenuItem("Assets/Capture Prefab Turnaround")]
        private static void ExecuteCapture()
        {
            GameObject prefabAsset = Selection.activeObject as GameObject;
            if (prefabAsset == null)
                return;

            string prefabName = prefabAsset.name;
            string prefabPath = AssetDatabase.GetAssetPath(prefabAsset);

            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene tempScene = default;
            GameObject instantiatedPrefab = null;
            GameObject cameraGO = null;
            GameObject lightGO = null;
            RenderTexture renderTexture = null;

            try
            {
                // Create a temporary empty scene
                tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(tempScene);

                // Instantiate the prefab
                instantiatedPrefab = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, tempScene);
                if (instantiatedPrefab == null)
                {
                    instantiatedPrefab = UnityEngine.Object.Instantiate(prefabAsset);
                    SceneManager.MoveGameObjectToScene(instantiatedPrefab, tempScene);
                }

                // Move prefab to isolation layer so the camera only sees it
                SetLayerRecursively(instantiatedPrefab, ISOLATION_LAYER);

                // Calculate bounds from all renderers
                Renderer[] renderers = instantiatedPrefab.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    EditorUtility.DisplayDialog("Prefab Turnaround",
                        "Prefab has no renderers — cannot calculate bounds.", "OK");
                    return;
                }

                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                float distance = bounds.extents.magnitude * BOUNDS_PADDING * CAMERA_DISTANCE_MULTIPLIER;
                if (distance < 0.01f)
                    distance = 2f;

                Vector3 boundsCenter = bounds.center;

                // Clear environment lighting so it doesn't bleed into the render
                RenderSettings.skybox = null;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = Color.white;
                RenderSettings.reflectionIntensity = 0f;

                // Add a directional light for clean illumination
                lightGO = new GameObject("__TempLight__");
                lightGO.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(lightGO, tempScene);
                lightGO.layer = ISOLATION_LAYER;
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1f;
                light.cullingMask = 1 << ISOLATION_LAYER;
                lightGO.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

                // Create temporary camera — only renders the isolation layer
                cameraGO = new GameObject("__TempTurnaroundCam__");
                cameraGO.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(cameraGO, tempScene);
                cameraGO.layer = ISOLATION_LAYER;

                Camera camera = cameraGO.AddComponent<Camera>();
                var urpData = cameraGO.AddComponent<UniversalAdditionalCameraData>();
                urpData.renderType = CameraRenderType.Base;
                urpData.renderPostProcessing = false;

                camera.cullingMask = 1 << ISOLATION_LAYER;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.fieldOfView = CAMERA_FOV;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = distance * 10f;

                // Use ARGBHalf format for proper alpha channel support in URP
                renderTexture = new RenderTexture(CAPTURE_SIZE, CAPTURE_SIZE, RENDER_TEXTURE_DEPTH,
                    RenderTextureFormat.ARGBHalf);
                renderTexture.Create();

                camera.targetTexture = renderTexture;

                // Ensure output directory exists
                Directory.CreateDirectory(DEFAULT_OUTPUT_DIRECTORY);

                // Capture each angle
                foreach (var (label, angle) in CaptureAngles)
                {
                    Quaternion rotation = Quaternion.Euler(CAMERA_PITCH, angle, 0f);
                    Vector3 offset = rotation * (Vector3.back * distance);
                    cameraGO.transform.position = boundsCenter + offset;
                    cameraGO.transform.LookAt(boundsCenter);

                    camera.Render();

                    // Read pixels
                    RenderTexture previousActive = RenderTexture.active;
                    RenderTexture.active = renderTexture;

                    Texture2D captureTexture = new Texture2D(CAPTURE_SIZE, CAPTURE_SIZE,
                        TextureFormat.RGBA32, false);
                    captureTexture.ReadPixels(new Rect(0, 0, CAPTURE_SIZE, CAPTURE_SIZE), 0, 0);
                    captureTexture.Apply();

                    RenderTexture.active = previousActive;

                    // Encode and save
                    byte[] pngData = ImageConversion.EncodeToPNG(captureTexture);
                    string filename = $"{prefabName}_{label}.png";
                    string fullPath = Path.Combine(DEFAULT_OUTPUT_DIRECTORY, filename);
                    File.WriteAllBytes(fullPath, pngData);

                    UnityEngine.Object.DestroyImmediate(captureTexture);

                    Debug.Log($"[PrefabTurnaround] Saved: {fullPath}");
                }

                AssetDatabase.Refresh();

                // Select the output folder
                var folderAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DEFAULT_OUTPUT_DIRECTORY);
                if (folderAsset != null)
                {
                    EditorGUIUtility.PingObject(folderAsset);
                }

                Debug.Log($"[PrefabTurnaround] Completed turnaround capture for '{prefabName}' from '{prefabPath}'.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PrefabTurnaround] Capture failed: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                // Cleanup
                if (instantiatedPrefab != null)
                    UnityEngine.Object.DestroyImmediate(instantiatedPrefab);

                if (cameraGO != null)
                    UnityEngine.Object.DestroyImmediate(cameraGO);

                if (lightGO != null)
                    UnityEngine.Object.DestroyImmediate(lightGO);

                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }

                // Close and remove the temporary scene
                if (tempScene.IsValid() && tempScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(tempScene, true);
                }

                // Restore previous active scene
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousActiveScene);
                }
            }
        }

        /// <summary>
        /// Sets the layer on a GameObject and all of its children recursively.
        /// </summary>
        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }
    }
}
