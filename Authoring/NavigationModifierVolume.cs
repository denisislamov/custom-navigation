using UnityEngine;
using UnityEngine.Serialization;

namespace CustomNavigation.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Custom Navigation/Modifier Volume")]
    public sealed class NavigationModifierVolume : MonoBehaviour
    {
        [SerializeField, Tooltip("Block makes the volume impassable; Include overrides the surface type inside the volume.")]
        private NavigationGeometryMode mode = NavigationGeometryMode.Block;
        [SerializeField, FormerlySerializedAs("areaId"), Tooltip("Surface type for the Include mode. Block always uses Not Walkable.")]
        private NavigationArea area = NavigationArea.NotWalkable;
        [SerializeField, Tooltip("Local offset of the volume center relative to the Transform.")]
        private Vector3 center;
        [SerializeField, Tooltip("Local volume size along X/Y/Z in meters.")]
        private Vector3 size = Vector3.one;

        public NavigationGeometryMode Mode => mode;
        public NavigationArea Area => area;
        public int AreaId => (int)area;
        public Vector3 Center => center;
        public Vector3 Size => size;
        public Matrix4x4 LocalToWorldMatrix => transform.localToWorldMatrix;

        private void OnValidate()
        {
            size.x = Mathf.Max(0.01f, size.x);
            size.y = Mathf.Max(0.01f, size.y);
            size.z = Mathf.Max(0.01f, size.z);
        }

        private void OnDrawGizmos()
        {
            if (!NavigationHighlightSettings.Enabled)
            {
                return;
            }

            DrawVolumeGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (NavigationHighlightSettings.Enabled)
            {
                return;
            }

            DrawVolumeGizmo();
        }

        private void DrawVolumeGizmo()
        {
            Color color = NavigationHighlightPalette.ForGeometryMode(mode);
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(color.r, color.g, color.b, 0.12f);
            Gizmos.DrawCube(center, size);
            Gizmos.color = color;
            Gizmos.DrawWireCube(center, size);
            Gizmos.matrix = previousMatrix;
        }
    }
}
