using UnityEngine;
using UnityEngine.Serialization;

namespace CustomNavigation.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DataSakura/Custom Navigation/Navigation Link")]
    public sealed class NavigationLink : MonoBehaviour
    {
        [SerializeField, Tooltip("Stable link id used by the artifact, the gameplay and diagnostics.")]
        private string linkId;
        [SerializeField, Tooltip("Gameplay semantics of the link: jump, drop, ladder, teleport or a scripted action.")]
        private NavigationLinkType linkType = NavigationLinkType.Jump;
        [SerializeField, Tooltip("Link start in the local coordinates of the Transform.")]
        private Vector3 localStart = Vector3.left;
        [SerializeField, Tooltip("Link end in the local coordinates of the Transform.")]
        private Vector3 localEnd = Vector3.right;
        [SerializeField, Tooltip("Allow traversing the link in both directions.")]
        private bool bidirectional;
        [SerializeField, Min(0.01f), Tooltip("Snap radius that binds the link endpoints to the nearest navmesh polygons.")]
        private float radius = 0.45f;
        [SerializeField, Min(1f), Tooltip("Traversal cost multiplier used when choosing a route.")]
        private float cost = 1f;
        [SerializeField, FormerlySerializedAs("areaId"), Tooltip("Surface type of the traversal link.")]
        private NavigationArea area = NavigationArea.Ground;

        public string LinkId => linkId;
        public NavigationLinkType LinkType => linkType;
        public Vector3 WorldStart => transform.TransformPoint(localStart);
        public Vector3 WorldEnd => transform.TransformPoint(localEnd);
        public bool Bidirectional => bidirectional;
        public float Radius => radius;
        public float Cost => cost;
        public NavigationArea Area => area;
        public int AreaId => (int)area;

        private void Reset()
        {
            linkId = NavigationIdUtility.CreateStableId("link");
        }

        private void OnValidate()
        {
            linkId = string.IsNullOrWhiteSpace(linkId)
                ? NavigationIdUtility.CreateStableId("link")
                : NavigationIdUtility.Sanitize(linkId, "link");
            radius = Mathf.Max(0.01f, radius);
            cost = Mathf.Max(1f, cost);
        }

        private void OnDrawGizmos()
        {
            if (!NavigationHighlightSettings.Enabled)
            {
                return;
            }

            DrawLinkGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (NavigationHighlightSettings.Enabled)
            {
                return;
            }

            DrawLinkGizmo();
        }

        private void DrawLinkGizmo()
        {
            Vector3 start = WorldStart;
            Vector3 end = WorldEnd;
            Gizmos.color = NavigationHighlightPalette.Link;
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(start, radius);
            Gizmos.DrawWireSphere(end, radius);

            Vector3 direction = end - start;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector3 forward = direction.normalized;
            Vector3 side = Vector3.Cross(forward, Vector3.up);
            if (side.sqrMagnitude <= 0.0001f)
            {
                side = Vector3.Cross(forward, Vector3.forward);
            }

            side = side.normalized * (radius * 0.45f);
            float headLength = Mathf.Min(radius, direction.magnitude * 0.25f);
            DrawArrowHead(end, forward, side, headLength);
            if (bidirectional)
            {
                DrawArrowHead(start, -forward, side, headLength);
            }
        }

        private static void DrawArrowHead(Vector3 tip, Vector3 forward, Vector3 side, float headLength)
        {
            Vector3 tail = tip - forward * headLength;
            Gizmos.DrawLine(tip, tail + side);
            Gizmos.DrawLine(tip, tail - side);
        }
    }
}
