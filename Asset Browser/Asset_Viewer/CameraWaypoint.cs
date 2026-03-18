using UnityEngine;

namespace ToolsTesting
{
    /// <summary>
    /// Marks a camera position along a waypoint path.
    /// Not rendered; only visible as gizmos in the Scene view.
    /// </summary>
    [DisallowMultipleComponent]
    public class CameraWaypoint : MonoBehaviour
    {
        private const float DEFAULT_FIELD_OF_VIEW = 30f;
        private const float DEFAULT_DWELL_TIME = 0f;
        private const float GIZMO_SPHERE_RADIUS = 0.15f;
        private const float GIZMO_SELECTED_SPHERE_RADIUS = 0.2f;
        private const float FOV_INDICATOR_LENGTH = 1f;

        [Tooltip("Camera field of view at this waypoint.")]
        [Range(10f, 120f)]
        [SerializeField] private float fieldOfView = DEFAULT_FIELD_OF_VIEW;

        [Tooltip("Seconds the camera holds at this waypoint before moving to the next.")]
        [Min(0f)]
        [SerializeField] private float dwellTime = DEFAULT_DWELL_TIME;

        /// <summary>Camera FOV at this waypoint.</summary>
        public float FieldOfView => fieldOfView;

        /// <summary>Seconds the camera pauses at this waypoint.</summary>
        public float DwellTime => dwellTime;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.DrawSphere(transform.position, GIZMO_SPHERE_RADIUS);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, GIZMO_SELECTED_SPHERE_RADIUS);
            DrawFovIndicator();
        }

        private void DrawFovIndicator()
        {
            float halfFov = fieldOfView * 0.5f;
            Vector3 forward = transform.forward;

            Vector3 right = Quaternion.AngleAxis(halfFov, transform.up) * forward;
            Vector3 left = Quaternion.AngleAxis(-halfFov, transform.up) * forward;
            Vector3 up = Quaternion.AngleAxis(-halfFov, transform.right) * forward;
            Vector3 down = Quaternion.AngleAxis(halfFov, transform.right) * forward;

            Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
            Gizmos.DrawRay(transform.position, right * FOV_INDICATOR_LENGTH);
            Gizmos.DrawRay(transform.position, left * FOV_INDICATOR_LENGTH);
            Gizmos.DrawRay(transform.position, up * FOV_INDICATOR_LENGTH);
            Gizmos.DrawRay(transform.position, down * FOV_INDICATOR_LENGTH);
        }
    }
}
