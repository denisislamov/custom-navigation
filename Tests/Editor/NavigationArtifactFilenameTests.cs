using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomNavigation.Editor.Tests
{
    public sealed class NavigationArtifactFilenameTests
    {
        private const string TestRoot = "Assets/__NavigationArtifactFilenameTests";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void CurrentNamesAreReadableStableAndDistinctPerLevel()
        {
            Assert.That(
                NavigationArtifactBuilder.GetClientDataPath("npi_multiplayer_test"),
                Is.EqualTo(
                    NavigationArtifactBuilder.GeneratedClientFolder +
                    "/npi_multiplayer_test.navigation.bytes"));
            Assert.That(
                NavigationArtifactBuilder.GetClientManifestPath("npi_multiplayer_test"),
                Does.EndWith("/npi_multiplayer_test.navigation.manifest.json"));
            Assert.That(
                NavigationArtifactBuilder.GetClientAssetPath("npi_multiplayer_test"),
                Does.EndWith("/npi_multiplayer_test.navigation.asset"));
            Assert.That(
                NavigationArtifactBuilder.GetClientDataPath("factory"),
                Is.Not.EqualTo(NavigationArtifactBuilder.GetClientDataPath("warehouse")));
            Assert.That(
                NavigationArtifactBuilder.GetManifestFileName("factory.navigation.bytes"),
                Is.EqualTo("factory.navigation.manifest.json"));
            Assert.That(
                NavigationArtifactBuilder.GetManifestFileName(
                    "factory.0123456789ab.navmesh.bytes"),
                Is.EqualTo("factory.0123456789ab.manifest.json"));
            Assert.That(
                NavigationArtifactBuilder.IsSupportedPayloadFileName("factory.navigation.bytes"),
                Is.True);
            Assert.That(
                NavigationArtifactBuilder.IsSupportedPayloadFileName(
                    "factory.0123456789ab.navmesh.bytes"),
                Is.True);
            Assert.That(
                NavigationArtifactBuilder.IsSupportedPayloadFileName("../factory.navigation.bytes"),
                Is.False);
            Assert.That(
                NavigationArtifactBuilder.IsSupportedPayloadFileName(".navigation.bytes"),
                Is.False);
        }

        [Test]
        public void LegacyArtifactStillLoadsBeforeMigration()
        {
            const string levelId = "cn05_legacy_load_test";
            string folder = NavigationArtifactBuilder.GeneratedClientFolder;
            string assetPath = NavigationArtifactBuilder.GetLegacyClientAssetPath(levelId);
            NavigationArtifactAsset artifact = null;
            try
            {
                EnsureFolder(folder);
                artifact = ScriptableObject.CreateInstance<NavigationArtifactAsset>();
                artifact.Configure(levelId, "hash", "1", "test",
                    NavigationCompatibilityContract.Precision,
                    NavigationCompatibilityContract.CanonicalJitterAssemblySha256,
                    NavigationCompatibilityContract.DeterministicMathCompatibilityId,
                    NavigationCompatibilityContract.FingerprintAlgorithmVersion,
                    NavigationCompatibilityContract.FingerprintAlgorithmId,
                    "agent", 1, 1, null, "{}");
                AssetDatabase.CreateAsset(artifact, assetPath);

                Assert.That(NavigationArtifactBuilder.LoadClientArtifact(levelId), Is.SameAs(artifact));
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void MigrationPreservesGuidsPayloadBytesAndReferencesAndIsIdempotent()
        {
            const string levelId = "cn05_migration";
            byte[] bytes = { 4, 8, 15, 16, 23, 42 };
            string hash = ComputeSha256(bytes);
            string stem = levelId + "." + hash.Substring(0, 12);
            string payloadPath = TestRoot + "/" + stem + NavigationArtifactBuilder.LegacyDataSuffix;
            string manifestPath = TestRoot + "/" + stem + ".manifest.json";
            string assetPath = TestRoot + "/" + levelId + NavigationArtifactBuilder.LegacyAssetSuffix;
            string targetPayload = TestRoot + "/" + levelId + NavigationArtifactBuilder.NavigationDataSuffix;
            string targetManifest = TestRoot + "/" + levelId + NavigationArtifactBuilder.NavigationManifestSuffix;
            string targetAsset = TestRoot + "/" + levelId + NavigationArtifactBuilder.NavigationAssetSuffix;

            EnsureFolder(TestRoot);
            File.WriteAllBytes(payloadPath, bytes);
            var manifest = new NavigationArtifactBuilder.NavigationArtifactManifest
            {
                schemaVersion = NavigationArtifactBuilder.SchemaVersion,
                dotRecastVersion = NavigationArtifactBuilder.DotRecastVersion,
                precision = NavigationCompatibilityContract.Precision,
                canonicalJitterAssemblySha256 = NavigationCompatibilityContract.CanonicalJitterAssemblySha256,
                deterministicMathCompatibilityId = NavigationCompatibilityContract.DeterministicMathCompatibilityId,
                fingerprintAlgorithmVersion = NavigationCompatibilityContract.FingerprintAlgorithmVersion,
                fingerprintAlgorithmId = NavigationCompatibilityContract.FingerprintAlgorithmId,
                levelId = levelId,
                artifactHash = hash,
                agentProfileId = "agent",
                polygonCount = 3,
                sourceMeshCount = 1,
                fileName = Path.GetFileName(payloadPath)
            };
            string manifestJson = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(manifestPath, manifestJson + "\n", new UTF8Encoding(false));
            AssetDatabase.ImportAsset(payloadPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceSynchronousImport);
            TextAsset payload = AssetDatabase.LoadAssetAtPath<TextAsset>(payloadPath);
            var artifact = ScriptableObject.CreateInstance<NavigationArtifactAsset>();
            artifact.Configure(levelId, hash, NavigationArtifactBuilder.SchemaVersion,
                NavigationArtifactBuilder.DotRecastVersion,
                NavigationCompatibilityContract.Precision,
                NavigationCompatibilityContract.CanonicalJitterAssemblySha256,
                NavigationCompatibilityContract.DeterministicMathCompatibilityId,
                NavigationCompatibilityContract.FingerprintAlgorithmVersion,
                NavigationCompatibilityContract.FingerprintAlgorithmId,
                "agent", 3, 1, payload, manifestJson);
            AssetDatabase.CreateAsset(artifact, assetPath);

            string payloadGuid = AssetDatabase.AssetPathToGUID(payloadPath);
            string manifestGuid = AssetDatabase.AssetPathToGUID(manifestPath);
            string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);

            NavigationArtifactFilenameMigrationResult first =
                NavigationArtifactFilenameMigration.Migrate(TestRoot);
            NavigationArtifactFilenameMigrationResult second =
                NavigationArtifactFilenameMigration.Migrate(TestRoot);

            Assert.That(first.Succeeded, Is.True, string.Join("\n", first.Messages));
            Assert.That(first.MigratedArtifactCount, Is.EqualTo(1));
            Assert.That(second.Succeeded, Is.True, string.Join("\n", second.Messages));
            Assert.That(second.MigratedArtifactCount, Is.Zero);
            Assert.That(AssetDatabase.AssetPathToGUID(targetPayload), Is.EqualTo(payloadGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(targetManifest), Is.EqualTo(manifestGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(targetAsset), Is.EqualTo(assetGuid));
            Assert.That(File.ReadAllBytes(targetPayload), Is.EqualTo(bytes));

            NavigationArtifactAsset migrated =
                AssetDatabase.LoadAssetAtPath<NavigationArtifactAsset>(targetAsset);
            Assert.That(AssetDatabase.GetAssetPath(migrated.NavigationData), Is.EqualTo(targetPayload));
            var migratedManifest =
                JsonUtility.FromJson<NavigationArtifactBuilder.NavigationArtifactManifest>(
                    migrated.ManifestJson);
            Assert.That(migratedManifest.fileName, Is.EqualTo(levelId + ".navigation.bytes"));
        }

        [Test]
        public void MigrationRejectsCorruptedPayloadBeforeMovingAnything()
        {
            const string levelId = "cn05_corrupt";
            string payloadPath = TestRoot + "/" + levelId + ".deadbeef0000.navmesh.bytes";
            string manifestPath = TestRoot + "/" + levelId + ".deadbeef0000.manifest.json";
            string assetPath = TestRoot + "/" + levelId + ".artifact.asset";
            EnsureFolder(TestRoot);
            File.WriteAllBytes(payloadPath, new byte[] { 1, 2, 3 });
            string manifestJson = JsonUtility.ToJson(
                new NavigationArtifactBuilder.NavigationArtifactManifest
                {
                    schemaVersion = "1",
                    dotRecastVersion = NavigationArtifactBuilder.DotRecastVersion,
                    levelId = levelId,
                    artifactHash = new string('0', 64),
                    agentProfileId = "agent",
                    polygonCount = 1,
                    sourceMeshCount = 1,
                    fileName = Path.GetFileName(payloadPath)
                },
                true);
            File.WriteAllText(manifestPath, manifestJson);
            AssetDatabase.ImportAsset(payloadPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceSynchronousImport);
            var artifact = ScriptableObject.CreateInstance<NavigationArtifactAsset>();
            artifact.Configure(levelId, new string('0', 64), "1",
                NavigationArtifactBuilder.DotRecastVersion,
                NavigationCompatibilityContract.Precision,
                NavigationCompatibilityContract.CanonicalJitterAssemblySha256,
                NavigationCompatibilityContract.DeterministicMathCompatibilityId,
                NavigationCompatibilityContract.FingerprintAlgorithmVersion,
                NavigationCompatibilityContract.FingerprintAlgorithmId,
                "agent", 1, 1,
                AssetDatabase.LoadAssetAtPath<TextAsset>(payloadPath), manifestJson);
            AssetDatabase.CreateAsset(artifact, assetPath);

            NavigationArtifactFilenameMigrationResult result =
                NavigationArtifactFilenameMigration.Migrate(TestRoot);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(File.Exists(payloadPath), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<NavigationArtifactAsset>(assetPath), Is.Not.Null);
            Assert.That(File.Exists(TestRoot + "/" + levelId + ".navigation.bytes"), Is.False);
        }

        [Test]
        public void ExportRollbackDoesNotLeaveAnIncompleteActivePair()
        {
            string folder = Path.Combine(Path.GetTempPath(), "cn05-export-" + Guid.NewGuid().ToString("N"));
            string dataPath = Path.Combine(folder, "level.navigation.bytes");
            string manifestPath = Path.Combine(folder, "level.navigation.manifest.json");
            string activePath = Path.Combine(folder, NavigationArtifactBuilder.ActiveManifestFileName);
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(dataPath, new byte[] { 1 });
                File.WriteAllText(manifestPath, "old manifest");
                File.WriteAllText(activePath, "old active");

                Assert.Throws<IOException>(() => NavigationArtifactBuilder.WriteServerFilesAtomically(
                    dataPath,
                    new byte[] { 9, 9 },
                    manifestPath,
                    "new manifest",
                    activePath,
                    index =>
                    {
                        if (index == 1)
                        {
                            throw new IOException("Injected export failure.");
                        }
                    }));

                Assert.That(File.ReadAllBytes(dataPath), Is.EqualTo(new byte[] { 1 }));
                Assert.That(File.ReadAllText(manifestPath), Is.EqualTo("old manifest"));
                Assert.That(File.ReadAllText(activePath), Is.EqualTo("old active"));
                Assert.That(Directory.GetFiles(folder, "*.tmp-*"), Is.Empty);
            }
            finally
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
        }

        [Test]
        public void ExportRejectsCorruptedPayloadBeforeWritingServerFiles()
        {
            EnsureFolder(TestRoot);
            string payloadPath = TestRoot + "/corrupt.navigation.bytes";
            File.WriteAllBytes(payloadPath, new byte[] { 1, 2, 3, 4 });
            AssetDatabase.ImportAsset(payloadPath, ImportAssetOptions.ForceSynchronousImport);
            var artifact = ScriptableObject.CreateInstance<NavigationArtifactAsset>();
            try
            {
                string manifestJson = JsonUtility.ToJson(
                    new NavigationArtifactBuilder.NavigationArtifactManifest
                    {
                        schemaVersion = NavigationArtifactBuilder.SchemaVersion,
                        dotRecastVersion = NavigationArtifactBuilder.DotRecastVersion,
                        levelId = "corrupt",
                        artifactHash = new string('f', 64),
                        agentProfileId = "agent",
                        polygonCount = 1,
                        sourceMeshCount = 1,
                        fileName = "corrupt.navigation.bytes"
                    });
                artifact.Configure(
                    "corrupt",
                    new string('f', 64),
                    NavigationArtifactBuilder.SchemaVersion,
                    NavigationArtifactBuilder.DotRecastVersion,
                    NavigationCompatibilityContract.Precision,
                    NavigationCompatibilityContract.CanonicalJitterAssemblySha256,
                    NavigationCompatibilityContract.DeterministicMathCompatibilityId,
                    NavigationCompatibilityContract.FingerprintAlgorithmVersion,
                    NavigationCompatibilityContract.FingerprintAlgorithmId,
                    "agent",
                    1,
                    1,
                    AssetDatabase.LoadAssetAtPath<TextAsset>(payloadPath),
                    manifestJson);

                Assert.That(
                    Assert.Throws<InvalidOperationException>(() =>
                        NavigationArtifactBuilder.ExportForServer(artifact)).Message,
                    Does.Contain("out of date"));
            }
            finally
            {
                Object.DestroyImmediate(artifact);
            }
        }

        [Test]
        public void DuplicateLoadedLevelIdsAreRejectedBeforeBuild()
        {
            var first = new GameObject("CN-05 duplicate first");
            var second = new GameObject("CN-05 duplicate second");
            try
            {
                NavigationLevel firstLevel = first.AddComponent<NavigationLevel>();
                NavigationLevel secondLevel = second.AddComponent<NavigationLevel>();
                SetLevelId(firstLevel, "same_level");
                SetLevelId(secondLevel, "same_level");

                Assert.That(
                    NavigationAuthoringValidator.Validate(firstLevel)
                        .Any(issue => issue.Message.Contains("Duplicate Navigation Level ID")),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        private static void SetLevelId(NavigationLevel level, string value)
        {
            var serialized = new SerializedObject(level);
            serialized.FindProperty("levelId").stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
