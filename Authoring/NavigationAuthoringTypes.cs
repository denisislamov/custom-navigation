using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace CustomNavigation.Authoring
{
    public enum NavigationGeometryMode
    {
        Include,
        Block,
        Ignore
    }

    /// <summary>
    /// Navigation surface type. Replaces the magic area id in the 0-63 range.
    /// The numeric values match the DotRecast area ids, so older scenes
    /// that stored an int keep loading without any data loss.
    /// </summary>
    public enum NavigationArea
    {
        [InspectorName("Not Walkable (blocked)")]
        NotWalkable = 0,
        [InspectorName("Ground (regular floor)")]
        Ground = 1,
        [InspectorName("Stairs")]
        Stairs = 2,
        [InspectorName("Danger (hazard zone)")]
        Danger = 3,
        [InspectorName("Crouch (crouched passage)")]
        Crouch = 4,
        [InspectorName("Water")]
        Water = 5,
        [InspectorName("Road (faster travel)")]
        Road = 6,
        [InspectorName("Grass")]
        Grass = 7,
        [InspectorName("Mud (slow going)")]
        Mud = 8,
        [InspectorName("Ice")]
        Ice = 9,
        [InspectorName("Custom/Custom 10")] Custom10 = 10,
        [InspectorName("Custom/Custom 11")] Custom11 = 11,
        [InspectorName("Custom/Custom 12")] Custom12 = 12,
        [InspectorName("Custom/Custom 13")] Custom13 = 13,
        [InspectorName("Custom/Custom 14")] Custom14 = 14,
        [InspectorName("Custom/Custom 15")] Custom15 = 15
    }

    /// <summary>
    /// Agent movement capabilities. Replaces the opaque bit mask of
    /// polygon flags: Unity draws a [Flags] enum as a convenient multi-select.
    /// </summary>
    [Flags]
    public enum NavigationFlags
    {
        None = 0,
        [InspectorName("Walk (regular walking)")]
        Walk = 1 << 0,
        [InspectorName("Crouch (squeeze through crouched)")]
        Crouch = 1 << 1,
        [InspectorName("Swim")]
        Swim = 1 << 2,
        [InspectorName("Jump (leap across a gap)")]
        Jump = 1 << 3,
        [InspectorName("Door (passage through a door)")]
        Door = 1 << 4,
        [InspectorName("Ladder (ladder or rope)")]
        Ladder = 1 << 5,
        [InspectorName("Disabled (temporarily closed)")]
        Disabled = 1 << 6
    }

    /// <summary>
    /// Bake quality preset. Replaces manual tuning of ten raw Recast parameters.
    /// Custom unlocks every parameter for fine tuning.
    /// </summary>
    public enum NavigationBakeQuality
    {
        [InspectorName("Fast (quick bake, coarse navmesh)")]
        Fast,
        [InspectorName("Balanced (recommended)")]
        Balanced,
        [InspectorName("High Detail (precise, slow bake)")]
        HighDetail,
        [InspectorName("Custom (manual tuning)")]
        Custom
    }

    public static class NavigationFlagsUtility
    {
        public const int AllMask = 0xffff;

        public static int ToMask(NavigationFlags flags)
        {
            return (int)flags & AllMask;
        }

        public static NavigationFlags FromMask(int mask)
        {
            return (NavigationFlags)(mask & AllMask);
        }
    }

    public enum NavigationLinkType
    {
        Jump,
        Drop,
        Ladder,
        Vault,
        Teleport,
        Scripted
    }

    public enum NavigationPortalType
    {
        Door,
        Gate,
        DestructiblePassage,
        Bridge,
        Elevator,
        Scripted
    }

    public enum NavigationTestPointType
    {
        Generic,
        TeamSpawn,
        Objective,
        BombSite,
        Extraction,
        Patrol,
        SniperPosition
    }

    public enum NavigationDeviceTier
    {
        MobileLow,
        MobileMedium,
        MobileHigh,
        Custom
    }

    public enum NavigationQueryPriority
    {
        CriticalCorrection,
        PlayerImmediate,
        CombatBot,
        VisibleBot,
        BackgroundBot,
        Prewarm
    }

    [Serializable]
    public sealed class NavigationBuildSettings
    {
        [SerializeField, Tooltip("Bake quality preset. Drives every parameter below automatically. Pick Custom to tune them by hand.")]
        private NavigationBakeQuality quality = NavigationBakeQuality.Balanced;
        [SerializeField, Tooltip("Derive the voxel size from the agent radius (radius / 3), the way Unity NavMesh does.")]
        private bool autoCellSize = true;

        [SerializeField, Min(0.01f), Tooltip("Voxel cell size along X/Z. Smaller means more precision but a slower bake and a bigger artifact.")]
        private float cellSize = 0.25f;
        [SerializeField, Min(0.01f), Tooltip("Voxel cell height. Smaller means more precise steps and ramps but a slower build.")]
        private float cellHeight = 0.15f;
        [SerializeField, Range(16, 1024), Tooltip("NOT USED. Reserved for the future tiled production pipeline.")]
        private int tileSizeInCells = 128;
        [SerializeField, Range(3, 6), Tooltip("Maximum number of vertices per Detour polygon. In practice always 6.")]
        private int maximumVerticesPerPolygon = 6;
        [SerializeField, Min(0f), Tooltip("Minimum area of a standalone region; smaller islands are removed.")]
        private float minimumRegionArea = 3f;
        [SerializeField, Min(0f), Tooltip("Regions smaller than this value may be merged into their neighbours.")]
        private float mergedRegionArea = 8f;
        [SerializeField, Min(0f), Tooltip("Maximum contour edge length before tessellation.")]
        private float maximumEdgeLength = 12f;
        [SerializeField, Min(0.01f), Tooltip("Allowed contour simplification error. Smaller means more polygons.")]
        private float maximumEdgeError = 1.3f;
        [SerializeField, Min(0f), Tooltip("Detail mesh sampling interval relative to the cell size.")]
        private float detailSampleDistance = 6f;
        [SerializeField, Min(0f), Tooltip("Maximum detail mesh vertical error relative to the cell height.")]
        private float detailSampleMaximumError = 1f;

        public NavigationBakeQuality Quality => quality;
        public bool AutoCellSize => autoCellSize;
        public float CellSize => cellSize;
        public float CellHeight => cellHeight;
        public int TileSizeInCells => tileSizeInCells;
        public int MaximumVerticesPerPolygon => maximumVerticesPerPolygon;
        public float MinimumRegionArea => minimumRegionArea;
        public float MergedRegionArea => mergedRegionArea;
        public float MaximumEdgeLength => maximumEdgeLength;
        public float MaximumEdgeError => maximumEdgeError;
        public float DetailSampleDistance => detailSampleDistance;
        public float DetailSampleMaximumError => detailSampleMaximumError;

        /// <summary>
        /// Applies the quality preset and, when autoCellSize is on, derives the
        /// voxel size from the agent radius. Does nothing when quality == Custom.
        /// </summary>
        public void ApplyQualityPreset(float agentRadius, float agentClimb)
        {
            switch (quality)
            {
                case NavigationBakeQuality.Fast:
                    maximumEdgeError = 2f;
                    maximumEdgeLength = 16f;
                    minimumRegionArea = 6f;
                    mergedRegionArea = 16f;
                    detailSampleDistance = 0f;
                    detailSampleMaximumError = 0f;
                    break;

                case NavigationBakeQuality.HighDetail:
                    maximumEdgeError = 1f;
                    maximumEdgeLength = 8f;
                    minimumRegionArea = 1f;
                    mergedRegionArea = 4f;
                    detailSampleDistance = 6f;
                    detailSampleMaximumError = 0.5f;
                    break;

                case NavigationBakeQuality.Custom:
                    return;

                default:
                    maximumEdgeError = 1.3f;
                    maximumEdgeLength = 12f;
                    minimumRegionArea = 3f;
                    mergedRegionArea = 8f;
                    detailSampleDistance = 6f;
                    detailSampleMaximumError = 1f;
                    break;
            }

            maximumVerticesPerPolygon = 6;

            if (autoCellSize && agentRadius > 0f)
            {
                float divisor = quality switch
                {
                    NavigationBakeQuality.Fast => 2f,
                    NavigationBakeQuality.HighDetail => 4f,
                    _ => 3f
                };

                cellSize = Mathf.Max(0.01f, agentRadius / divisor);
                // Half of the climb step gives enough vertical precision.
                cellHeight = Mathf.Max(0.01f, agentClimb > 0f ? agentClimb * 0.5f : cellSize * 0.6f);
            }
        }

        internal void Validate()
        {
            cellSize = Mathf.Max(0.01f, cellSize);
            cellHeight = Mathf.Max(0.01f, cellHeight);
            tileSizeInCells = Mathf.Clamp(tileSizeInCells, 16, 1024);
            maximumVerticesPerPolygon = Mathf.Clamp(maximumVerticesPerPolygon, 3, 6);
            minimumRegionArea = Mathf.Max(0f, minimumRegionArea);
            mergedRegionArea = Mathf.Max(0f, mergedRegionArea);
            maximumEdgeLength = Mathf.Max(0f, maximumEdgeLength);
            maximumEdgeError = Mathf.Max(0.01f, maximumEdgeError);
            detailSampleDistance = Mathf.Max(0f, detailSampleDistance);
            detailSampleMaximumError = Mathf.Max(0f, detailSampleMaximumError);
        }
    }

    [Serializable]
    public sealed class NavigationAreaCost
    {
        [SerializeField, FormerlySerializedAs("areaId"), Tooltip("Surface type whose cost is overridden for this agent.")]
        private NavigationArea area = NavigationArea.Ground;
        [SerializeField, Min(1f), Tooltip("Path cost multiplier. 1 is a regular surface, higher means less preferred.")]
        private float cost = 1f;

        public NavigationArea Area => area;
        public int AreaId => (int)area;
        public float Cost => cost;

        internal void Validate()
        {
            cost = Mathf.Max(1f, cost);
        }
    }

    public static class NavigationIdUtility
    {
        public static string Sanitize(string value, string fallback)
        {
            string source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var characters = new List<char>(source.Length);
            bool previousWasSeparator = false;

            for (int i = 0; i < source.Length; i++)
            {
                char current = char.ToLowerInvariant(source[i]);
                bool valid = char.IsLetterOrDigit(current) || current == '_';
                if (valid)
                {
                    characters.Add(current);
                    previousWasSeparator = false;
                }
                else if (!previousWasSeparator && characters.Count > 0)
                {
                    characters.Add('_');
                    previousWasSeparator = true;
                }
            }

            while (characters.Count > 0 && characters[characters.Count - 1] == '_')
            {
                characters.RemoveAt(characters.Count - 1);
            }

            return characters.Count == 0 ? fallback : new string(characters.ToArray());
        }

        public static string CreateStableId(string prefix)
        {
            return $"{prefix}_{Guid.NewGuid():N}";
        }
    }
}
