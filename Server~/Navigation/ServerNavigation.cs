using CustomNavigation.Runtime;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Jitter2.LinearMath;

namespace DotRecastServer.Navigation;

public sealed class ServerNavigation
{
    private const int MaxPathPolygons = 256;
    private const int MaxStraightPathPoints = 256;

    private readonly DtNavMeshQuery _query;
    private readonly IDtQueryFilter _filter = new DtQueryDefaultFilter();
    private readonly object _queryLock = new();

    public string LevelId { get; }
    public string Description { get; }
    public string ArtifactHash { get; }
    public int PolygonCount { get; }

    public ServerNavigation(
        DtNavMesh navMesh,
        string levelId,
        string description,
        string artifactHash,
        int polygonCount)
    {
        _query = new DtNavMeshQuery(navMesh);
        LevelId = levelId;
        Description = description;
        ArtifactHash = artifactHash;
        PolygonCount = polygonCount;
    }

    public NavigationPathResponse FindPath(NavigationPathRequest request)
    {
        JVector start = request.Start;
        JVector destination = request.Destination;
        string requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? "server-generated"
            : request.RequestId;

        if (!IsFinite(start) || !IsFinite(destination))
        {
            return Failure(requestId, "Coordinates must be finite numbers.");
        }

        lock (_queryLock)
        {
            var searchExtents = new RcVec3f(2f, 4f, 2f);
            var startPoint = NavigationDotRecastAdapter.ToDotRecast(in start);
            var endPoint = NavigationDotRecastAdapter.ToDotRecast(in destination);

            DtStatus startStatus = _query.FindNearestPoly(
                startPoint,
                searchExtents,
                _filter,
                out long startRef,
                out RcVec3f nearestStart,
                out _);
            DtStatus endStatus = _query.FindNearestPoly(
                endPoint,
                searchExtents,
                _filter,
                out long endRef,
                out RcVec3f nearestEnd,
                out _);

            if (startStatus.Failed() || endStatus.Failed() || startRef == 0 || endRef == 0)
            {
                return Failure(requestId, "Start or destination is outside the navigation mesh.");
            }

            var polygonPath = new long[MaxPathPolygons];
            DtStatus pathStatus = _query.FindPath(
                startRef,
                endRef,
                nearestStart,
                nearestEnd,
                _filter,
                polygonPath.AsSpan(),
                out int polygonCount,
                polygonPath.Length);
            if (pathStatus.Failed() || polygonCount == 0)
            {
                return Failure(requestId, "DotRecast could not find a polygon corridor.");
            }

            var straightPath = new DtStraightPath[MaxStraightPathPoints];
            DtStatus straightStatus = _query.FindStraightPath(
                nearestStart,
                nearestEnd,
                polygonPath.AsSpan(),
                polygonCount,
                straightPath.AsSpan(),
                out int pointCount,
                straightPath.Length,
                DtStraightPathOptions.DT_STRAIGHTPATH_ALL_CROSSINGS);
            if (straightStatus.Failed() || pointCount == 0)
            {
                return Failure(requestId, "DotRecast could not create a straight path.");
            }

            var points = new JVector[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                RcVec3f point = straightPath[i].pos;
                points[i] = NavigationDotRecastAdapter.FromDotRecast(in point);
            }

            string fingerprint = NavigationPathFingerprint.Compute(points);
            bool artifactMismatch = !string.IsNullOrWhiteSpace(request.ClientArtifactHash)
                                    && !string.Equals(
                                        request.ClientArtifactHash,
                                        ArtifactHash,
                                        StringComparison.OrdinalIgnoreCase);
            bool pathMismatch = !string.IsNullOrWhiteSpace(request.ClientPathFingerprint)
                                && !string.Equals(
                                    request.ClientPathFingerprint,
                                    fingerprint,
                                    StringComparison.OrdinalIgnoreCase);

            if (artifactMismatch)
            {
                Console.Error.WriteLine(
                    $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [WARNING] [path {requestId}] " +
                    $"navigation artifact mismatch: client={request.ClientArtifactHash}, " +
                    $"server={ArtifactHash}, level={LevelId}");
            }

            if (pathMismatch)
            {
                Console.Error.WriteLine(
                    $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [WARNING] [path {requestId}] " +
                    $"local/server route mismatch: client={request.ClientPathFingerprint}, " +
                    $"server={fingerprint}, start={FormatPoint(start)}, " +
                    $"destination={FormatPoint(destination)}");
            }

            string message = pathStatus.IsPartial()
                ? "DotRecast returned a partial path."
                : $"DotRecast returned {pointCount} straight path points.";
            return new NavigationPathResponse
            {
                Success = true,
                Points = points,
                Message = message,
                RequestId = requestId,
                ArtifactHash = ArtifactHash,
                PathFingerprint = fingerprint,
                ServerMismatchDetected = artifactMismatch || pathMismatch
            };
        }
    }

    private NavigationPathResponse Failure(string requestId, string message)
    {
        return new NavigationPathResponse
        {
            Success = false,
            Points = Array.Empty<JVector>(),
            Message = message,
            RequestId = requestId,
            ArtifactHash = ArtifactHash
        };
    }

    private static bool IsFinite(JVector value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }

    private static string FormatPoint(JVector point)
    {
        return FormattableString.Invariant($"({point.X:F3}, {point.Y:F3}, {point.Z:F3})");
    }
}
