using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotRecast.Detour;
using DotRecast.Detour.Io;

namespace DotRecastServer.Navigation;
public static class NavigationArtifactStore
{
    public const string SupportedSchemaVersion = "1";
    public const string SupportedDotRecastVersion = "2026.1.3";
    public const string ActiveManifestFileName = "active.manifest.json";

    public static ServerNavigation Load(string manifestPath, JsonSerializerOptions jsonOptions)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                "Navigation manifest was not found. Export navigation from Unity first.",
                manifestPath);
        }

        NavigationArtifactManifest manifest = JsonSerializer.Deserialize<NavigationArtifactManifest>(
            File.ReadAllText(manifestPath),
            jsonOptions)
            ?? throw new InvalidDataException("Navigation manifest is empty.");

        string directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
                           ?? throw new InvalidOperationException("Cannot resolve manifest directory.");
        string dataPath = Path.Combine(directory, manifest.FileName);
        byte[] data = File.ReadAllBytes(dataPath);
        return Create(manifest, data, dataPath);
    }

    /// <summary>
    /// Validates a manifest/payload pair and builds the queryable navmesh. Shared by
    /// disk loading and by HTTP uploads so both go through exactly the same checks.
    /// </summary>
    public static ServerNavigation Create(
        NavigationArtifactManifest manifest,
        byte[] data,
        string sourceLabel)
    {
        if (!string.Equals(manifest.SchemaVersion, SupportedSchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported navigation schema '{manifest.SchemaVersion}'. " +
                $"Server supports '{SupportedSchemaVersion}'.");
        }

        if (!string.Equals(manifest.DotRecastVersion, SupportedDotRecastVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Navigation artifact uses DotRecast '{manifest.DotRecastVersion}', " +
                $"server uses '{SupportedDotRecastVersion}'.");
        }

        string actualHash = ComputeSha256(data);
        if (!string.Equals(actualHash, manifest.ArtifactHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Navigation artifact hash mismatch: manifest={manifest.ArtifactHash}, data={actualHash}.");
        }

        DtNavMesh navMesh;
        using (var reader = new BinaryReader(new MemoryStream(data, false)))
        {
            navMesh = new DtMeshSetReader().Read(reader);
        }

        int polygonCount = CountPolygons(navMesh);
        if (polygonCount <= 0 || polygonCount != manifest.PolygonCount)
        {
            throw new InvalidDataException(
                $"Navigation polygon count mismatch: manifest={manifest.PolygonCount}, data={polygonCount}.");
        }

        Console.WriteLine(
            $"[artifact] Loaded level={manifest.LevelId}, hash={actualHash}, " +
            $"polygons={polygonCount}, sourceMeshes={manifest.SourceMeshCount}, file={sourceLabel}");
        return new ServerNavigation(
            navMesh,
            manifest.LevelId,
            manifest.Description,
            actualHash,
            polygonCount);
    }

    /// <summary>
    /// Stores an uploaded artifact next to the ones exported through the file system.
    /// The payload is fully validated before anything is written, so a bad upload can
    /// never leave the server with a half-written map.
    /// </summary>
    public static ArtifactUploadResponse Save(
        string dataDirectory,
        ArtifactUploadRequest request,
        JsonSerializerOptions jsonOptions)
    {
        if (string.IsNullOrWhiteSpace(request.ManifestJson))
        {
            return Rejected("manifestJson is required.");
        }

        if (string.IsNullOrWhiteSpace(request.DataBase64))
        {
            return Rejected("dataBase64 is required.");
        }

        NavigationArtifactManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<NavigationArtifactManifest>(
                request.ManifestJson!,
                jsonOptions);
        }
        catch (JsonException exception)
        {
            return Rejected("Manifest is not valid JSON: " + exception.Message);
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.FileName))
        {
            return Rejected("Manifest is empty or carries no fileName.");
        }

        // The file name comes from the network and is used to build a path, so anything
        // that could escape the data folder is refused outright.
        string fileName = Path.GetFileName(manifest.FileName);
        if (!string.Equals(fileName, manifest.FileName, StringComparison.Ordinal)
            || fileName.Length == 0
            || !fileName.EndsWith(".navmesh.bytes", StringComparison.Ordinal))
        {
            return Rejected(
                $"Unexpected artifact file name '{manifest.FileName}'. " +
                "It must be a plain '<level>.<hash>.navmesh.bytes' name.");
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(request.DataBase64!);
        }
        catch (FormatException exception)
        {
            return Rejected("dataBase64 is not valid base64: " + exception.Message);
        }

        try
        {
            // Parses the navmesh and checks schema, DotRecast version, hash and polygon
            // count - the upload is rejected before a single byte reaches the disk.
            Create(manifest, data, $"upload:{fileName}");
        }
        catch (Exception exception)
        {
            return Rejected(exception.Message);
        }

        Directory.CreateDirectory(dataDirectory);
        string manifestFileName =
            Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName))
            + ".manifest.json";
        string manifestJson = request.ManifestJson!.TrimEnd() + "\n";

        File.WriteAllBytes(Path.Combine(dataDirectory, fileName), data);
        File.WriteAllText(
            Path.Combine(dataDirectory, manifestFileName),
            manifestJson,
            new UTF8Encoding(false));
        if (request.SetActive)
        {
            File.WriteAllText(
                Path.Combine(dataDirectory, ActiveManifestFileName),
                manifestJson,
                new UTF8Encoding(false));
        }

        Console.WriteLine(
            $"[upload] Stored level={manifest.LevelId}, hash={manifest.ArtifactHash}, " +
            $"file={fileName}, active={request.SetActive}");

        return new ArtifactUploadResponse(
            true,
            manifest.LevelId,
            manifest.ArtifactHash,
            fileName,
            request.SetActive,
            request.SetActive
                ? "Uploaded and marked active."
                : "Uploaded.");
    }

    private static ArtifactUploadResponse Rejected(string message)
    {
        Console.WriteLine($"[upload] Rejected: {message}");
        return new ArtifactUploadResponse(
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            false,
            message);
    }

    /// <summary>
    /// Manifest pinned with <c>--manifest</c>, or null to let the registry pick the
    /// active one. Pinning is for serving one specific map, e.g. a dedicated instance.
    /// </summary>
    public static string? ResolvePinnedManifestPath(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--manifest", StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                throw new ArgumentException("--manifest requires a path.");
            }

            return Path.GetFullPath(args[i + 1]);
        }

        return null;
    }

    /// <summary>Folder the server serves artifacts from: <c>--data</c>, else NavigationData.</summary>
    public static string ResolveDataDirectory(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--data", StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                throw new ArgumentException("--data requires a folder path.");
            }

            return Path.GetFullPath(args[i + 1]);
        }

        string? pinned = ResolvePinnedManifestPath(args);
        if (pinned is not null)
        {
            return Path.GetDirectoryName(pinned) ?? AppContext.BaseDirectory;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "NavigationData"));
    }

    /// <summary>
    /// Enumerates every manifest in the NavigationData folder. Used by the Unity editor
    /// to show the difference between client and server maps without access to the server file system.
    /// </summary>
    public static ArtifactsResponse ListArtifacts(
        string dataDirectory,
        ServerNavigation? loadedNavigation,
        JsonSerializerOptions jsonOptions)
    {
        var artifacts = new List<ServerArtifactDto>();
        string loadedLevelId = loadedNavigation?.LevelId ?? string.Empty;
        string loadedHash = loadedNavigation?.ArtifactHash ?? string.Empty;
        if (!Directory.Exists(dataDirectory))
        {
            return new ArtifactsResponse(
                loadedLevelId,
                loadedHash,
                dataDirectory,
                artifacts);
        }

        string activeHash = ReadManifestHashOrEmpty(
            Path.Combine(dataDirectory, ActiveManifestFileName),
            jsonOptions);

        string[] manifestPaths = Directory
            .GetFiles(dataDirectory, "*.manifest.json", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(
                Path.GetFileName(path),
                ActiveManifestFileName,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        foreach (string manifestPath in manifestPaths)
        {
            artifacts.Add(Describe(manifestPath, dataDirectory, activeHash, loadedHash, jsonOptions));
        }

        return new ArtifactsResponse(
            loadedLevelId,
            loadedHash,
            dataDirectory,
            artifacts);
    }

    private static ServerArtifactDto Describe(
        string manifestPath,
        string dataDirectory,
        string activeHash,
        string loadedHash,
        JsonSerializerOptions jsonOptions)
    {
        try
        {
            NavigationArtifactManifest? manifest = JsonSerializer.Deserialize<NavigationArtifactManifest>(
                File.ReadAllText(manifestPath),
                jsonOptions);
            if (manifest is null)
            {
                return Broken(manifestPath, "Manifest is empty.");
            }

            string dataPath = Path.Combine(dataDirectory, manifest.FileName);
            bool dataPresent = File.Exists(dataPath);
            bool hashMatches = dataPresent
                               && string.Equals(
                                   ComputeSha256(File.ReadAllBytes(dataPath)),
                                   manifest.ArtifactHash,
                                   StringComparison.OrdinalIgnoreCase);

            return new ServerArtifactDto(
                manifest.LevelId,
                manifest.Description,
                manifest.ArtifactHash,
                manifest.SchemaVersion,
                manifest.DotRecastVersion,
                manifest.AgentProfileId,
                manifest.PolygonCount,
                manifest.SourceMeshCount,
                manifest.FileName,
                dataPresent,
                hashMatches,
                string.Equals(manifest.ArtifactHash, activeHash, StringComparison.OrdinalIgnoreCase),
                string.Equals(manifest.ArtifactHash, loadedHash, StringComparison.OrdinalIgnoreCase),
                string.Empty);
        }
        catch (Exception exception)
        {
            return Broken(manifestPath, exception.Message);
        }
    }

    private static ServerArtifactDto Broken(string manifestPath, string error)
    {
        return new ServerArtifactDto(
            Path.GetFileNameWithoutExtension(manifestPath),
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            0,
            string.Empty,
            false,
            false,
            false,
            false,
            error);
    }

    private static string ReadManifestHashOrEmpty(string manifestPath, JsonSerializerOptions jsonOptions)
    {
        if (!File.Exists(manifestPath))
        {
            return string.Empty;
        }

        try
        {
            NavigationArtifactManifest? manifest = JsonSerializer.Deserialize<NavigationArtifactManifest>(
                File.ReadAllText(manifestPath),
                jsonOptions);
            return manifest?.ArtifactHash ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static int CountPolygons(DtNavMesh navMesh)
    {
        int count = 0;
        for (int tileIndex = 0; tileIndex < navMesh.GetMaxTiles(); tileIndex++)
        {
            DtMeshTile tile = navMesh.GetTile(tileIndex);
            if (tile?.data?.header is not null)
            {
                count += tile.data.header.polyCount;
            }
        }

        return count;
    }

    private static string ComputeSha256(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        var builder = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
        {
            builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}

public sealed class NavigationArtifactManifest
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string DotRecastVersion { get; init; } = string.Empty;
    public string LevelId { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ArtifactHash { get; init; } = string.Empty;
    public string AgentProfileId { get; init; } = string.Empty;
    public int PolygonCount { get; init; }
    public int SourceMeshCount { get; init; }
    public string FileName { get; init; } = string.Empty;
}
