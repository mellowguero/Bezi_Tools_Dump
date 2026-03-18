using UnityEditor;
using UnityEngine;

namespace ToolsTesting.Editor
{
    /// <summary>
    /// Custom inspector for WaypointPath with quick waypoint creation.
    /// </summary>
    [CustomEditor(typeof(WaypointPath))]
    public class WaypointPathEditor : UnityEditor.Editor
    {
        private const string ADD_WAYPOINT_UNDO = "Add Camera Waypoint";

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            EditorGUILayout.Space(8);

            var path = (WaypointPath)target;
            var waypoints = path.GetWaypoints();

            DrawWaypointSummary(path, waypoints);
            EditorGUILayout.Space(4);
            DrawButtons(path);
        }

        private void DrawWaypointSummary(WaypointPath path, System.Collections.Generic.List<CameraWaypoint> waypoints)
        {
            EditorGUILayout.LabelField("Waypoints", EditorStyles.boldLabel);

            if (waypoints.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No waypoints yet. Click 'Add Waypoint' to place one at the current Scene view position.",
                    MessageType.Info);
                return;
            }

            int segmentCount = path.ClosedLoop ? waypoints.Count : waypoints.Count - 1;
            float totalDwell = path.GetTotalDwellTime();

            EditorGUILayout.HelpBox(
                $"Count: {waypoints.Count}\n" +
                $"Travel segments: {segmentCount}\n" +
                $"Total dwell time: {totalDwell:F1}s",
                MessageType.Info);
        }

        private void DrawButtons(WaypointPath path)
        {
            if (GUILayout.Button("Add Waypoint", GUILayout.Height(28)))
            {
                AddWaypoint(path);
            }
        }

        private void AddWaypoint(WaypointPath path)
        {
            int count = path.GetWaypoints().Count;
            var waypointGO = new GameObject($"Waypoint_{count}");
            Undo.RegisterCreatedObjectUndo(waypointGO, ADD_WAYPOINT_UNDO);

            waypointGO.transform.SetParent(path.transform);

            if (SceneView.lastActiveSceneView != null)
            {
                waypointGO.transform.position = SceneView.lastActiveSceneView.camera.transform.position;
            }
            else
            {
                waypointGO.transform.localPosition = Vector3.forward * (count + 1) * 2f;
            }

            Undo.AddComponent<CameraWaypoint>(waypointGO);
            Selection.activeGameObject = waypointGO;
        }
    }
}
