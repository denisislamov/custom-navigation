using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace CustomNavigation.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Custom Navigation/Geometry Source")]
    public sealed class NavigationGeometrySource : MonoBehaviour
    {
        [SerializeField, Tooltip("Include adds the mesh to the bake, Block blocks its bounds, Ignore keeps it visual only.")]
        private NavigationGeometryMode mode = NavigationGeometryMode.Include;
        [SerializeField, FormerlySerializedAs("areaId"), Tooltip("Surface type assigned to this geometry.")]
        private NavigationArea area = NavigationArea.Ground;
        [SerializeField, Tooltip("Include MeshFilters of all children of this source.")]
        private bool includeChildren = true;
        [SerializeField, Tooltip("Take inactive children into account during the editor export.")]
        private bool includeInactiveChildren;

        public NavigationGeometryMode Mode => mode;
        public NavigationArea Area => area;
        public int AreaId => (int)area;
        public bool IncludeChildren => includeChildren;
        public bool IncludeInactiveChildren => includeInactiveChildren;

        private List<MeshFilter> gizmoMeshFilters;

        /// <summary>
        /// Returns the MeshFilters the exporter will collect from this source.
        /// </summary>
        public void CollectMeshFilters(List<MeshFilter> results)
        {
            if (results == null)
            {
                throw new System.ArgumentNullException(nameof(results));
            }

            results.Clear();
            if (includeChildren)
            {
                GetComponentsInChildren(includeInactiveChildren, results);
            }
            else if (TryGetComponent(out MeshFilter meshFilter))
            {
                results.Add(meshFilter);
            }

            for (int i = results.Count - 1; i >= 0; i--)
            {
                if (results[i] == null || results[i].sharedMesh == null)
                {
                    results.RemoveAt(i);
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (!NavigationHighlightSettings.Enabled)
            {
                return;
            }

            DrawGeometryGizmo();
        }

        private void OnDrawGizmosSelected()
        {
            if (NavigationHighlightSettings.Enabled)
            {
                return;
            }

            DrawGeometryGizmo();
        }

        private void DrawGeometryGizmo()
        {
            gizmoMeshFilters ??= new List<MeshFilter>();
            CollectMeshFilters(gizmoMeshFilters);
            if (gizmoMeshFilters.Count == 0)
            {
                return;
            }

            Gizmos.color = NavigationHighlightPalette.ForGeometryMode(mode);
            Matrix4x4 previousMatrix = Gizmos.matrix;
            for (int i = 0; i < gizmoMeshFilters.Count; i++)
            {
                MeshFilter meshFilter = gizmoMeshFilters[i];
                Bounds bounds = meshFilter.sharedMesh.bounds;
                Gizmos.matrix = meshFilter.transform.localToWorldMatrix;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }

            Gizmos.matrix = previousMatrix;
        }
    }
}
