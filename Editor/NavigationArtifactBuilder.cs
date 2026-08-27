using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Io;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    internal sealed class NavigationArtifactBuildResult
    {
        public readonly byte[] Data;
        public readonly string Hash;
        public readonly int PolygonCount;
        public readonly int SourceMeshCount;
        public readonly NavigationArtifactAsset Asset;
        public readonly string ClientDataPath;
        public readonly string ClientManifestPath;
        public readonly string ServerDataPath;

        /// <summary>How long the build took. Shown on the result card so the designer knows what to expect.</summary>
        public double ElapsedSeconds;

        public int ByteSize => Data?.Length ?? 0;

        public bool ExportedToServer => !string.IsNullOrEmpty(ServerDataPath);

        public NavigationArtifactBuildResult(
            byte[] data,
            string hash,
            int polygonCount,
            int sourceMeshCount,
            NavigationArtifactAsset asset,
            string clientDataPath,
            string clientManifestPath,
            string serverDataPath)
        {
            Data = data;
            Hash = hash;
            PolygonCount = polygonCount;
            SourceMeshCount = sourceMeshCount;
            Asset = asset;
            ClientDataPath = clientDataPath;
            ClientManifestPath = clientManifestPath;
            ServerDataPath = serverDataPath;
        }

        public NavigationArtifactBuildResult WithServerPath(string serverDataPath)
        {
            return new NavigationArtifactBuildResult(
                Data,
                Hash,
                PolygonCount,
                SourceMeshCount,
                Asset,
                ClientDataPath,
                ClientManifestPath,
                serverDataPath)
            {
                ElapsedSeconds = ElapsedSeconds
            };
        }
    }

    /// <summary>What exactly is stored on the server for one level.</summary>
    internal sealed class NavigationServerExportResult
    {
        public readonly string LevelId;
        public readonly string Hash;
        public readonly string ServerDataPath;
        public readonly string ServerManifestPath;
        public readonly bool SetAsActive;

        public NavigationServerExportResult(
            string levelId,
            string hash,
            string serverDataPath,
            string serverManifestPath,
            bool setAsActive)
        {
            LevelId = levelId;
            Hash = hash;
            ServerDataPath = serverDataPath;
            ServerManifestPath = serverManifestPath;
            SetAsActive = setAsActive;
        }
    }

    internal static class NavigationArtifactBuilder
    {
        public const string SchemaVersion = "1";
        public const string DotRecastVersion = "2026.1.3";
        public const string GeneratedClientFolder = "Assets/DataSakura/CustomNavigation/Generated/Navigation";
        public const string ActiveManifestFileName = "active.manifest.json";
        public const string DefaultServerArtifactFolder =
            NavigationServerSettings.DefaultServerArtifactFolder;
        private const int WalkableFlag = 1;

        private static void RunArtifactRoundtripTest()
        {
            GameObject root = null;
            Mesh mesh = null;
            NavigationAgentProfile agent = null;
            NavigationAreaCatalog areas = null;
            NavigationPerformanceProfile performance = null;
            try
            {
                root = new GameObject("Navigation Artifact Test");
                NavigationLevel level = root.AddComponent<NavigationLevel>();
                var floor = new GameObject("Test Floor");
                floor.transform.SetParent(root.transform, false);
                var meshFilter = floor.AddComponent<MeshFilter>();
                mesh = CreateTestFloorMesh();
                meshFilter.sharedMesh = mesh;
                floor.AddComponent<NavigationGeometrySource>();
                var blocker = new GameObject("Test Blocker");
                blocker.transform.SetParent(root.transform, false);
                NavigationModifierVolume modifier = blocker.AddComponent<NavigationModifierVolume>();
                var modifierObject = new SerializedObject(modifier);
                modifierObject.Update();
                modifierObject.FindProperty("center").vector3Value = new Vector3(0f, 1f, 0f);
                modifierObject.FindProperty("size").vector3Value = new Vector3(2.5f, 2f, 2.5f);
                modifierObject.ApplyModifiedPropertiesWithoutUndo();

                agent = ScriptableObject.CreateInstance<NavigationAgentProfile>();
                areas = ScriptableObject.CreateInstance<NavigationAreaCatalog>();
                areas.ResetToDefaults();
                performance = ScriptableObject.CreateInstance<NavigationPerformanceProfile>();
                performance.ApplyStartingPreset(NavigationDeviceTier.MobileLow);
                level.ConfigureDefaults(agent, areas, performance);

                BuiltNavigation built = Build(level);
                byte[] data = Serialize(built.NavMesh);
                string firstHash = ComputeSha256(data);
                byte[] repeatedData = Serialize(Build(level).NavMesh);
                string secondHash = ComputeSha256(repeatedData);
                if (!string.Equals(firstHash, secondHash, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Deterministic build check failed: {firstHash} != {secondHash}.");
                }

                using var reader = new BinaryReader(new MemoryStream(data));
                DtNavMesh loaded = new DtMeshSetReader().Read(reader);
                var query = new DtNavMeshQuery(loaded);
                var filter = new DtQueryDefaultFilter();
                var extents = new RcVec3f(2f, 4f, 2f);
                DtStatus startStatus = query.FindNearestPoly(
                    new RcVec3f(-3f, 0f, -3f),
                    extents,
                    filter,
                    out long startRef,
                    out RcVec3f nearestStart,
                    out _);
                DtStatus endStatus = query.FindNearestPoly(
                    new RcVec3f(3f, 0f, 3f),
                    extents,
                    filter,
                    out long endRef,
                    out RcVec3f nearestEnd,
                    out _);
                var corridor = new long[64];
                DtStatus pathStatus = query.FindPath(
                    startRef,
                    endRef,
                    nearestStart,
                    nearestEnd,
                    filter,
                    corridor.AsSpan(),
                    out int corridorCount,
                    corridor.Length);
                if (startStatus.Failed()
                    || endStatus.Failed()
                    || pathStatus.Failed()
                    || corridorCount == 0)
                {
                    throw new InvalidOperationException("Round-trip artifact could not answer a path query.");
                }

                NavigationArtifactInstance instance = NavigationArtifactLoader.LoadBytes(
                    "artifact_test",
                    firstHash,
                    agent.ProfileId,
                    built.PolygonCount,
                    data);
                var scheduler = new NavigationQueryScheduler(instance, performance, agent);
                NavigationPathResult scheduledResult = null;
                scheduler.RequestPath(
                    new Vector3(-3f, 0f, -3f),
                    new Vector3(3f, 0f, 3f),
                    NavigationQueryPriority.CombatBot,
                    result => scheduledResult = result);
                for (int tick = 0; tick < 32 && scheduledResult == null; tick++)
                {
                    scheduler.Tick();
                }

                if (scheduledResult == null || !scheduledResult.Success || scheduledResult.Points.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Budgeted sliced-query scheduler could not complete the round-trip path.");
                }

                Debug.Log(
                    $"[CustomNavigation] Artifact roundtrip passed: hash={firstHash}, " +
                    $"bytes={data.Length}, polygons={built.PolygonCount}, " +
                    $"scheduledIterations={scheduledResult.Iterations}.");
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }

                if (agent != null)
                {
                    UnityEngine.Object.DestroyImmediate(agent);
                }

                if (areas != null)
                {
                    UnityEngine.Object.DestroyImmediate(areas);
                }

                if (performance != null)
                {
                    UnityEngine.Object.DestroyImmediate(performance);
                }
            }
        }

        private static Mesh CreateTestFloorMesh()
        {
            var mesh = new Mesh { name = "Navigation artifact test mesh" };
            mesh.vertices = new[]
            {
                new Vector3(-5f, 0f, -5f),
                new Vector3(5f, 0f, -5f),
                new Vector3(5f, 0f, 5f),
                new Vector3(-5f, 0f, 5f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

#if UNITY_INCLUDE_TESTS
        internal static NavigationArtifactInstance BuildInMemoryForSchedulerTests(
            NavigationLevel level)
        {
            BuiltNavigation built = Build(level);
            byte[] data = Serialize(built.NavMesh);
            string hash = ComputeSha256(data);
            return NavigationArtifactLoader.LoadBytes(
                level.LevelId,
                hash,
                level.DefaultAgentProfile.ProfileId,
                built.PolygonCount,
                data);
        }
#endif

        /// <summary>Builds the navmesh and writes the client assets. Uploads nothing to the server.</summary>
        public static NavigationArtifactBuildResult BuildForClient(NavigationLevel level)
        {
            return BuildForClient(level, null);
        }

        /// <summary>
        /// The same build with progress reporting. <paramref name="progress"/> may be null
        /// (for example for demo scene builders and tests that need no progress bar).
        /// </summary>
        public static NavigationArtifactBuildResult BuildForClient(
            NavigationLevel level,
            NavigationBuildProgress progress)
        {
            if (level == null)
            {
                throw new ArgumentNullException(nameof(level));
            }

            progress?.Stage("Validating the level");
            List<NavigationValidationIssue> issues = NavigationAuthoringValidator.Validate(level);
            NavigationValidationIssue firstError = issues.FirstOrDefault(
                issue => issue.Severity == NavigationValidationSeverity.Error);
            if (firstError.Severity == NavigationValidationSeverity.Error)
            {
                throw new InvalidOperationException(
                    "Navigation authoring validation failed: " + firstError.Message);
            }

            BuiltNavigation built = Build(level, progress);

            progress?.Stage("Serializing the DotRecast binary");
            byte[] data = Serialize(built.NavMesh);

            progress?.Stage("Hashing with SHA-256");
            string hash = ComputeSha256(data);
            string safeLevelId = NavigationIdUtility.Sanitize(level.LevelId, "level");
            string fileStem = safeLevelId + "." + hash.Substring(0, 12);
            string clientDataPath = $"{GeneratedClientFolder}/{fileStem}.navmesh.bytes";
            string clientManifestPath = $"{GeneratedClientFolder}/{fileStem}.manifest.json";
            string clientAssetPath = $"{GeneratedClientFolder}/{safeLevelId}.artifact.asset";

            progress?.Stage("Writing the artifact into the project");
            EnsureAssetFolder(GeneratedClientFolder);
            WriteAssetFile(clientDataPath, data);

            var manifest = new NavigationArtifactManifest
            {
                schemaVersion = SchemaVersion,
                dotRecastVersion = DotRecastVersion,
                levelId = safeLevelId,
                description = level.Description,
                artifactHash = hash,
                agentProfileId = level.DefaultAgentProfile.ProfileId,
                polygonCount = built.PolygonCount,
                sourceMeshCount = built.SourceMeshCount,
                fileName = Path.GetFileName(clientDataPath)
            };
            string manifestJson = JsonUtility.ToJson(manifest, true);
            WriteAssetFile(clientManifestPath, Encoding.UTF8.GetBytes(manifestJson + "\n"));

            progress?.Stage("Importing the assets into Unity");
            AssetDatabase.ImportAsset(clientDataPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(clientManifestPath, ImportAssetOptions.ForceSynchronousImport);

            TextAsset dataAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(clientDataPath);
            if (dataAsset == null)
            {
                throw new InvalidOperationException("Unity could not import the navigation binary as TextAsset.");
            }

            NavigationArtifactAsset artifactAsset = AssetDatabase.LoadAssetAtPath<NavigationArtifactAsset>(
                clientAssetPath);
            if (artifactAsset == null)
            {
                artifactAsset = ScriptableObject.CreateInstance<NavigationArtifactAsset>();
                artifactAsset.name = safeLevelId + " Navigation Artifact";
                AssetDatabase.CreateAsset(artifactAsset, clientAssetPath);
            }

            artifactAsset.Configure(
                safeLevelId,
                hash,
                SchemaVersion,
                DotRecastVersion,
                level.DefaultAgentProfile.ProfileId,
                built.PolygonCount,
                built.SourceMeshCount,
                dataAsset,
                manifestJson);
            EditorUtility.SetDirty(artifactAsset);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return new NavigationArtifactBuildResult(
                data,
                hash,
                built.PolygonCount,
                built.SourceMeshCount,
                artifactAsset,
                clientDataPath,
                clientManifestPath,
                string.Empty)
            {
                ElapsedSeconds = progress?.ElapsedSeconds ?? 0d
            };
        }

        /// <summary>
        /// Uploads an already built client artifact of the level to the navigation server folder.
        /// Rebuilds nothing: a missing client build is an error, not a silent rebuild,
        /// otherwise a navmesh absent from the client build would reach the server.
        /// </summary>
        public static NavigationServerExportResult ExportForServer(NavigationLevel level, bool setAsActive = true)
        {
            if (level == null)
            {
                throw new ArgumentNullException(nameof(level));
            }

            string safeLevelId = NavigationIdUtility.Sanitize(level.LevelId, "level");
            NavigationArtifactAsset artifactAsset = LoadClientArtifact(safeLevelId);
            if (artifactAsset == null)
            {
                throw new InvalidOperationException(
                    $"No client artifact found for level '{safeLevelId}'. " +
                    "Press Build for Client first.");
            }

            return ExportForServer(artifactAsset, setAsActive);
        }

        public static NavigationServerExportResult ExportForServer(
            NavigationArtifactAsset artifactAsset,
            bool setAsActive = true)
        {
            if (artifactAsset == null)
            {
                throw new ArgumentNullException(nameof(artifactAsset));
            }

            if (artifactAsset.NavigationData == null)
            {
                throw new InvalidOperationException(
                    $"Artifact '{artifactAsset.LevelId}' contains no navmesh binary. " +
                    "Rebuild it with the Build for Client button.");
            }

            if (string.IsNullOrWhiteSpace(artifactAsset.ManifestJson))
            {
                throw new InvalidOperationException(
                    $"Artifact '{artifactAsset.LevelId}' contains no manifest. " +
                    "Rebuild it with the Build for Client button.");
            }

            byte[] data = artifactAsset.NavigationData.bytes;
            string actualHash = ComputeSha256(data);
            if (!string.Equals(actualHash, artifactAsset.ArtifactHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Client artifact '{artifactAsset.LevelId}' is out of date: " +
                    $"manifest={artifactAsset.ArtifactHash}, data={actualHash}. " +
                    "Rebuild it with the Build for Client button.");
            }

            var manifest = JsonUtility.FromJson<NavigationArtifactManifest>(artifactAsset.ManifestJson);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.fileName))
            {
                throw new InvalidOperationException(
                    $"The manifest of artifact '{artifactAsset.LevelId}' is corrupted. " +
                    "Rebuild it with the Build for Client button.");
            }

            string manifestJson = artifactAsset.ManifestJson;
            string serverFolder = ResolveServerFolder();
            Directory.CreateDirectory(serverFolder);
            string serverDataPath = Path.Combine(serverFolder, manifest.fileName);
            string serverManifestPath = Path.Combine(
                serverFolder,
                Path.GetFileNameWithoutExtension(
                    Path.GetFileNameWithoutExtension(manifest.fileName)) + ".manifest.json");
            File.WriteAllBytes(serverDataPath, data);
            File.WriteAllText(serverManifestPath, manifestJson + "\n", new UTF8Encoding(false));
            if (setAsActive)
            {
                File.WriteAllText(
                    Path.Combine(serverFolder, ActiveManifestFileName),
                    manifestJson + "\n",
                    new UTF8Encoding(false));
            }

            return new NavigationServerExportResult(
                manifest.levelId,
                manifest.artifactHash,
                serverDataPath,
                serverManifestPath,
                setAsActive);
        }

        /// <summary>
        /// Client build plus server export in one operation.
        /// Writes into the server NavigationData folder and therefore creates it on disk,
        /// so call this only from explicit user actions (never from importers or
        /// <c>[InitializeOnLoadMethod]</c> hooks). LocalOnly flows should use
        /// <see cref="BuildForClient(NavigationLevel)"/> instead.
        /// </summary>
        public static NavigationArtifactBuildResult BuildAndExport(NavigationLevel level)
        {
            NavigationArtifactBuildResult result = BuildForClient(level);
            NavigationServerExportResult export = ExportForServer(result.Asset);
            return result.WithServerPath(export.ServerDataPath);
        }

        public static NavigationArtifactAsset LoadClientArtifact(string levelId)
        {
            string safeLevelId = NavigationIdUtility.Sanitize(levelId, "level");
            return AssetDatabase.LoadAssetAtPath<NavigationArtifactAsset>(
                $"{GeneratedClientFolder}/{safeLevelId}.artifact.asset");
        }

        /// <summary>
        /// Returns the generated project files owned by one client artifact. Server exports are
        /// deliberately excluded: removing a local bake must not mutate a running server.
        /// </summary>
        public static IReadOnlyList<string> GetClientArtifactPaths(NavigationArtifactAsset artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            var paths = new List<string>(3);
            AddGeneratedClientPath(paths, AssetDatabase.GetAssetPath(artifact), "artifact asset");

            string payloadPath = artifact.NavigationData != null
                ? AssetDatabase.GetAssetPath(artifact.NavigationData)
                : string.Empty;
            AddGeneratedClientPath(paths, payloadPath, "navigation payload", allowMissing: true);

            if (!string.IsNullOrEmpty(payloadPath))
            {
                const string payloadSuffix = ".navmesh.bytes";
                if (!payloadPath.EndsWith(payloadSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Navigation payload '{payloadPath}' does not use the expected '{payloadSuffix}' suffix.");
                }

                string manifestPath = payloadPath.Substring(0, payloadPath.Length - payloadSuffix.Length)
                    + ".manifest.json";
                AddGeneratedClientPath(paths, manifestPath, "navigation manifest", allowMissing: true);
            }

            return paths;
        }

        /// <summary>Deletes only the selected client artifact and its generated payload files.</summary>
        public static void DeleteClientArtifact(NavigationArtifactAsset artifact)
        {
            IReadOnlyList<string> paths = GetClientArtifactPaths(artifact);
            for (int i = paths.Count - 1; i >= 0; i--)
            {
                string path = paths[i];
                if (AssetDatabase.LoadMainAssetAtPath(path) != null && !AssetDatabase.DeleteAsset(path))
                {
                    throw new IOException($"Unity could not delete generated navigation file '{path}'.");
                }
            }

            AssetDatabase.Refresh();
        }

        private static void AddGeneratedClientPath(
            ICollection<string> paths,
            string path,
            string description,
            bool allowMissing = false)
        {
            if (string.IsNullOrEmpty(path))
            {
                if (allowMissing)
                {
                    return;
                }

                throw new InvalidOperationException($"The {description} is not stored in the project.");
            }

            string normalized = path.Replace('\\', '/');
            string prefix = GeneratedClientFolder.TrimEnd('/') + "/";
            if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Refusing to delete {description} outside '{GeneratedClientFolder}': {normalized}");
            }

            if (!paths.Contains(normalized))
            {
                paths.Add(normalized);
            }
        }

        /// <summary>Absolute path to the server NavigationData folder (configured in NavigationServerSettings).</summary>
        public static string ResolveServerFolder()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Cannot resolve the Unity project root.");
            NavigationServerSettings settings = NavigationServerSettings.LoadOrNull();
            string relative = settings != null
                ? settings.ServerArtifactFolder
                : DefaultServerArtifactFolder;
            return Path.GetFullPath(Path.Combine(projectRoot, relative));
        }

        private static BuiltNavigation Build(NavigationLevel level)
        {
            return Build(level, null);
        }

        private static BuiltNavigation Build(NavigationLevel level, NavigationBuildProgress progress)
        {
            progress?.Stage("Collecting geometry");
            List<NavigationGeometrySource> sources = level.GeometryRoot
                .GetComponentsInChildren<NavigationGeometrySource>(true)
                .OrderBy(GetStableObjectId, StringComparer.Ordinal)
                .ToList();

            var vertices = new List<float>();
            var triangles = new List<int>();
            var sourceVolumes = new List<WorldVolume>();
            var addedMeshes = new HashSet<string>(StringComparer.Ordinal);
            int sourceMeshCount = 0;

            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                NavigationGeometrySource source = sources[sourceIndex];
                progress?.Report(
                    $"Collecting geometry ({sourceIndex + 1} / {sources.Count} sources)",
                    sources.Count == 0 ? 1f : (float)sourceIndex / sources.Count);
                if (source.Mode == NavigationGeometryMode.Ignore)
                {
                    continue;
                }

                List<MeshFilter> meshes = GetSourceMeshes(source);
                for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
                {
                    MeshFilter meshFilter = meshes[meshIndex];
                    string meshId = GetStableObjectId(meshFilter);
                    if (!addedMeshes.Add(meshId) || meshFilter.sharedMesh == null)
                    {
                        continue;
                    }

                    if (source.Mode == NavigationGeometryMode.Block)
                    {
                        sourceVolumes.Add(CreateMeshBoundsVolume(meshFilter, 0));
                        continue;
                    }

                    AppendMesh(meshFilter, vertices, triangles);
                    sourceMeshCount++;
                    if (source.AreaId != 1)
                    {
                        sourceVolumes.Add(CreateMeshBoundsVolume(meshFilter, source.AreaId));
                    }
                }
            }

            if (triangles.Count == 0)
            {
                throw new InvalidOperationException("No Include source produced navigation triangles.");
            }

            var geometry = new RcSampleInputGeomProvider(vertices.ToArray(), triangles.ToArray());
            progress?.Stage("Modifier volumes and links");
            for (int i = 0; i < sourceVolumes.Count; i++)
            {
                AddVolume(geometry, sourceVolumes[i]);
            }

            NavigationModifierVolume[] modifiers = level.GetComponentsInChildren<NavigationModifierVolume>(true);
            Array.Sort(modifiers, (left, right) => string.CompareOrdinal(
                GetStableObjectId(left),
                GetStableObjectId(right)));
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (modifiers[i].Mode != NavigationGeometryMode.Ignore)
                {
                    AddVolume(geometry, CreateModifierVolume(modifiers[i]));
                }
            }

            NavigationLink[] links = level.GetComponentsInChildren<NavigationLink>(true);
            Array.Sort(links, (left, right) => string.CompareOrdinal(left.LinkId, right.LinkId));
            for (int i = 0; i < links.Length; i++)
            {
                NavigationLink link = links[i];
                geometry.AddOffMeshConnection(
                    ToRc(link.WorldStart),
                    ToRc(link.WorldEnd),
                    link.Radius,
                    link.Bidirectional,
                    link.AreaId,
                    ResolvePolygonFlags(level.AreaCatalog, link.AreaId));
            }

            NavigationBuildSettings settings = level.BuildSettings;
            NavigationAgentProfile agent = level.DefaultAgentProfile;
            var config = new RcConfig(
                RcPartition.WATERSHED,
                settings.CellSize,
                settings.CellHeight,
                agent.MaximumSlope,
                agent.Height,
                agent.Radius,
                agent.MaximumClimb,
                Mathf.Max(1, Mathf.RoundToInt(settings.MinimumRegionArea)),
                Mathf.Max(1, Mathf.RoundToInt(settings.MergedRegionArea)),
                settings.MaximumEdgeLength,
                settings.MaximumEdgeError,
                settings.MaximumVerticesPerPolygon,
                settings.DetailSampleDistance,
                settings.DetailSampleMaximumError,
                true,
                true,
                true,
                new RcAreaModification(RcRecast.RC_WALKABLE_AREA),
                true);

            var builderConfig = new RcBuilderConfig(
                config,
                geometry.GetMeshBoundsMin(),
                geometry.GetMeshBoundsMax());
            progress?.Stage("Recast: voxels, regions, contours");
            RcBuilderResult result = new RcBuilder().Build(geometry, builderConfig, false);
            if (result?.Mesh == null || result.Mesh.npolys == 0)
            {
                throw new InvalidOperationException("DotRecast produced an empty navigation mesh.");
            }

            progress?.Stage("Detour: building the tile");
            DtMeshData meshData = BuildDetourData(level, geometry, result, config);
            var navMesh = new DtNavMesh();
            DtStatus status = navMesh.Init(meshData, config.MaxVertsPerPoly, 0);
            if (status.Failed())
            {
                throw new InvalidOperationException("Detour could not initialize the generated navigation mesh.");
            }

            return new BuiltNavigation(navMesh, result.Mesh.npolys, sourceMeshCount);
        }

        private static List<MeshFilter> GetSourceMeshes(NavigationGeometrySource source)
        {
            IEnumerable<MeshFilter> values = source.IncludeChildren
                ? source.GetComponentsInChildren<MeshFilter>(source.IncludeInactiveChildren)
                : source.TryGetComponent(out MeshFilter mesh)
                    ? new[] { mesh }
                    : Array.Empty<MeshFilter>();
            return values
                .Where(value => value != null && value.sharedMesh != null)
                .OrderBy(GetStableObjectId, StringComparer.Ordinal)
                .ToList();
        }

        private static void AppendMesh(
            MeshFilter meshFilter,
            List<float> vertices,
            List<int> triangles)
        {
            Mesh mesh = meshFilter.sharedMesh;
            if (!mesh.isReadable)
            {
                throw new InvalidOperationException(
                    $"Mesh '{AssetDatabase.GetAssetPath(mesh)}' is not readable. " +
                    "Enable Read/Write for editor navigation export, then disable it in a build-specific copy if needed.");
            }

            Vector3[] localVertices = mesh.vertices;
            int[] localTriangles = mesh.triangles;
            int vertexOffset = vertices.Count / 3;
            Matrix4x4 localToWorld = meshFilter.transform.localToWorldMatrix;
            for (int i = 0; i < localVertices.Length; i++)
            {
                Vector3 world = localToWorld.MultiplyPoint3x4(localVertices[i]);
                vertices.Add(CanonicalFloat(world.x));
                vertices.Add(CanonicalFloat(world.y));
                vertices.Add(CanonicalFloat(world.z));
            }

            bool reverseWinding = localToWorld.determinant < 0f;
            for (int i = 0; i < localTriangles.Length; i += 3)
            {
                triangles.Add(vertexOffset + localTriangles[i]);
                triangles.Add(vertexOffset + localTriangles[i + (reverseWinding ? 2 : 1)]);
                triangles.Add(vertexOffset + localTriangles[i + (reverseWinding ? 1 : 2)]);
            }
        }

        private static WorldVolume CreateMeshBoundsVolume(MeshFilter meshFilter, int areaId)
        {
            Bounds bounds = meshFilter.sharedMesh.bounds;
            return CreateWorldVolume(
                meshFilter.transform.localToWorldMatrix,
                bounds.center,
                bounds.size,
                areaId);
        }

        private static WorldVolume CreateModifierVolume(NavigationModifierVolume modifier)
        {
            int areaId = modifier.Mode == NavigationGeometryMode.Block ? 0 : modifier.AreaId;
            return CreateWorldVolume(
                modifier.LocalToWorldMatrix,
                modifier.Center,
                modifier.Size,
                areaId);
        }

        private static WorldVolume CreateWorldVolume(
            Matrix4x4 matrix,
            Vector3 center,
            Vector3 size,
            int areaId)
        {
            Vector3 half = size * 0.5f;
            var worldCorners = new Vector3[8];
            int cornerIndex = 0;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        worldCorners[cornerIndex++] = matrix.MultiplyPoint3x4(
                            center + Vector3.Scale(half, new Vector3(x, y, z)));
                    }
                }
            }

            float minimumHeight = worldCorners.Min(value => value.y);
            float maximumHeight = worldCorners.Max(value => value.y);
            List<Vector2> hull = BuildConvexHull(worldCorners.Select(value => new Vector2(value.x, value.z)));
            if (hull.Count < 3)
            {
                throw new InvalidOperationException("Navigation modifier volume has a degenerate footprint.");
            }

            var footprint = new float[hull.Count * 3];
            for (int i = 0; i < hull.Count; i++)
            {
                footprint[i * 3] = CanonicalFloat(hull[i].x);
                footprint[i * 3 + 1] = CanonicalFloat(minimumHeight);
                footprint[i * 3 + 2] = CanonicalFloat(hull[i].y);
            }

            return new WorldVolume(
                footprint,
                CanonicalFloat(minimumHeight - 0.01f),
                CanonicalFloat(maximumHeight + 0.01f),
                areaId);
        }

        private static List<Vector2> BuildConvexHull(IEnumerable<Vector2> input)
        {
            List<Vector2> points = input
                .Select(value => new Vector2(CanonicalFloat(value.x), CanonicalFloat(value.y)))
                .Distinct()
                .OrderBy(value => value.x)
                .ThenBy(value => value.y)
                .ToList();
            if (points.Count <= 1)
            {
                return points;
            }

            var lower = new List<Vector2>();
            for (int i = 0; i < points.Count; i++)
            {
                while (lower.Count >= 2 && Cross(
                           lower[lower.Count - 2],
                           lower[lower.Count - 1],
                           points[i]) <= 0f)
                {
                    lower.RemoveAt(lower.Count - 1);
                }

                lower.Add(points[i]);
            }

            var upper = new List<Vector2>();
            for (int i = points.Count - 1; i >= 0; i--)
            {
                while (upper.Count >= 2 && Cross(
                           upper[upper.Count - 2],
                           upper[upper.Count - 1],
                           points[i]) <= 0f)
                {
                    upper.RemoveAt(upper.Count - 1);
                }

                upper.Add(points[i]);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            lower.AddRange(upper);
            return lower;
        }

        private static float Cross(Vector2 origin, Vector2 left, Vector2 right)
        {
            return (left.x - origin.x) * (right.y - origin.y)
                   - (left.y - origin.y) * (right.x - origin.x);
        }

        private static void AddVolume(RcSampleInputGeomProvider geometry, WorldVolume volume)
        {
            geometry.AddConvexVolume(
                volume.Footprint,
                volume.MinimumHeight,
                volume.MaximumHeight,
                new RcAreaModification(volume.AreaId));
        }

        private static DtMeshData BuildDetourData(
            NavigationLevel level,
            RcSampleInputGeomProvider geometry,
            RcBuilderResult result,
            RcConfig config)
        {
            RcPolyMesh polygonMesh = result.Mesh;
            RcPolyMeshDetail detailMesh = result.MeshDetail;
            for (int i = 0; i < polygonMesh.npolys; i++)
            {
                if (polygonMesh.areas[i] == RcRecast.RC_WALKABLE_AREA)
                {
                    polygonMesh.areas[i] = 1;
                }

                polygonMesh.flags[i] = ResolvePolygonFlags(level.AreaCatalog, polygonMesh.areas[i]);
            }

            var parameters = new DtNavMeshCreateParams
            {
                verts = polygonMesh.verts,
                vertCount = polygonMesh.nverts,
                polys = polygonMesh.polys,
                polyAreas = polygonMesh.areas,
                polyFlags = polygonMesh.flags,
                polyCount = polygonMesh.npolys,
                nvp = polygonMesh.nvp,
                walkableHeight = level.DefaultAgentProfile.Height,
                walkableRadius = level.DefaultAgentProfile.Radius,
                walkableClimb = level.DefaultAgentProfile.MaximumClimb,
                bmin = polygonMesh.bmin,
                bmax = polygonMesh.bmax,
                cs = config.Cs,
                ch = config.Ch,
                buildBvTree = true
            };

            if (detailMesh != null)
            {
                parameters.detailMeshes = detailMesh.meshes;
                parameters.detailVerts = detailMesh.verts;
                parameters.detailVertsCount = detailMesh.nverts;
                parameters.detailTris = detailMesh.tris;
                parameters.detailTriCount = detailMesh.ntris;
            }

            List<RcOffMeshConnection> connections = geometry.GetOffMeshConnections();
            parameters.offMeshConCount = connections.Count;
            parameters.offMeshConVerts = new float[connections.Count * 6];
            parameters.offMeshConRad = new float[connections.Count];
            parameters.offMeshConDir = new int[connections.Count];
            parameters.offMeshConAreas = new int[connections.Count];
            parameters.offMeshConFlags = new int[connections.Count];
            parameters.offMeshConUserID = new int[connections.Count];
            for (int i = 0; i < connections.Count; i++)
            {
                RcOffMeshConnection connection = connections[i];
                Array.Copy(connection.verts, 0, parameters.offMeshConVerts, i * 6, 6);
                parameters.offMeshConRad[i] = connection.radius;
                parameters.offMeshConDir[i] = connection.bidir ? 1 : 0;
                parameters.offMeshConAreas[i] = connection.area;
                parameters.offMeshConFlags[i] = connection.flags;
                parameters.offMeshConUserID[i] = i + 1;
            }

            return DtNavMeshBuilder.CreateNavMeshData(parameters)
                   ?? throw new InvalidOperationException("DotRecast could not create Detour mesh data.");
        }

        private static int ResolvePolygonFlags(NavigationAreaCatalog catalog, int areaId)
        {
            if (catalog != null)
            {
                for (int i = 0; i < catalog.Areas.Count; i++)
                {
                    NavigationAreaDefinition area = catalog.Areas[i];
                    if (area != null && area.Id == areaId)
                    {
                        return area.PolygonFlags == 0 ? WalkableFlag : area.PolygonFlags;
                    }
                }
            }

            return WalkableFlag;
        }

        private static byte[] Serialize(DtNavMesh navMesh)
        {
            using var memory = new MemoryStream();
            using (var writer = new BinaryWriter(memory, Encoding.UTF8, true))
            {
                new DtMeshSetWriter().Write(
                    writer,
                    navMesh,
                    RcByteOrder.LITTLE_ENDIAN,
                    false);
            }

            byte[] bytes = memory.ToArray();
            using var reader = new BinaryReader(new MemoryStream(bytes));
            DtNavMesh roundTrip = new DtMeshSetReader().Read(reader);
            if (roundTrip == null)
            {
                throw new InvalidOperationException("DotRecast artifact round-trip verification failed.");
            }

            return bytes;
        }

        private static string ComputeSha256(byte[] data)
        {
            using SHA256 algorithm = SHA256.Create();
            byte[] hash = algorithm.ComputeHash(data);
            var builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static float CanonicalFloat(float value)
        {
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException("Navigation geometry contains a non-finite coordinate.");
            }

            return Mathf.Round(value * 10000f) * 0.0001f;
        }

        private static RcVec3f ToRc(Vector3 value)
        {
            return new RcVec3f(
                CanonicalFloat(value.x),
                CanonicalFloat(value.y),
                CanonicalFloat(value.z));
        }

        private static string GetStableObjectId(UnityEngine.Object value)
        {
            return GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
        }

        private static void WriteAssetFile(string assetPath, byte[] data)
        {
            string absolutePath = Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Cannot resolve the Unity project root."),
                assetPath);
            File.WriteAllBytes(absolutePath, data);
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        [Serializable]
        internal sealed class NavigationArtifactManifest
        {
            public string schemaVersion;
            public string dotRecastVersion;
            public string levelId;
            public string description;
            public string artifactHash;
            public string agentProfileId;
            public int polygonCount;
            public int sourceMeshCount;
            public string fileName;
        }

        private readonly struct WorldVolume
        {
            public readonly float[] Footprint;
            public readonly float MinimumHeight;
            public readonly float MaximumHeight;
            public readonly int AreaId;

            public WorldVolume(float[] footprint, float minimumHeight, float maximumHeight, int areaId)
            {
                Footprint = footprint;
                MinimumHeight = minimumHeight;
                MaximumHeight = maximumHeight;
                AreaId = areaId;
            }
        }

        private readonly struct BuiltNavigation
        {
            public readonly DtNavMesh NavMesh;
            public readonly int PolygonCount;
            public readonly int SourceMeshCount;

            public BuiltNavigation(DtNavMesh navMesh, int polygonCount, int sourceMeshCount)
            {
                NavMesh = navMesh;
                PolygonCount = polygonCount;
                SourceMeshCount = sourceMeshCount;
            }
        }
    }
}
