using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DotRecast.Core.Numerics;
using DotRecast.Detour;

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

    public PathResponse FindPath(PathRequest request)
    {
        Vector3Dto start = request.Start
            ?? throw new ArgumentException("Path start is required.", nameof(request));
        Vector3Dto destination = request.Destination
            ?? throw new ArgumentException("Path destination is required.", nameof(request));
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
            var startPoint = ToRc(start);
            var endPoint = ToRc(destination);

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

            var points = new Vector3Dto[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                RcVec3f point = straightPath[i].pos;
                points[i] = new Vector3Dto(point.X, point.Y, point.Z);
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
            return new PathResponse(
                true,
                points,
                message,
                requestId,
                ArtifactHash,
                fingerprint,
                artifactMismatch || pathMismatch);
        }
    }

    private PathResponse Failure(string requestId, string message)
    {
        return new PathResponse(
            false,
            Array.Empty<Vector3Dto>(),
            message,
            requestId,
            ArtifactHash,
            string.Empty,
            false);
    }

    private static bool IsFinite(Vector3Dto value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    }

    private static RcVec3f ToRc(Vector3Dto value)
    {
        return new RcVec3f(value.X, value.Y, value.Z);
    }

    private static string FormatPoint(Vector3Dto point)
    {
        return FormattableString.Invariant($"({point.X:F3}, {point.Y:F3}, {point.Z:F3})");
    }
}

public static class NavigationPathFingerprint
{
    public static string Compute(IReadOnlyList<Vector3Dto> points)
    {
        var canonical = new StringBuilder(points.Count * 32);
        for (int i = 0; i < points.Count; i++)
        {
            Vector3Dto point = points[i];
            canonical.Append(Quantize(point.X).ToString(CultureInfo.InvariantCulture));
            canonical.Append(',');
            canonical.Append(Quantize(point.Y).ToString(CultureInfo.InvariantCulture));
            canonical.Append(',');
            canonical.Append(Quantize(point.Z).ToString(CultureInfo.InvariantCulture));
            canonical.Append(';');
        }

        byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static long Quantize(float value)
    {
        return (long)Math.Round(value * 1000d, MidpointRounding.AwayFromZero);
    }
}
