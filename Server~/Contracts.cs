namespace DotRecastServer;

public sealed record Vector3Dto(float X, float Y, float Z);

public sealed class PathRequest
{
    public string? RequestId { get; init; }

    /// <summary>
    /// Which map to path on. Empty means "the active one", which keeps every
    /// single-level project and every older client working unchanged.
    /// </summary>
    public string? LevelId { get; init; }

    public Vector3Dto? Start { get; init; }

    public Vector3Dto? Destination { get; init; }

    public string? ClientArtifactHash { get; init; }

    public string? ClientPathFingerprint { get; init; }
}

public sealed record PathResponse(
    bool Success,
    IReadOnlyList<Vector3Dto> Points,
    string Message,
    string RequestId,
    string ArtifactHash,
    string PathFingerprint,
    bool ServerMismatchDetected);

public sealed record HealthResponse(
    string Status,
    string DotRecastVersion,
    int NavigationPolygons,
    string LevelId,
    string Description,
    string ArtifactHash,
    string Message = "",
    string DataDirectory = "",
    IReadOnlyList<string>? AvailableLevels = null);

/// <summary>A single navmesh stored in the server NavigationData folder.</summary>
public sealed record ServerArtifactDto(
    string LevelId,
    string Description,
    string ArtifactHash,
    string SchemaVersion,
    string DotRecastVersion,
    string AgentProfileId,
    int PolygonCount,
    int SourceMeshCount,
    string FileName,
    bool DataPresent,
    bool HashMatchesData,
    bool IsActive,
    bool IsLoaded,
    string Error);

/// <summary>Response of GET /artifacts: what the server can actually serve to clients.</summary>
public sealed record ArtifactsResponse(
    string LoadedLevelId,
    string LoadedArtifactHash,
    string DataDirectory,
    IReadOnlyList<ServerArtifactDto> Artifacts);

/// <summary>
/// Body of <c>POST /artifacts</c>: one baked navmesh pushed from the Unity editor.
/// Uploading over HTTP is the only way to reach a server that does not share a file
/// system with the machine running Unity.
/// </summary>
public sealed class ArtifactUploadRequest
{
    /// <summary>The manifest exactly as Unity wrote it, so hashes stay byte-identical.</summary>
    public string? ManifestJson { get; init; }

    /// <summary>Base64 of the .navigation.bytes payload (legacy .navmesh.bytes is accepted).</summary>
    public string? DataBase64 { get; init; }

    /// <summary>Make this the map served when a request carries no levelId.</summary>
    public bool SetActive { get; init; } = true;
}

public sealed record ArtifactUploadResponse(
    bool Success,
    string LevelId,
    string ArtifactHash,
    string FileName,
    bool SetActive,
    string Message);
