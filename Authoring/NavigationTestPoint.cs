using UnityEngine;

namespace CustomNavigation.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DataSakura/Custom Navigation/Navigation Test Point")]
    public sealed class NavigationTestPoint : MonoBehaviour
    {
        [SerializeField, Tooltip("Stable test point id used by validation and reports.")]
        private string pointId;
        [SerializeField, Tooltip("Gameplay purpose of the point: spawn, objective, patrol and so on.")]
        private NavigationTestPointType pointType = NavigationTestPointType.Generic;
        [SerializeField, Tooltip("Group of points validated together by a single validation scenario.")]
        private string group = "default";
        [SerializeField, Tooltip("When enabled, an unreachable point must block the production export.")]
        private bool required = true;

        public string PointId => pointId;
        public NavigationTestPointType PointType => pointType;
        public string Group => group;
        public bool Required => required;

        private void Reset()
        {
            pointId = NavigationIdUtility.CreateStableId("point");
        }

        private void OnValidate()
        {
            pointId = string.IsNullOrWhiteSpace(pointId)
                ? NavigationIdUtility.CreateStableId("point")
                : NavigationIdUtility.Sanitize(pointId, "point");
            group = NavigationIdUtility.Sanitize(group, "default");
        }

        private void OnDrawGizmos()
        {
            if (!NavigationHighlightSettings.Enabled)
            {
                return;
            }

            DrawTestPointGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (NavigationHighlightSettings.Enabled)
            {
                return;
            }

            DrawTestPointGizmo();
        }

        private void DrawTestPointGizmo()
        {
            Vector3 position = transform.position;
            Gizmos.color = required
                ? NavigationHighlightPalette.TestPointRequired
                : NavigationHighlightPalette.TestPointOptional;
            Gizmos.DrawWireSphere(position, 0.35f);
            Gizmos.DrawLine(position, position + Vector3.up * 0.9f);
        }
    }
}
