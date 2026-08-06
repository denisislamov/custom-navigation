using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    internal enum NavigationArtifactSyncState
    {
        InSync,
        ServerOutdated,
        MissingOnServer,
        MissingInClient,
        Broken
    }

    /// <summary>A single comparison row: client map versus server map.</summary>
    internal sealed class NavigationArtifactComparison
    {
        public string LevelId = string.Empty;
        public NavigationArtifactAsset ClientAsset;
        public string ClientHash = string.Empty;
        public int ClientPolygonCount;
        public NavigationServerEditorClient.ServerArtifact ServerArtifact;
        public string ServerHash = string.Empty;
        public int ServerPolygonCount;
        public bool ServerHasLevel;
        public bool ServerActive;
        public bool ServerLoaded;
        public NavigationArtifactSyncState State = NavigationArtifactSyncState.Broken;
        public string Details = string.Empty;

        public bool HasClient => ClientAsset != null;
    }

    /// <summary>
    /// Collects the list of client navmesh artifacts (what ships with the app build)
    /// and compares it against the list reported by the running navigation server.
    /// </summary>
    internal static class NavigationArtifactIndex
    {
        public static List<NavigationArtifactAsset> ScanClientArtifacts()
        {
            var result = new List<NavigationArtifactAsset>();
            if (!AssetDatabase.IsValidFolder(NavigationArtifactBuilder.GeneratedClientFolder))
            {
                return result;
            }

            string[] guids = AssetDatabase.FindAssets(
                "t:" + nameof(NavigationArtifactAsset),
                new[] { NavigationArtifactBuilder.GeneratedClientFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                var asset = AssetDatabase.LoadAssetAtPath<NavigationArtifactAsset>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (asset != null)
                {
                    result.Add(asset);
                }
            }

            result.Sort((a, b) => string.Compare(a.LevelId, b.LevelId, StringComparison.Ordinal));
            return result;
        }

        public static List<NavigationArtifactComparison> Compare(
            IReadOnlyList<NavigationArtifactAsset> clientArtifacts,
            NavigationServerEditorClient.ArtifactsResponse serverResponse,
            bool serverReachable)
        {
            var rows = new List<NavigationArtifactComparison>();
            NavigationServerEditorClient.ServerArtifact[] serverArtifacts =
                serverResponse?.artifacts ?? Array.Empty<NavigationServerEditorClient.ServerArtifact>();
            var matchedServerArtifacts = new HashSet<NavigationServerEditorClient.ServerArtifact>();

            for (int i = 0; i < clientArtifacts.Count; i++)
            {
                NavigationArtifactAsset client = clientArtifacts[i];
                var row = new NavigationArtifactComparison
                {
                    LevelId = client.LevelId,
                    ClientAsset = client,
                    ClientHash = client.ArtifactHash,
                    ClientPolygonCount = client.PolygonCount
                };

                NavigationServerEditorClient.ServerArtifact exact = null;
                NavigationServerEditorClient.ServerArtifact sameLevel = null;
                for (int s = 0; s < serverArtifacts.Length; s++)
                {
                    NavigationServerEditorClient.ServerArtifact candidate = serverArtifacts[s];
                    if (!string.Equals(candidate.levelId, client.LevelId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // Previously uploaded versions of the same level stay in the server folder,
                    // so the one marked active is treated as current.
                    if (sameLevel == null || candidate.isActive)
                    {
                        sameLevel = candidate;
                    }

                    if (string.Equals(candidate.artifactHash, client.ArtifactHash, StringComparison.OrdinalIgnoreCase))
                    {
                        exact = candidate;
                    }
                }

                NavigationServerEditorClient.ServerArtifact chosen = exact ?? sameLevel;
                if (chosen != null)
                {
                    matchedServerArtifacts.Add(chosen);
                    row.ServerArtifact = chosen;
                    row.ServerHash = chosen.artifactHash;
                    row.ServerPolygonCount = chosen.polygonCount;
                    row.ServerHasLevel = true;
                    row.ServerActive = chosen.isActive;
                    row.ServerLoaded = chosen.isLoaded;
                }

                if (exact != null && exact.dataPresent && exact.hashMatchesData)
                {
                    row.State = NavigationArtifactSyncState.InSync;
                    if (!serverReachable)
                    {
                        row.Details = exact.isActive
                            ? "The file is in the server folder and marked active."
                            : "The file is in the server folder but another manifest is active.";
                    }
                    else
                    {
                        row.Details = exact.isLoaded
                            ? "The server loaded exactly this artifact."
                            : "The file is on the server but a different one is loaded (not active).";
                    }
                }
                else if (exact != null)
                {
                    row.State = NavigationArtifactSyncState.Broken;
                    row.Details = string.IsNullOrEmpty(exact.error)
                        ? "The .navmesh.bytes for this hash is missing or corrupted on the server."
                        : exact.error;
                }
                else if (chosen != null)
                {
                    row.State = NavigationArtifactSyncState.ServerOutdated;
                    row.Details =
                        $"The server has a different version of the level: {Short(chosen.artifactHash)} " +
                        $"({chosen.polygonCount} polygons). Press Export for Server.";
                }
                else
                {
                    row.State = NavigationArtifactSyncState.MissingOnServer;
                    row.Details = "The level is missing on the server. Press Export for Server.";
                }

                rows.Add(row);
            }

            for (int s = 0; s < serverArtifacts.Length; s++)
            {
                NavigationServerEditorClient.ServerArtifact server = serverArtifacts[s];
                if (matchedServerArtifacts.Contains(server))
                {
                    continue;
                }

                bool levelKnownToClient = false;
                for (int i = 0; i < clientArtifacts.Count; i++)
                {
                    if (string.Equals(clientArtifacts[i].LevelId, server.levelId, StringComparison.Ordinal))
                    {
                        levelKnownToClient = true;
                        break;
                    }
                }

                if (levelKnownToClient)
                {
                    // An old version of an already known level - it is covered by that level row.
                    continue;
                }

                rows.Add(new NavigationArtifactComparison
                {
                    LevelId = server.levelId,
                    ServerArtifact = server,
                    ServerHash = server.artifactHash,
                    ServerPolygonCount = server.polygonCount,
                    ServerHasLevel = true,
                    ServerActive = server.isActive,
                    ServerLoaded = server.isLoaded,
                    State = NavigationArtifactSyncState.MissingInClient,
                    Details = "Server only: the client build has no such level."
                });
            }

            rows.Sort((a, b) => string.Compare(a.LevelId, b.LevelId, StringComparison.Ordinal));
            return rows;
        }

        /// <summary>Artifacts sitting in the local server folder - a fallback when the server is down.</summary>
        public static NavigationServerEditorClient.ArtifactsResponse ScanLocalServerFolder()
        {
            var response = new NavigationServerEditorClient.ArtifactsResponse
            {
                loadedLevelId = string.Empty,
                loadedArtifactHash = string.Empty,
                artifacts = Array.Empty<NavigationServerEditorClient.ServerArtifact>()
            };

            string folder = NavigationArtifactBuilder.ResolveServerFolder();
            response.dataDirectory = folder;
            if (!Directory.Exists(folder))
            {
                return response;
            }

            string activeHash = ReadHash(Path.Combine(folder, NavigationArtifactBuilder.ActiveManifestFileName));
            var artifacts = new List<NavigationServerEditorClient.ServerArtifact>();
            string[] manifestPaths = Directory.GetFiles(folder, "*.manifest.json", SearchOption.TopDirectoryOnly);
            Array.Sort(manifestPaths, StringComparer.Ordinal);

            for (int i = 0; i < manifestPaths.Length; i++)
            {
                string fileName = Path.GetFileName(manifestPaths[i]);
                if (string.Equals(
                        fileName,
                        NavigationArtifactBuilder.ActiveManifestFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var manifest = ReadManifest(manifestPaths[i]);
                if (manifest == null)
                {
                    continue;
                }

                bool dataPresent = File.Exists(Path.Combine(folder, manifest.fileName));
                bool hashMatches = dataPresent
                                   && string.Equals(
                                       ComputeSha256(Path.Combine(folder, manifest.fileName)),
                                       manifest.artifactHash,
                                       StringComparison.OrdinalIgnoreCase);
                artifacts.Add(new NavigationServerEditorClient.ServerArtifact
                {
                    levelId = manifest.levelId,
                    description = manifest.description,
                    artifactHash = manifest.artifactHash,
                    schemaVersion = manifest.schemaVersion,
                    dotRecastVersion = manifest.dotRecastVersion,
                    agentProfileId = manifest.agentProfileId,
                    polygonCount = manifest.polygonCount,
                    sourceMeshCount = manifest.sourceMeshCount,
                    fileName = manifest.fileName,
                    dataPresent = dataPresent,
                    hashMatchesData = hashMatches,
                    isActive = string.Equals(
                        manifest.artifactHash,
                        activeHash,
                        StringComparison.OrdinalIgnoreCase),
                    // The server was not queried, so what it actually loaded is unknown here.
                    isLoaded = false,
                    error = dataPresent
                        ? (hashMatches ? string.Empty : "The file SHA-256 does not match the manifest.")
                        : "There is no .navmesh.bytes next to the manifest."
                });
            }

            response.artifacts = artifacts.ToArray();
            response.loadedArtifactHash = activeHash;
            return response;
        }

        public static string Short(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return "—";
            }

            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }

        private static string ComputeSha256(string filePath)
        {
            try
            {
                using var sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(File.ReadAllBytes(filePath));
                var builder = new System.Text.StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return builder.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string ReadHash(string manifestPath)
        {
            var manifest = ReadManifest(manifestPath);
            return manifest != null ? manifest.artifactHash : string.Empty;
        }

        private static NavigationArtifactBuilder.NavigationArtifactManifest ReadManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<NavigationArtifactBuilder.NavigationArtifactManifest>(
                    File.ReadAllText(manifestPath));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
