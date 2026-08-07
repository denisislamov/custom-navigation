using System.Text.Json;

namespace DotRecastServer.Navigation;

/// <summary>
/// Owns every navmesh the server can serve.
///
/// The server used to load exactly one artifact at startup and crash when it was
/// missing, which made the very first run impossible: you cannot export from Unity
/// before the server exists, and you could not start the server before exporting.
/// The registry resolves artifacts lazily instead, per request, so the server can
/// boot on an empty NavigationData folder and start answering as soon as Unity
/// exports something - no restart required.
/// </summary>
public sealed class NavigationRegistry
{
    private readonly string dataDirectory;
    private readonly string? pinnedManifestPath;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly object gate = new();

    private readonly Dictionary<string, CacheEntry> cache =
        new(StringComparer.OrdinalIgnoreCase);

    public NavigationRegistry(
        string dataDirectory,
        string? pinnedManifestPath,
        JsonSerializerOptions jsonOptions)
    {
        this.dataDirectory = Path.GetFullPath(dataDirectory);
        this.pinnedManifestPath = string.IsNullOrWhiteSpace(pinnedManifestPath)
            ? null
            : Path.GetFullPath(pinnedManifestPath);
        this.jsonOptions = jsonOptions;
    }

    public string DataDirectory => dataDirectory;

    /// <summary>
    /// Resolves the navmesh for a request. An empty <paramref name="levelId"/> means
    /// "whatever the active manifest points at", which is what a single-level project
    /// and every existing client rely on.
    /// </summary>
    public bool TryResolve(string? levelId, out ServerNavigation? navigation, out string error)
    {
        navigation = null;
        error = string.Empty;

        if (!Directory.Exists(dataDirectory))
        {
            error =
                $"The navigation data folder '{dataDirectory}' does not exist. " +
                "Export navigation from Unity (Navigation Editor -> Export for Server).";
            return false;
        }

        string? manifestPath = string.IsNullOrWhiteSpace(levelId)
            ? ResolveDefaultManifest()
            : ResolveManifestForLevel(levelId!);

        if (manifestPath is null)
        {
            error = string.IsNullOrWhiteSpace(levelId)
                ? DescribeMissingDefault()
                : $"Level '{levelId}' is not on the server. Available levels: {DescribeAvailableLevels()}.";
            return false;
        }

        try
        {
            navigation = LoadCached(manifestPath);
            return true;
        }
        catch (Exception exception)
        {
            error = $"Failed to load '{Path.GetFileName(manifestPath)}': {exception.Message}";
            return false;
        }
    }

    /// <summary>Currently active navmesh, or null when nothing has been exported yet.</summary>
    public ServerNavigation? TryGetActive()
    {
        return TryResolve(null, out ServerNavigation? navigation, out _) ? navigation : null;
    }

    public IReadOnlyList<string> AvailableLevelIds()
    {
        var levels = new List<string>();
        foreach ((string levelId, _) in EnumerateManifests())
        {
            if (!levels.Contains(levelId, StringComparer.OrdinalIgnoreCase))
            {
                levels.Add(levelId);
            }
        }

        levels.Sort(StringComparer.Ordinal);
        return levels;
    }

    private string DescribeMissingDefault()
    {
        IReadOnlyList<string> levels = AvailableLevelIds();
        if (levels.Count == 0)
        {
            return
                $"No navigation artifact in '{dataDirectory}'. " +
                "Export navigation from Unity (Navigation Editor -> Export for Server).";
        }

        return
            $"'{NavigationArtifactStore.ActiveManifestFileName}' is missing in '{dataDirectory}', " +
            $"so there is no default level. Send levelId explicitly, one of: {DescribeAvailableLevels()}.";
    }

    private string DescribeAvailableLevels()
    {
        IReadOnlyList<string> levels = AvailableLevelIds();
        return levels.Count == 0 ? "none" : string.Join(", ", levels);
    }

    private string? ResolveDefaultManifest()
    {
        if (pinnedManifestPath is not null && File.Exists(pinnedManifestPath))
        {
            return pinnedManifestPath;
        }

        string active = Path.Combine(dataDirectory, NavigationArtifactStore.ActiveManifestFileName);
        if (File.Exists(active))
        {
            return active;
        }

        // A project with a single exported level should not need an active manifest.
        List<(string LevelId, string Path)> manifests = EnumerateManifests();
        return manifests.Count == 1 ? manifests[0].Path : null;
    }

    private string? ResolveManifestForLevel(string levelId)
    {
        // The active manifest wins when it already holds the requested level, so an
        // explicit levelId returns exactly what a client without one would get.
        string active = Path.Combine(dataDirectory, NavigationArtifactStore.ActiveManifestFileName);
        if (File.Exists(active)
            && string.Equals(ReadLevelId(active), levelId, StringComparison.OrdinalIgnoreCase))
        {
            return active;
        }

        string? newest = null;
        DateTime newestWriteTime = DateTime.MinValue;
        foreach ((string candidateLevel, string path) in EnumerateManifests())
        {
            if (!string.Equals(candidateLevel, levelId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Several hashes of one level can coexist after repeated exports.
            DateTime writeTime = File.GetLastWriteTimeUtc(path);
            if (newest is null || writeTime > newestWriteTime)
            {
                newest = path;
                newestWriteTime = writeTime;
            }
        }

        return newest;
    }

    private List<(string LevelId, string Path)> EnumerateManifests()
    {
        var manifests = new List<(string, string)>();
        if (!Directory.Exists(dataDirectory))
        {
            return manifests;
        }

        foreach (string path in Directory
                     .GetFiles(dataDirectory, "*.manifest.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            if (string.Equals(
                    Path.GetFileName(path),
                    NavigationArtifactStore.ActiveManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string levelId = ReadLevelId(path);
            if (!string.IsNullOrEmpty(levelId))
            {
                manifests.Add((levelId, path));
            }
        }

        return manifests;
    }

    private string ReadLevelId(string manifestPath)
    {
        try
        {
            NavigationArtifactManifest? manifest =
                JsonSerializer.Deserialize<NavigationArtifactManifest>(
                    File.ReadAllText(manifestPath),
                    jsonOptions);
            return manifest?.LevelId ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private ServerNavigation LoadCached(string manifestPath)
    {
        DateTime stamp = File.GetLastWriteTimeUtc(manifestPath);
        lock (gate)
        {
            // Re-exporting rewrites the manifest, so the timestamp is enough to notice
            // that a cached navmesh went stale - the server hot-reloads it.
            if (cache.TryGetValue(manifestPath, out CacheEntry? entry) && entry.Stamp == stamp)
            {
                return entry.Navigation;
            }

            ServerNavigation navigation = NavigationArtifactStore.Load(manifestPath, jsonOptions);
            cache[manifestPath] = new CacheEntry(navigation, stamp);
            return navigation;
        }
    }

    private sealed record CacheEntry(ServerNavigation Navigation, DateTime Stamp);
}

