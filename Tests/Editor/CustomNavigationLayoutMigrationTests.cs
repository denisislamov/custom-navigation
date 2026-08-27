using System;
using System.IO;
using CustomNavigation.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using PackageManagerInfo = UnityEditor.PackageManager.PackageInfo;

namespace CustomNavigation.Editor.Tests
{
    public sealed class CustomNavigationLayoutMigrationTests
    {
        private const string TestRoot = "Assets/__CustomNavigationLayoutMigrationTests";
        private const string LegacyRoot = TestRoot + "/CustomNavigation";
        private const string CurrentRoot = TestRoot + "/DataSakura/CustomNavigation";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void PackageManifestUsesUnifiedDisplayNameAndNativeSample()
        {
            PackageManagerInfo package = PackageManagerInfo.FindForAssembly(
                typeof(CustomNavigationLayoutMigration).Assembly);
            string json = File.ReadAllText(Path.Combine(package.resolvedPath, "package.json"));
            PackageManifest manifest = JsonUtility.FromJson<PackageManifest>(json);

            Assert.That(manifest.name, Is.EqualTo("com.datasakura.custom-navigation"));
            Assert.That(manifest.version, Is.EqualTo("0.6.10"));
            Assert.That(manifest.displayName, Is.EqualTo("DataSakura Custom Navigation"));
            Assert.That(manifest.samples, Has.Length.EqualTo(1));
            Assert.That(manifest.samples[0].displayName, Is.EqualTo("Navigation Demos & Bots"));
            Assert.That(manifest.samples[0].path, Is.EqualTo("Samples~/Demos"));
            Assert.That(
                Directory.Exists(Path.Combine(package.resolvedPath, manifest.samples[0].path)),
                Is.True);
        }

        [Test]
        public void FreshProjectIsAnIdempotentNoOp()
        {
            CustomNavigationLayoutMigrationResult first =
                CustomNavigationLayoutMigration.Migrate(LegacyRoot, CurrentRoot);
            CustomNavigationLayoutMigrationResult second =
                CustomNavigationLayoutMigration.Migrate(LegacyRoot, CurrentRoot);

            Assert.That(first.Succeeded, Is.True);
            Assert.That(second.Succeeded, Is.True);
            Assert.That(AssetDatabase.IsValidFolder(LegacyRoot), Is.False);
            Assert.That(AssetDatabase.IsValidFolder(CurrentRoot), Is.False);
        }

        [Test]
        public void UpgradeMovesRootAndRenamesSceneFolderWithoutChangingGuids()
        {
            CreateFolder(LegacyRoot + "/Scene");
            string rootGuid = AssetDatabase.AssetPathToGUID(LegacyRoot);
            string scenesGuid = AssetDatabase.AssetPathToGUID(LegacyRoot + "/Scene");

            CustomNavigationLayoutMigrationResult result =
                CustomNavigationLayoutMigration.Migrate(LegacyRoot, CurrentRoot);

            Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Messages));
            Assert.That(AssetDatabase.IsValidFolder(LegacyRoot), Is.False);
            Assert.That(AssetDatabase.AssetPathToGUID(CurrentRoot), Is.EqualTo(rootGuid));
            Assert.That(
                AssetDatabase.AssetPathToGUID(CurrentRoot + "/Scenes"),
                Is.EqualTo(scenesGuid));

            CustomNavigationLayoutMigrationResult repeated =
                CustomNavigationLayoutMigration.Migrate(LegacyRoot, CurrentRoot);
            Assert.That(repeated.Succeeded, Is.True, string.Join("\n", repeated.Messages));
        }

        [Test]
        public void ExistingDestinationRefusesToMergeBeforeMutation()
        {
            CreateFolder(LegacyRoot);
            CreateFolder(CurrentRoot);

            CustomNavigationLayoutMigrationResult result =
                CustomNavigationLayoutMigration.Migrate(LegacyRoot, CurrentRoot);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(AssetDatabase.IsValidFolder(LegacyRoot), Is.True);
            Assert.That(AssetDatabase.IsValidFolder(CurrentRoot), Is.True);
            Assert.That(result.Messages[0], Does.StartWith("Conflict:"));
        }

        [Test]
        public void PartiallyMigratedSceneFolderIsFinishedOnRepeat()
        {
            CreateFolder(CurrentRoot + "/Scene");
            string scenesGuid = AssetDatabase.AssetPathToGUID(CurrentRoot + "/Scene");

            CustomNavigationLayoutMigrationResult result =
                CustomNavigationLayoutMigration.Migrate(LegacyRoot, CurrentRoot);

            Assert.That(result.Succeeded, Is.True, string.Join("\n", result.Messages));
            Assert.That(AssetDatabase.IsValidFolder(CurrentRoot + "/Scene"), Is.False);
            Assert.That(
                AssetDatabase.AssetPathToGUID(CurrentRoot + "/Scenes"),
                Is.EqualTo(scenesGuid));
        }

        private static void CreateFolder(string path)
        {
            string[] parts = path.Split('/');
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

        [Serializable]
        private sealed class PackageManifest
        {
            public string name;
            public string version;
            public string displayName;
            public PackageSample[] samples;
        }

        [Serializable]
        private sealed class PackageSample
        {
            public string displayName;
            public string path;
        }
    }
}
