using System.Collections.Generic;
using UnityEngine;

namespace ToolsTesting
{
    /// <summary>
    /// Interpolation strategy for camera movement between waypoints.
    /// </summary>
    public enum PathInterpolationMode
    {
        Linear,
        CatmullRomSpline
    }

    /// <summary>
    /// Snapshot of the camera state at a point along the path.
    /// </summary>
    public struct WaypointSample
    {
        public Vector3 Position;
        public float FieldOfView;
    }

    /// <summary>
    /// Container for CameraWaypoint children. Evaluates smooth or linear
    /// camera paths with per-waypoint FOV and dwell time support.
    /// Add CameraWaypoint GameObjects as children; their sibling order defines the path.
    /// </summary>
    [DisallowMultipleComponent]
    public class WaypointPath : MonoBehaviour
    {
        private const int GIZMO_CURVE_STEPS = 50;

        [Header("Look-At")]
        [Tooltip("The GameObject the camera should always face.")]
        [SerializeField] private Transform lookAtTarget;

        [Tooltip("World-space offset from the target pivot (e.g., raise Y to focus on the head).")]
        [SerializeField] private Vector3 lookAtOffset = Vector3.zero;

        [Header("Path")]
        [Tooltip("How the camera interpolates between waypoints.")]
        [SerializeField] private PathInterpolationMode interpolationMode = PathInterpolationMode.CatmullRomSpline;

        [Tooltip("If true, the path loops from the last waypoint back to the first.")]
        [SerializeField] private bool closedLoop;

        public Transform LookAtTarget => lookAtTarget;
        public Vector3 LookAtOffset => lookAtOffset;
        public PathInterpolationMode InterpolationMode => interpolationMode;
        public bool ClosedLoop => closedLoop;

        /// <summary>
        /// Returns the world-space point the camera should look at.
        /// </summary>
        public Vector3 GetLookAtPoint()
        {
            if (lookAtTarget == null)
            {
                return transform.position;
            }

            return lookAtTarget.position + lookAtOffset;
        }

        /// <summary>
        /// Collects all active CameraWaypoint children in sibling order.
        /// </summary>
        public List<CameraWaypoint> GetWaypoints()
        {
            var waypoints = new List<CameraWaypoint>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var wp = transform.GetChild(i).GetComponent<CameraWaypoint>();
                if (wp != null && wp.gameObject.activeSelf)
                {
                    waypoints.Add(wp);
                }
            }

            return waypoints;
        }

        /// <summary>
        /// Returns the sum of all waypoint dwell times.
        /// </summary>
        public float GetTotalDwellTime()
        {
            float total = 0f;
            foreach (var wp in GetWaypoints())
            {
                total += wp.DwellTime;
            }

            return total;
        }

        /// <summary>
        /// Evaluates the path at normalized time t (0..1), factoring in dwell periods.
        /// During a dwell period the camera holds position; during travel it interpolates.
        /// </summary>
        public WaypointSample Evaluate(float t, float totalDuration)
        {
            var waypoints = GetWaypoints();

            if (waypoints.Count == 0)
            {
                return new WaypointSample
                {
                    Position = transform.position,
                    FieldOfView = 30f
                };
            }

            if (waypoints.Count == 1)
            {
                return new WaypointSample
                {
                    Position = waypoints[0].transform.position,
                    FieldOfView = waypoints[0].FieldOfView
                };
            }

            int segmentCount = closedLoop ? waypoints.Count : waypoints.Count - 1;
            float totalDwell = GetTotalDwellTime();
            float travelTime = Mathf.Max(0f, totalDuration - totalDwell);
            float timePerSegment = segmentCount > 0 ? travelTime / segmentCount : 0f;

            float currentTime = t * totalDuration;
            float accumulated = 0f;

            for (int i = 0; i < waypoints.Count; i++)
            {
                // --- Dwell at waypoint i ---
                float dwell = waypoints[i].DwellTime;
                if (currentTime <= accumulated + dwell)
                {
                    return new WaypointSample
                    {
                        Position = waypoints[i].transform.position,
                        FieldOfView = waypoints[i].FieldOfView
                    };
                }

                accumulated += dwell;

                // --- Travel from waypoint i to next ---
                int nextIndex;
                if (closedLoop)
                {
                    nextIndex = (i + 1) % waypoints.Count;
                }
                else
                {
                    if (i >= waypoints.Count - 1)
                    {
                        break;
                    }

                    nextIndex = i + 1;
                }

                if (currentTime <= accumulated + timePerSegment)
                {
                    float segmentT = timePerSegment > 0f
                        ? (currentTime - accumulated) / timePerSegment
                        : 0f;
                    return InterpolateSegment(waypoints, i, nextIndex, segmentT);
                }

                accumulated += timePerSegment;
            }

            // Clamp to last waypoint
            var last = waypoints[waypoints.Count - 1];
            return new WaypointSample
            {
                Position = last.transform.position,
                FieldOfView = last.FieldOfView
            };
        }

        private WaypointSample InterpolateSegment(
            List<CameraWaypoint> waypoints, int fromIndex, int toIndex, float t)
        {
            var from = waypoints[fromIndex];
            var to = waypoints[toIndex];

            Vector3 position;
            if (interpolationMode == PathInterpolationMode.CatmullRomSpline)
            {
                Vector3 p0 = GetControlPoint(waypoints, fromIndex - 1);
                Vector3 p1 = from.transform.position;
                Vector3 p2 = to.transform.position;
                Vector3 p3 = GetControlPoint(waypoints, toIndex + 1);
                position = CatmullRom(p0, p1, p2, p3, t);
            }
            else
            {
                position = Vector3.Lerp(from.transform.position, to.transform.position, t);
            }

            float fov = Mathf.Lerp(from.FieldOfView, to.FieldOfView, t);

            return new WaypointSample
            {
                Position = position,
                FieldOfView = fov
            };
        }

        private Vector3 GetControlPoint(List<CameraWaypoint> waypoints, int index)
        {
            if (closedLoop)
            {
                index = ((index % waypoints.Count) + waypoints.Count) % waypoints.Count;
                return waypoints[index].transform.position;
            }

            index = Mathf.Clamp(index, 0, waypoints.Count - 1);
            return waypoints[index].transform.position;
        }

        /// <summary>
        /// Catmull-Rom spline interpolation between p1 and p2,
        /// using p0 and p3 as tangent control points.
        /// </summary>
        public static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        // ----- Gizmo Visualization -----

        private void OnDrawGizmos()
        {
            DrawPathGizmos(false);
        }

        private void OnDrawGizmosSelected()
        {
            DrawPathGizmos(true);
        }

        private void DrawPathGizmos(bool selected)
        {
            var waypoints = GetWaypoints();
            if (waypoints.Count < 2)
            {
                return;
            }

            Gizmos.color = selected
                ? new Color(1f, 0.9f, 0.2f, 0.9f)
                : new Color(0.2f, 0.8f, 1f, 0.5f);

            int segmentCount = closedLoop ? waypoints.Count : waypoints.Count - 1;

            for (int seg = 0; seg < segmentCount; seg++)
            {
                int fromIndex = seg;
                int toIndex = closedLoop ? (seg + 1) % waypoints.Count : seg + 1;

                if (interpolationMode == PathInterpolationMode.CatmullRomSpline)
                {
                    DrawSplineSegment(waypoints, fromIndex, toIndex);
                }
                else
                {
                    Gizmos.DrawLine(
                        waypoints[fromIndex].transform.position,
                        waypoints[toIndex].transform.position);
                }
            }

            if (lookAtTarget != null && selected)
            {
                DrawLookAtLines(waypoints);
            }
        }

        private void DrawSplineSegment(List<CameraWaypoint> waypoints, int fromIndex, int toIndex)
        {
            Vector3 p0 = GetControlPoint(waypoints, fromIndex - 1);
            Vector3 p1 = waypoints[fromIndex].transform.position;
            Vector3 p2 = waypoints[toIndex].transform.position;
            Vector3 p3 = GetControlPoint(waypoints, toIndex + 1);

            Vector3 prev = p1;
            for (int i = 1; i <= GIZMO_CURVE_STEPS; i++)
            {
                float st = (float)i / GIZMO_CURVE_STEPS;
                Vector3 curr = CatmullRom(p0, p1, p2, p3, st);
                Gizmos.DrawLine(prev, curr);
                prev = curr;
            }
        }

        private void DrawLookAtLines(List<CameraWaypoint> waypoints)
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.4f);
            Vector3 lookAt = GetLookAtPoint();
            foreach (var wp in waypoints)
            {
                Gizmos.DrawLine(wp.transform.position, lookAt);
            }
        }
    }
}
