using System;
using System.IO;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ToolsTesting.Editor
{
    /// <summary>
    /// Recording mode selection for the pan recorder.
    /// </summary>
    public enum RecordingMode
    {
        OrbitPan360,
        WaypointPath
    }

    /// <summary>
    /// Editor window that records video of a model using either a 360-degree orbit
    /// or a user-defined waypoint path. Always keeps the camera aimed at the target.
    /// </summary>
    public class ModelPanRecorder : EditorWindow
    {
        // ----- Constants -----
        private const int DEFAULT_WIDTH = 1920;
        private const int DEFAULT_HEIGHT = 1080;
        private const int DEFAULT_FRAME_RATE = 30;
        private const float DEFAULT_DURATION = 8f;
        private const float DEFAULT_ORBIT_DISTANCE_MULTIPLIER = 2.5f;
        private const float DEFAULT_CAMERA_HEIGHT_FACTOR = 0.5f;
        private const float DEFAULT_FIELD_OF_VIEW = 30f;
        private const float MIN_ORBIT_DISTANCE = 1.5f;
        private const float MAX_ORBIT_DISTANCE = 6f;
        private const int RENDER_TEXTURE_DEPTH = 24;
        private const int ANTI_ALIASING_SAMPLES = 4;
        private const float FULL_ROTATION_DEGREES = 360f;
        private const string OUTPUT_FOLDER = "Assets/Recordings";
        private const string TEMP_CAMERA_NAME = "__PanRecorderCamera__";

        // ----- Shared Settings -----
        [SerializeField] private RecordingMode recordingMode = RecordingMode.OrbitPan360;
        [SerializeField] private int videoWidth = DEFAULT_WIDTH;
        [SerializeField] private int videoHeight = DEFAULT_HEIGHT;
        [SerializeField] private int frameRate = DEFAULT_FRAME_RATE;
        [SerializeField] private float duration = DEFAULT_DURATION;
        [SerializeField] private Color backgroundColor = new Color(0.15f, 0.15f, 0.15f, 1f);

        // ----- 360 Orbit Settings -----
        [SerializeField] private float orbitDistanceMultiplier = DEFAULT_ORBIT_DISTANCE_MULTIPLIER;
        [SerializeField] private float cameraHeightFactor = DEFAULT_CAMERA_HEIGHT_FACTOR;

        // ----- Waypoint Settings -----
        [SerializeField] private WaypointPath waypointPath;

        private Vector2 scrollPosition;

        /// <summary>
        /// Opens the recorder editor window.
        /// </summary>
        [MenuItem("Tools/Record 360 Pan")]
        private static void ShowWindow()
        {
            var window = GetWindow<ModelPanRecorder>("360 Pan Recorder");
            window.minSize = new Vector2(400, 420);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.LabelField("Model Pan Video Recorder", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);

            DrawModeSelection();
            EditorGUILayout.Space(6);
            DrawVideoSettings();
            EditorGUILayout.Space(6);

            if (recordingMode == RecordingMode.OrbitPan360)
            {
                DrawOrbitSettings();
            }
            else
            {
                DrawWaypointSettings();
            }

            EditorGUILayout.Space(6);
            DrawTargetInfo();
            EditorGUILayout.Space(8);
            DrawRecordButton();

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        //  GUI Drawing
        // ================================================================

        private void DrawModeSelection()
        {
            recordingMode = (RecordingMode)EditorGUILayout.EnumPopup("Recording Mode", recordingMode);
        }

        private void DrawVideoSettings()
        {
            EditorGUILayout.LabelField("Video Settings", EditorStyles.boldLabel);
            videoWidth = Mathf.Max(128, EditorGUILayout.IntField("Width (px)", videoWidth));
            videoHeight = Mathf.Max(128, EditorGUILayout.IntField("Height (px)", videoHeight));
            frameRate = Mathf.Clamp(EditorGUILayout.IntField("Frame Rate", frameRate), 10, 120);
            duration = Mathf.Max(1f, EditorGUILayout.FloatField("Duration (seconds)", duration));
            backgroundColor = EditorGUILayout.ColorField("Background Color", backgroundColor);
        }

        private void DrawOrbitSettings()
        {
            EditorGUILayout.LabelField("360 Orbit Settings", EditorStyles.boldLabel);
            orbitDistanceMultiplier = EditorGUILayout.Slider(
                "Orbit Distance", orbitDistanceMultiplier, MIN_ORBIT_DISTANCE, MAX_ORBIT_DISTANCE);
            cameraHeightFactor = EditorGUILayout.Slider(
                "Camera Height Offset", cameraHeightFactor, 0f, 1f);
        }

        private void DrawWaypointSettings()
        {
            EditorGUILayout.LabelField("Waypoint Path Settings", EditorStyles.boldLabel);
            waypointPath = (WaypointPath)EditorGUILayout.ObjectField(
                "Waypoint Path", waypointPath, typeof(WaypointPath), true);

            if (waypointPath == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a WaypointPath from the scene.\n" +
                    "Create one via GameObject > Create Empty, add the WaypointPath component, " +
                    "then use its Inspector to add child waypoints.",
                    MessageType.Warning);
                return;
            }

            var waypoints = waypointPath.GetWaypoints();
            float totalDwell = waypointPath.GetTotalDwellTime();

            string lookAtName = waypointPath.LookAtTarget != null
                ? waypointPath.LookAtTarget.name
                : "None (set on the WaypointPath component)";

            EditorGUILayout.HelpBox(
                $"Waypoints: {waypoints.Count}\n" +
                $"Interpolation: {waypointPath.InterpolationMode}\n" +
                $"Total dwell: {totalDwell:F1}s | Loop: {waypointPath.ClosedLoop}\n" +
                $"Look-At Target: {lookAtName}",
                MessageType.Info);

            if (totalDwell >= duration)
            {
                EditorGUILayout.HelpBox(
                    "Total dwell time exceeds the recording duration. " +
                    "Increase duration or reduce per-waypoint dwell times.",
                    MessageType.Error);
            }

            if (waypoints.Count < 2)
            {
                EditorGUILayout.HelpBox(
                    "At least 2 waypoints are needed for a path recording.",
                    MessageType.Warning);
            }
        }

        private void DrawTargetInfo()
        {
            if (recordingMode == RecordingMode.OrbitPan360)
            {
                GameObject selected = Selection.activeGameObject;
                if (selected != null)
                {
                    EditorGUILayout.HelpBox($"Orbit Target: {selected.name}", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Select a GameObject in the scene to orbit around.", MessageType.Warning);
                }
            }
        }

        private void DrawRecordButton()
        {
            bool canRecord = CanRecord();
            EditorGUI.BeginDisabledGroup(!canRecord);

            string buttonLabel = recordingMode == RecordingMode.OrbitPan360
                ? "Record 360 Pan"
                : "Record Waypoint Path";

            if (GUILayout.Button(buttonLabel, GUILayout.Height(36)))
            {
                if (recordingMode == RecordingMode.OrbitPan360)
                {
                    RecordOrbitPan(Selection.activeGameObject);
                }
                else
                {
                    RecordWaypointPath();
                }
            }

            EditorGUI.EndDisabledGroup();
        }

        private bool CanRecord()
        {
            if (recordingMode == RecordingMode.OrbitPan360)
            {
                return Selection.activeGameObject != null;
            }

            if (waypointPath == null)
            {
                return false;
            }

            var waypoints = waypointPath.GetWaypoints();
            return waypoints.Count >= 2 && waypointPath.GetTotalDwellTime() < duration;
        }

        // ================================================================
        //  Recording - 360 Orbit (original mode)
        // ================================================================

        /// <summary>
        /// Records a 360-degree orbit video around the specified target.
        /// </summary>
        private void RecordOrbitPan(GameObject target)
        {
            if (!TryCalculateBounds(target, out Bounds bounds))
            {
                Debug.LogError("[ModelPanRecorder] No renderers found on the target or its children.");
                return;
            }

            Vector3 center = bounds.center;
            float radius = bounds.extents.magnitude;
            float orbitDistance = radius * orbitDistanceMultiplier;
            float cameraY = center.y + bounds.extents.y * cameraHeightFactor;

            string filePath = BuildOutputPath($"{SanitizeFileName(target.name)}_360Pan");
            int totalFrames = Mathf.RoundToInt(duration * frameRate);

            RecordFrames(filePath, totalFrames, (progress, camera) =>
            {
                PositionCameraOrbit(camera.transform, center, orbitDistance, cameraY, progress);
                camera.fieldOfView = DEFAULT_FIELD_OF_VIEW;
            });
        }

        private static void PositionCameraOrbit(
            Transform cameraTransform, Vector3 center, float orbitDistance, float cameraY, float progress)
        {
            float angle = progress * FULL_ROTATION_DEGREES * Mathf.Deg2Rad;
            float x = center.x + Mathf.Sin(angle) * orbitDistance;
            float z = center.z + Mathf.Cos(angle) * orbitDistance;

            cameraTransform.position = new Vector3(x, cameraY, z);
            cameraTransform.LookAt(center);
        }

        // ================================================================
        //  Recording - Waypoint Path
        // ================================================================

        /// <summary>
        /// Records video following the assigned waypoint path.
        /// </summary>
        private void RecordWaypointPath()
        {
            string targetName = waypointPath.LookAtTarget != null
                ? waypointPath.LookAtTarget.name
                : waypointPath.name;

            string filePath = BuildOutputPath($"{SanitizeFileName(targetName)}_WaypointPan");
            int totalFrames = Mathf.RoundToInt(duration * frameRate);
            Vector3 lookAtPoint = waypointPath.GetLookAtPoint();

            RecordFrames(filePath, totalFrames, (progress, camera) =>
            {
                WaypointSample sample = waypointPath.Evaluate(progress, duration);
                camera.transform.position = sample.Position;
                camera.transform.LookAt(lookAtPoint);
                camera.fieldOfView = sample.FieldOfView;
            });
        }

        // ================================================================
        //  Shared Recording Core
        // ================================================================

        /// <summary>
        /// Core recording loop shared by both modes.
        /// The positionCallback receives normalized progress (0..1) and the Camera,
        /// and must set the camera's position, rotation, and FOV for that frame.
        /// </summary>
        private void RecordFrames(string filePath, int totalFrames, Action<float, Camera> positionCallback)
        {
            EnsureOutputDirectory();

            GameObject cameraGO = null;
            Camera camera = null;
            RenderTexture rt = null;
            Texture2D tex = null;

            try
            {
                cameraGO = CreateRecordingCamera(out camera);
                rt = CreateRenderTexture();
                camera.targetTexture = rt;
                tex = new Texture2D(videoWidth, videoHeight, TextureFormat.RGBA32, false);

                var videoAttrs = new VideoTrackAttributes
                {
                    frameRate = new MediaRational(frameRate),
                    width = (uint)videoWidth,
                    height = (uint)videoHeight,
                    includeAlpha = false,
                    bitRateMode = VideoBitrateMode.High
                };

                using (var encoder = new MediaEncoder(filePath, videoAttrs))
                {
                    for (int i = 0; i < totalFrames; i++)
                    {
                        float progress = (float)i / totalFrames;

                        if (EditorUtility.DisplayCancelableProgressBar(
                            "Recording",
                            $"Frame {i + 1} / {totalFrames}",
                            progress))
                        {
                            Debug.LogWarning("[ModelPanRecorder] Recording cancelled by user.");
                            break;
                        }

                        positionCallback(progress, camera);
                        camera.Render();
                        CaptureFrame(rt, tex);
                        encoder.AddFrame(tex);
                    }
                }

                AssetDatabase.Refresh();
                Debug.Log($"[ModelPanRecorder] Video saved to: {filePath}");

                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(filePath);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ModelPanRecorder] Recording failed: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                CleanupResources(cameraGO, rt, tex);
            }
        }

        // ================================================================
        //  Helpers
        // ================================================================

        private static bool TryCalculateBounds(GameObject target, out Bounds bounds)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            bounds = default;

            if (renderers.Length == 0)
            {
                return false;
            }

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private GameObject CreateRecordingCamera(out Camera camera)
        {
            var cameraGO = new GameObject(TEMP_CAMERA_NAME);
            cameraGO.hideFlags = HideFlags.HideAndDontSave;
            camera = cameraGO.AddComponent<Camera>();
            cameraGO.AddComponent<UniversalAdditionalCameraData>();

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = backgroundColor;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 1000f;
            camera.fieldOfView = DEFAULT_FIELD_OF_VIEW;
            camera.enabled = false;

            return cameraGO;
        }

        private RenderTexture CreateRenderTexture()
        {
            var rt = new RenderTexture(
                videoWidth, videoHeight, RENDER_TEXTURE_DEPTH, RenderTextureFormat.ARGB32);
            rt.antiAliasing = ANTI_ALIASING_SAMPLES;
            rt.Create();
            return rt;
        }

        private static void CaptureFrame(RenderTexture rt, Texture2D tex)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
        }

        private static void CleanupResources(GameObject cameraGO, RenderTexture rt, Texture2D tex)
        {
            if (tex != null)
            {
                DestroyImmediate(tex);
            }

            if (rt != null)
            {
                rt.Release();
                DestroyImmediate(rt);
            }

            if (cameraGO != null)
            {
                DestroyImmediate(cameraGO);
            }
        }

        private static string BuildOutputPath(string baseName)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{baseName}_{timestamp}.mp4";
            return Path.Combine(OUTPUT_FOLDER, fileName);
        }

        private static void EnsureOutputDirectory()
        {
            if (!Directory.Exists(OUTPUT_FOLDER))
            {
                Directory.CreateDirectory(OUTPUT_FOLDER);
            }
        }

        private static string SanitizeFileName(string name)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }

            return name;
        }
    }
}
