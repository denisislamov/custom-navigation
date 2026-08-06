using System;
using System.Collections.Generic;
using System.Diagnostics;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using UnityEngine;

namespace CustomNavigation.Editor
{
    /// <summary>Why no route was found. Replaces the featureless "No route found".</summary>
    internal enum NavigationProbeFailure
    {
        None,
        NoArtifact,
        NoAgentProfile,
        StartOffNavmesh,
        DestinationOffNavmesh,
        Unreachable,
        NoCorridor,
        NoStraightPath,
        ArtifactBroken
    }

    internal sealed class NavigationProbeResult
    {
        public bool Success;
        public Vector3[] Points = Array.Empty<Vector3>();
        public int CorridorPolygonCount;
        public float Length;
        public bool Partial;
        public double ElapsedMilliseconds;
        public NavigationProbeFailure Failure = NavigationProbeFailure.None;
        public string Message = string.Empty;
        public string Hint = string.Empty;
        public readonly List<NavigationArea> Areas = new List<NavigationArea>();

        public bool HasProjectedStart;
        public Vector3 ProjectedStart;
        public bool HasProjectedDestination;
        public Vector3 ProjectedDestination;

        public string DescribeFailure()
        {
            return Failure switch
            {
                NavigationProbeFailure.NoArtifact =>
                    "The navigation artifact is not built. Press Build for Client.",
                NavigationProbeFailure.NoAgentProfile =>
                    "The level has no Agent Profile assigned - the query filter cannot be built without it.",
                NavigationProbeFailure.StartOffNavmesh =>
                    "The Start point is off the navmesh.",
                NavigationProbeFailure.DestinationOffNavmesh =>
                    "The Destination point is off the navmesh.",
                NavigationProbeFailure.Unreachable =>
                    "Both points are on the navmesh but they are not connected.",
                NavigationProbeFailure.NoCorridor =>
                    "DotRecast returned no polygon corridor.",
                NavigationProbeFailure.NoStraightPath =>
                    "A corridor was found but the straight path is empty.",
                NavigationProbeFailure.ArtifactBroken =>
                    "The artifact cannot be read: " + Message,
                _ => Message
            };
        }
    }

    /// <summary>
    /// One-shot path query over the local artifact for the Scene View Path Probe.
    /// Runs on button press only: nothing subscribes to editor events.
    /// </summary>
    internal static class NavigationPathProbe
    {
        private const int MaximumCorridorPolygons = 512;
        private const int MaximumStraightPathPoints = 512;

        private static NavigationArtifactInstance cachedInstance;
        private static string cachedKey = string.Empty;

        /// <summary>Drops the loaded navmesh. Called after the artifact is rebuilt.</summary>
        public static void InvalidateCache()
        {
            cachedInstance = null;
            cachedKey = string.Empty;
        }

        public static NavigationArtifactInstance TryGetArtifact(
            NavigationArtifactAsset asset,
            out string error)
        {
            error = string.Empty;
            if (asset == null)
            {
                error = "Artifact not found.";
                return null;
            }

            string key = asset.ArtifactHash + "|" + asset.PolygonCount;
            if (cachedInstance != null && string.Equals(cachedKey, key, StringComparison.Ordinal))
            {
                return cachedInstance;
            }

            try
            {
                cachedInstance = NavigationArtifactLoader.Load(asset);
                cachedKey = key;
                return cachedInstance;
            }
            catch (Exception exception)
            {
                cachedInstance = null;
                cachedKey = string.Empty;
                error = exception.Message;
                return null;
            }
        }

        public static NavigationProbeResult FindPath(
            NavigationArtifactAsset asset,
            NavigationAgentProfile agent,
            Vector3 start,
            Vector3 destination)
        {
            var result = new NavigationProbeResult();
            if (agent == null)
            {
                result.Failure = NavigationProbeFailure.NoAgentProfile;
                return result;
            }

            NavigationArtifactInstance instance = TryGetArtifact(asset, out string error);
            if (instance == null)
            {
                result.Failure = string.IsNullOrEmpty(error)
                    ? NavigationProbeFailure.NoArtifact
                    : NavigationProbeFailure.ArtifactBroken;
                result.Message = error;
                return result;
            }

            var stopwatch = Stopwatch.StartNew();
            DtNavMeshQuery query = instance.CreateQuery();
            IDtQueryFilter filter = CreateFilter(agent);
            var extents = new RcVec3f(
                Mathf.Max(1f, agent.Radius * 4f),
                Mathf.Max(2f, agent.Height * 2f),
                Mathf.Max(1f, agent.Radius * 4f));

            DtStatus startStatus = query.FindNearestPoly(
                ToRc(start),
                extents,
                filter,
                out long startRef,
                out RcVec3f nearestStart,
                out _);
            if (startStatus.Failed() || startRef == 0)
            {
                result.Failure = NavigationProbeFailure.StartOffNavmesh;
                result.Hint = "Move the point closer to the floor, or check that the floor is marked as Include.";
                result.ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                return result;
            }

            result.HasProjectedStart = true;
            result.ProjectedStart = ToUnity(nearestStart);

            DtStatus endStatus = query.FindNearestPoly(
                ToRc(destination),
                extents,
                filter,
                out long endRef,
                out RcVec3f nearestEnd,
                out _);
            if (endStatus.Failed() || endRef == 0)
            {
                result.Failure = NavigationProbeFailure.DestinationOffNavmesh;
                result.Hint = "Move the point closer to the floor, or check that the floor is marked as Include.";
                result.ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                return result;
            }

            result.HasProjectedDestination = true;
            result.ProjectedDestination = ToUnity(nearestEnd);

            var corridor = new long[MaximumCorridorPolygons];
            DtStatus pathStatus = query.FindPath(
                startRef,
                endRef,
                nearestStart,
                nearestEnd,
                filter,
                corridor.AsSpan(),
                out int corridorCount,
                corridor.Length);
            if (pathStatus.Failed() || corridorCount == 0)
            {
                result.Failure = NavigationProbeFailure.NoCorridor;
                result.Hint = "The navmesh regions are most likely disconnected - a NavigationLink or a ramp is needed.";
                result.ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                return result;
            }

            result.Partial = pathStatus.IsPartial();
            result.CorridorPolygonCount = corridorCount;

            var straightPath = new DtStraightPath[MaximumStraightPathPoints];
            DtStatus straightStatus = query.FindStraightPath(
                nearestStart,
                nearestEnd,
                corridor.AsSpan(),
                corridorCount,
                straightPath.AsSpan(),
                out int pointCount,
                straightPath.Length,
                DtStraightPathOptions.DT_STRAIGHTPATH_ALL_CROSSINGS);
            if (straightStatus.Failed() || pointCount == 0)
            {
                result.Failure = NavigationProbeFailure.NoStraightPath;
                result.ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
                return result;
            }

            var points = new Vector3[pointCount];
            float length = 0f;
            for (int i = 0; i < pointCount; i++)
            {
                points[i] = ToUnity(straightPath[i].pos);
                if (i > 0)
                {
                    length += Vector3.Distance(points[i - 1], points[i]);
                }
            }

            CollectAreas(instance.NavMesh, corridor, corridorCount, result.Areas);

            result.Success = true;
            result.Points = points;
            result.Length = length;
            result.ElapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            result.Message = result.Partial
                ? "Partial path found: the destination is not fully reachable."
                : "Route found.";
            if (result.Partial)
            {
                result.Failure = NavigationProbeFailure.Unreachable;
                result.Hint = "Check connectivity: a link, a ramp, or a passage wider than 2 x Radius is needed.";
            }

            return result;
        }

        private static void CollectAreas(
            DtNavMesh navMesh,
            long[] corridor,
            int corridorCount,
            List<NavigationArea> areas)
        {
            var seen = new HashSet<int>();
            for (int i = 0; i < corridorCount; i++)
            {
                DtStatus status = navMesh.GetTileAndPolyByRef(corridor[i], out _, out DtPoly poly);
                if (status.Failed() || poly == null)
                {
                    continue;
                }

                int area = poly.GetArea();
                if (seen.Add(area))
                {
                    areas.Add((NavigationArea)area);
                }
            }
        }

        internal static IDtQueryFilter CreateFilter(NavigationAgentProfile agent)
        {
            var costs = new float[64];
            for (int areaId = 0; areaId < costs.Length; areaId++)
            {
                costs[areaId] = agent.GetAreaCost(areaId);
            }

            return new DtQueryDefaultFilter(
                agent.IncludedPolygonFlags,
                agent.ExcludedPolygonFlags,
                costs);
        }

        internal static RcVec3f ToRc(Vector3 value)
        {
            return new RcVec3f(value.x, value.y, value.z);
        }

        internal static Vector3 ToUnity(RcVec3f value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }
    }
}

