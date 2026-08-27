using System.Collections.Generic;
using UnityEngine;

namespace CustomNavigation.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DataSakura/Custom Navigation/Navigation Level")]
    public sealed class NavigationLevel : MonoBehaviour
    {
        [SerializeField, Tooltip("Stable level id used by artifacts, the API and the server load path.")]
        private string levelId = "new_level";
        [SerializeField, TextArea(2, 5), Tooltip("Short description of the level purpose for the team and the scene catalog.")]
        private string description;
        [SerializeField, Tooltip("Root the editor searches for explicitly tagged navigation sources.")]
        private Transform geometryRoot;
        [SerializeField, Tooltip("Recast bake settings for this level.")]
        private NavigationBuildSettings buildSettings = new NavigationBuildSettings();
        [SerializeField, Tooltip("Agent profile the current navmesh artifact is built for.")]
        private NavigationAgentProfile defaultAgentProfile;
        [SerializeField, Tooltip("Catalog of navigation areas, flags and base costs.")]
        private NavigationAreaCatalog areaCatalog;
        [SerializeField, Tooltip("Local scheduler CPU, iteration, admission, backlog, and result limits.")]
        private NavigationPerformanceProfile performanceProfile;
        public string LevelId => levelId;
        public string Description => description;
        public Transform GeometryRoot => geometryRoot != null ? geometryRoot : transform;
        public NavigationBuildSettings BuildSettings => buildSettings;
        public NavigationAgentProfile DefaultAgentProfile => defaultAgentProfile;
        public NavigationAreaCatalog AreaCatalog => areaCatalog;
        public NavigationPerformanceProfile PerformanceProfile => performanceProfile;

        private List<NavigationGeometrySource> gizmoSources;
        private List<MeshFilter> gizmoMeshFilters;

        public void ConfigureDefaults(
            NavigationAgentProfile agentProfile,
            NavigationAreaCatalog catalog,
            NavigationPerformanceProfile mobilePerformance)
        {
            defaultAgentProfile = agentProfile;
            areaCatalog = catalog;
            performanceProfile = mobilePerformance;
            SynchronizeBuildSettingsWithAgent();
        }

        private void Reset()
        {
            geometryRoot = transform;
        }

        private void OnValidate()
        {
            levelId = NavigationIdUtility.Sanitize(levelId, "new_level");
            description = string.IsNullOrWhiteSpace(description) ? string.Empty : description.Trim();
            SynchronizeBuildSettingsWithAgent();
        }

        /// <summary>
        /// Keeps programmatic setup and Unity's serialized validation on the same bake settings.
        /// Without this, the first bake after ConfigureDefaults can use constructor defaults while
        /// later bakes use the agent-driven preset applied by OnValidate.
        /// </summary>
        private void SynchronizeBuildSettingsWithAgent()
        {
            buildSettings ??= new NavigationBuildSettings();
            if (defaultAgentProfile != null)
            {
                buildSettings.ApplyQualityPreset(
                    defaultAgentProfile.Radius,
                    defaultAgentProfile.MaximumClimb);
            }

            buildSettings.Validate();
        }

        /// <summary>
        /// Checks that the level is ready for the bake. Returns a human readable
        /// reason when something is missing.
        /// </summary>
        public bool IsReadyToBake(out string reason)
        {
            if (defaultAgentProfile == null)
            {
                reason = "Agent Profile is not assigned - set the agent size (height, radius, step).";
                return false;
            }

            if (areaCatalog == null)
            {
                reason = "Area Catalog is not assigned - it defines the surface types and their colors.";
                return false;
            }

            if (performanceProfile == null)
            {
                reason = "Performance Profile is not assigned - it defines the pathfinding CPU budget.";
                return false;
            }

            if (!TryGetGeometryBounds(out _))
            {
                reason = "There is no Geometry Source with an Include mesh under the Geometry Root.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>
        /// World bounds of every Include/Block geometry source of the level.
        /// Returns false when there is no usable mesh under the geometry root.
        /// </summary>
        public bool TryGetGeometryBounds(out Bounds bounds)
        {
            bounds = default;
            Transform root = GeometryRoot;
            if (root == null)
            {
                return false;
            }

            gizmoSources ??= new List<NavigationGeometrySource>();
            gizmoMeshFilters ??= new List<MeshFilter>();
            root.GetComponentsInChildren(true, gizmoSources);

            bool initialized = false;
            for (int i = 0; i < gizmoSources.Count; i++)
            {
                NavigationGeometrySource source = gizmoSources[i];
                if (source == null || source.Mode == NavigationGeometryMode.Ignore)
                {
                    continue;
                }

                source.CollectMeshFilters(gizmoMeshFilters);
                for (int meshIndex = 0; meshIndex < gizmoMeshFilters.Count; meshIndex++)
                {
                    MeshFilter meshFilter = gizmoMeshFilters[meshIndex];
                    Bounds localBounds = meshFilter.sharedMesh.bounds;
                    Bounds worldBounds = TransformBounds(
                        meshFilter.transform.localToWorldMatrix,
                        localBounds);
                    if (initialized)
                    {
                        bounds.Encapsulate(worldBounds);
                    }
                    else
                    {
                        bounds = worldBounds;
                        initialized = true;
                    }
                }
            }

            return initialized;
        }

        private static Bounds TransformBounds(Matrix4x4 matrix, Bounds localBounds)
        {
            Vector3 center = matrix.MultiplyPoint3x4(localBounds.center);
            Vector3 extents = localBounds.extents;
            Vector3 worldExtents = new Vector3(
                Mathf.Abs(matrix.m00) * extents.x
                + Mathf.Abs(matrix.m01) * extents.y
                + Mathf.Abs(matrix.m02) * extents.z,
                Mathf.Abs(matrix.m10) * extents.x
                + Mathf.Abs(matrix.m11) * extents.y
                + Mathf.Abs(matrix.m12) * extents.z,
                Mathf.Abs(matrix.m20) * extents.x
                + Mathf.Abs(matrix.m21) * extents.y
                + Mathf.Abs(matrix.m22) * extents.z);
            return new Bounds(center, worldExtents * 2f);
        }

        private void OnDrawGizmos()
        {
            if (!NavigationHighlightSettings.SourcesEnabled || !TryGetGeometryBounds(out Bounds bounds))
            {
                return;
            }

            Gizmos.color = NavigationHighlightPalette.LevelBounds;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }
}
