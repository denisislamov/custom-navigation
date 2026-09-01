using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CustomNavigation.Authoring;
using DotRecast.Detour;
using DotRecast.Detour.Io;

namespace CustomNavigation.Runtime
{
    public sealed class NavigationArtifactInstance
    {
        public string LevelId { get; }
        public string ArtifactHash { get; }
        public string AgentProfileId { get; }
        public int PolygonCount { get; }
        public DtNavMesh NavMesh { get; }

        internal NavigationArtifactInstance(
            string levelId,
            string artifactHash,
            string agentProfileId,
            int polygonCount,
            DtNavMesh navMesh)
        {
            LevelId = levelId;
            ArtifactHash = artifactHash;
            AgentProfileId = agentProfileId;
            PolygonCount = polygonCount;
            NavMesh = navMesh;
        }

        public DtNavMeshQuery CreateQuery()
        {
            return new DtNavMeshQuery(NavMesh);
        }
    }

    public static class NavigationArtifactLoader
    {
        public const string SupportedSchemaVersion = NavigationCompatibilityContract.ArtifactSchemaVersion;
        public const string SupportedDotRecastVersion = NavigationCompatibilityContract.DotRecastVersion;

        public static NavigationArtifactInstance Load(NavigationArtifactAsset artifact)
        {
            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            NavigationCompatibilityContract.ValidateArtifact(
                artifact.SchemaVersion,
                artifact.DotRecastVersion,
                artifact.Precision,
                artifact.CanonicalJitterAssemblySha256,
                artifact.DeterministicMathCompatibilityId,
                artifact.FingerprintAlgorithmVersion,
                artifact.FingerprintAlgorithmId);

            if (artifact.NavigationData == null)
            {
                throw new InvalidOperationException(
                    $"Navigation artifact '{artifact.name}' has no binary data.");
            }

            byte[] bytes = artifact.NavigationData.bytes;
            return LoadBytes(
                artifact.LevelId,
                artifact.ArtifactHash,
                artifact.AgentProfileId,
                artifact.PolygonCount,
                bytes);
        }

        public static NavigationArtifactInstance LoadBytes(
            string levelId,
            string expectedHash,
            string agentProfileId,
            int expectedPolygonCount,
            byte[] bytes)
        {
            CanonicalJitterContract.ValidateLoadedAssembly();

            if (bytes == null || bytes.Length == 0)
            {
                throw new InvalidDataException("Navigation artifact binary is empty.");
            }

            string actualHash = ComputeSha256(bytes);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Navigation artifact hash mismatch: expected {expectedHash}, got {actualHash}.");
            }

            DtNavMesh navMesh;
            using (var memory = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(memory))
            {
                navMesh = new DtMeshSetReader().Read(reader);
            }

            int polygonCount = CountPolygons(navMesh);
            if (polygonCount <= 0)
            {
                throw new InvalidDataException("Navigation artifact contains no polygons.");
            }

            if (expectedPolygonCount > 0 && polygonCount != expectedPolygonCount)
            {
                throw new InvalidDataException(
                    $"Navigation polygon count mismatch: metadata={expectedPolygonCount}, binary={polygonCount}.");
            }

            return new NavigationArtifactInstance(
                levelId,
                actualHash,
                agentProfileId,
                polygonCount,
                navMesh);
        }

        public static string ComputeSha256(byte[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            using SHA256 algorithm = SHA256.Create();
            byte[] hash = algorithm.ComputeHash(data);
            var builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static int CountPolygons(DtNavMesh navMesh)
        {
            int count = 0;
            for (int tileIndex = 0; tileIndex < navMesh.GetMaxTiles(); tileIndex++)
            {
                DtMeshTile tile = navMesh.GetTile(tileIndex);
                if (tile?.data?.header != null)
                {
                    count += tile.data.header.polyCount;
                }
            }

            return count;
        }
    }
}
