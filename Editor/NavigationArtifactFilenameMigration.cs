using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    public sealed class NavigationArtifactFilenameMigrationResult
    {
        public bool Succeeded { get; }
        public int MigratedArtifactCount { get; }
        public IReadOnlyList<string> Messages { get; }

        internal NavigationArtifactFilenameMigrationResult(
            bool succeeded,
            int migratedArtifactCount,
            IReadOnlyList<string> messages)
        {
            Succeeded = succeeded;
            MigratedArtifactCount = migratedArtifactCount;
            Messages = messages ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Explicitly moves hash-based generated artifacts to stable level-based names. Unity moves
    /// every file through AssetDatabase so .meta GUIDs and serialized references are preserved.
    /// </summary>
    public static class NavigationArtifactFilenameMigration
    {
        public static NavigationArtifactFilenameMigrationResult Migrate()
        {
            return Migrate(NavigationArtifactBuilder.GeneratedClientFolder);
        }

        internal static NavigationArtifactFilenameMigrationResult Migrate(string generatedRoot)
        {
            var messages = new List<string>();
            if (!AssetDatabase.IsValidFolder(generatedRoot))
            {
                messages.Add("No generated navigation folder was found; nothing to migrate.");
                return new NavigationArtifactFilenameMigrationResult(true, 0, messages);
            }

            string[] guids = AssetDatabase.FindAssets(
                "t:" + nameof(NavigationArtifactAsset),
                new[] { generatedRoot });
            var items = new List<MigrationItem>(guids.Length);
            var targetOwners = new Dictionary<string, NavigationArtifactAsset>(StringComparer.Ordinal);
            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                NavigationArtifactAsset artifact =
                    AssetDatabase.LoadAssetAtPath<NavigationArtifactAsset>(assetPath);
                if (artifact == null)
                {
                    continue;
                }

                try
                {
                    MigrationItem item = Inspect(artifact, assetPath, generatedRoot);
                    if (targetOwners.TryGetValue(item.TargetAssetPath, out NavigationArtifactAsset owner)
                        && owner != artifact)
                    {
                        throw new InvalidOperationException(
                            $"Multiple artifacts resolve to '{item.TargetAssetPath}'. " +
                            "Rename the duplicate Navigation Level before migration.");
                    }

                    targetOwners[item.TargetAssetPath] = artifact;
                    items.Add(item);
                }
                catch (Exception exception)
                {
                    messages.Add($"Conflict: {assetPath}: {exception.Message} Nothing was moved.");
                    return new NavigationArtifactFilenameMigrationResult(false, 0, messages);
                }
            }

            items.Sort((left, right) => string.CompareOrdinal(left.AssetPath, right.AssetPath));
            for (int i = 0; i < items.Count; i++)
            {
                try
                {
                    PreflightDestination(items[i].PayloadPath, items[i].TargetPayloadPath);
                    PreflightDestination(items[i].ManifestPath, items[i].TargetManifestPath);
                    PreflightDestination(items[i].AssetPath, items[i].TargetAssetPath);
                }
                catch (Exception exception)
                {
                    messages.Add($"Conflict: {exception.Message} Nothing was moved.");
                    return new NavigationArtifactFilenameMigrationResult(false, 0, messages);
                }
            }

            int migrated = 0;
            for (int i = 0; i < items.Count; i++)
            {
                MigrationItem item = items[i];
                bool changed = MoveIfNeeded(item.PayloadPath, item.TargetPayloadPath);
                changed |= MoveIfNeeded(item.ManifestPath, item.TargetManifestPath);
                changed |= MoveIfNeeded(item.AssetPath, item.TargetAssetPath);

                var manifest = JsonUtility.FromJson<NavigationArtifactBuilder.NavigationArtifactManifest>(
                    item.Artifact.ManifestJson);
                string targetFileName = Path.GetFileName(item.TargetPayloadPath);
                bool manifestChanged = !string.Equals(
                    item.ManifestFileName,
                    targetFileName,
                    StringComparison.Ordinal);
                if (changed || manifestChanged)
                {
                    manifest.fileName = targetFileName;
                    string manifestJson = JsonUtility.ToJson(manifest, true);
                    File.WriteAllText(
                        item.TargetManifestPath,
                        manifestJson + "\n",
                        new UTF8Encoding(false));
                    AssetDatabase.ImportAsset(
                        item.TargetManifestPath,
                        ImportAssetOptions.ForceSynchronousImport);

                    TextAsset payload = AssetDatabase.LoadAssetAtPath<TextAsset>(item.TargetPayloadPath)
                                        ?? throw new InvalidOperationException(
                                            $"Unity could not reload '{item.TargetPayloadPath}' after migration.");
                    item.Artifact.Configure(
                        item.Artifact.LevelId,
                        item.Artifact.ArtifactHash,
                        item.Artifact.SchemaVersion,
                        item.Artifact.DotRecastVersion,
                        item.Artifact.Precision,
                        item.Artifact.CanonicalJitterAssemblySha256,
                        item.Artifact.DeterministicMathCompatibilityId,
                        item.Artifact.FingerprintAlgorithmVersion,
                        item.Artifact.FingerprintAlgorithmId,
                        item.Artifact.AgentProfileId,
                        item.Artifact.PolygonCount,
                        item.Artifact.SourceMeshCount,
                        payload,
                        manifestJson);
                    item.Artifact.name = item.Artifact.LevelId + " Navigation Artifact";
                    EditorUtility.SetDirty(item.Artifact);
                    migrated++;
                    messages.Add(
                        $"Migrated '{item.Artifact.LevelId}' to " +
                        $"{Path.GetFileName(item.TargetPayloadPath)}, " +
                        $"{Path.GetFileName(item.TargetManifestPath)}, and " +
                        $"{Path.GetFileName(item.TargetAssetPath)}.");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (messages.Count == 0)
            {
                messages.Add("Artifact filenames already use the current level-based convention.");
            }

            return new NavigationArtifactFilenameMigrationResult(true, migrated, messages);
        }

        private static MigrationItem Inspect(
            NavigationArtifactAsset artifact,
            string assetPath,
            string generatedRoot)
        {
            if (artifact.NavigationData == null)
            {
                throw new InvalidOperationException("The artifact has no payload reference.");
            }

            string payloadPath = AssetDatabase.GetAssetPath(artifact.NavigationData);
            RequireGeneratedPath(assetPath, generatedRoot);
            RequireGeneratedPath(payloadPath, generatedRoot);

            byte[] bytes = artifact.NavigationData.bytes;
            string actualHash = ComputeSha256(bytes);
            if (!string.Equals(actualHash, artifact.ArtifactHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Payload SHA-256 mismatch: asset={artifact.ArtifactHash}, data={actualHash}.");
            }

            NavigationArtifactBuilder.NavigationArtifactManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<NavigationArtifactBuilder.NavigationArtifactManifest>(
                    artifact.ManifestJson);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Manifest JSON is invalid.", exception);
            }

            if (manifest == null
                || string.IsNullOrWhiteSpace(manifest.fileName)
                || !string.Equals(manifest.artifactHash, actualHash, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(manifest.levelId, artifact.LevelId, StringComparison.Ordinal)
                || !string.Equals(manifest.schemaVersion, artifact.SchemaVersion, StringComparison.Ordinal)
                || !string.Equals(manifest.dotRecastVersion, artifact.DotRecastVersion,
                    StringComparison.Ordinal)
                || !string.Equals(manifest.precision, artifact.Precision, StringComparison.Ordinal)
                || !string.Equals(manifest.canonicalJitterAssemblySha256,
                    artifact.CanonicalJitterAssemblySha256, StringComparison.Ordinal)
                || !string.Equals(manifest.deterministicMathCompatibilityId,
                    artifact.DeterministicMathCompatibilityId, StringComparison.Ordinal)
                || manifest.fingerprintAlgorithmVersion != artifact.FingerprintAlgorithmVersion
                || !string.Equals(manifest.fingerprintAlgorithmId,
                    artifact.FingerprintAlgorithmId, StringComparison.Ordinal)
                || manifest.polygonCount != artifact.PolygonCount
                || manifest.sourceMeshCount != artifact.SourceMeshCount)
            {
                throw new InvalidOperationException(
                    "Manifest is missing or does not match the artifact identity, versions, counts, " +
                    "or payload SHA-256.");
            }

            string safeLevelId = NavigationIdUtility.Sanitize(artifact.LevelId, "level");
            string targetAssetPath = generatedRoot + "/" + safeLevelId
                                     + NavigationArtifactBuilder.NavigationAssetSuffix;
            string targetPayloadPath = generatedRoot + "/" + safeLevelId
                                       + NavigationArtifactBuilder.NavigationDataSuffix;
            string targetManifestPath = generatedRoot + "/" + safeLevelId
                                        + NavigationArtifactBuilder.NavigationManifestSuffix;
            string manifestPath = targetManifestPath;
            if (!File.Exists(manifestPath))
            {
                string legacyManifestName = NavigationArtifactBuilder.GetManifestFileName(manifest.fileName);
                manifestPath = generatedRoot + "/" + legacyManifestName;
            }

            RequireGeneratedPath(manifestPath, generatedRoot);
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException("The manifest file referenced by the artifact was not found.", manifestPath);
            }

            return new MigrationItem(
                artifact,
                assetPath,
                payloadPath,
                manifestPath,
                targetAssetPath,
                targetPayloadPath,
                targetManifestPath,
                manifest.fileName);
        }

        private static void PreflightDestination(string source, string destination)
        {
            if (string.Equals(source, destination, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(destination)) || File.Exists(destination))
            {
                throw new InvalidOperationException(
                    $"destination '{destination}' already exists for source '{source}'.");
            }
        }

        private static bool MoveIfNeeded(string source, string destination)
        {
            if (string.Equals(source, destination, StringComparison.Ordinal))
            {
                return false;
            }

            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error))
            {
                throw new IOException(
                    $"Unity refused the GUID-preserving move '{source}' -> '{destination}': {error}");
            }

            return true;
        }

        private static void RequireGeneratedPath(string path, string generatedRoot)
        {
            string prefix = generatedRoot.TrimEnd('/') + "/";
            if (string.IsNullOrWhiteSpace(path)
                || !path.Replace('\\', '/').StartsWith(prefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Artifact path '{path}' is outside generated navigation root '{prefix}'.");
            }
        }

        private static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(data);
            var text = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                text.Append(hash[i].ToString("x2"));
            }

            return text.ToString();
        }

        private sealed class MigrationItem
        {
            public readonly NavigationArtifactAsset Artifact;
            public readonly string AssetPath;
            public readonly string PayloadPath;
            public readonly string ManifestPath;
            public readonly string TargetAssetPath;
            public readonly string TargetPayloadPath;
            public readonly string TargetManifestPath;
            public readonly string ManifestFileName;

            public MigrationItem(
                NavigationArtifactAsset artifact,
                string assetPath,
                string payloadPath,
                string manifestPath,
                string targetAssetPath,
                string targetPayloadPath,
                string targetManifestPath,
                string manifestFileName)
            {
                Artifact = artifact;
                AssetPath = assetPath;
                PayloadPath = payloadPath;
                ManifestPath = manifestPath;
                TargetAssetPath = targetAssetPath;
                TargetPayloadPath = targetPayloadPath;
                TargetManifestPath = targetManifestPath;
                ManifestFileName = manifestFileName;
            }
        }
    }
}
