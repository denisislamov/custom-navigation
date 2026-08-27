using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CustomNavigation.Authoring;
using CustomNavigation.Editor.Api;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomNavigation.Editor.Tests
{
    /// <summary>Consumer-facing contract used by an NPI editor adapter or standalone tooling.</summary>
    public sealed class NavigationEditorApiTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private readonly List<string> generatedLevelIds = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            }
            spawned.Clear();

            for (int i = 0; i < generatedLevelIds.Count; i++)
            {
                string id = generatedLevelIds[i];
                AssetDatabase.DeleteAsset(NavigationArtifactBuilder.GetClientAssetPath(id));
                AssetDatabase.DeleteAsset(NavigationArtifactBuilder.GetClientDataPath(id));
                AssetDatabase.DeleteAsset(NavigationArtifactBuilder.GetClientManifestPath(id));
            }
            generatedLevelIds.Clear();
            AssetDatabase.Refresh();
        }

        [Test]
        public void StandaloneValidationUsesSerializedLevelId()
        {
            NavigationLevel level = CreateLevel("cn06_standalone", "standalone_source");

            NavigationEditorResult result = NavigationEditorApi.Validate(level);

            Assert.That(result.Succeeded, Is.True, Format(result));
            Assert.That(result.Status, Is.EqualTo(NavigationEditorResultStatus.Valid));
            Assert.That(result.Ownership, Is.EqualTo(NavigationLevelIdOwnership.Standalone));
            Assert.That(result.LevelId, Is.EqualTo(level.LevelId));
        }

        [Test]
        public void ExternalManagedIdIsExplicitAndDoesNotMutateStandaloneId()
        {
            NavigationLevel level = CreateLevel("cn06_standalone_owner", "managed_source");

            NavigationEditorResult result = NavigationEditorApi.Validate(
                level,
                NavigationLevelIdBinding.External("NPI", "cn06_managed"));

            Assert.That(result.Succeeded, Is.True, Format(result));
            Assert.That(result.LevelId, Is.EqualTo("cn06_managed"));
            Assert.That(result.Owner, Is.EqualTo("NPI"));
            Assert.That(result.Ownership, Is.EqualTo(NavigationLevelIdOwnership.ExternalManaged));
            Assert.That(level.LevelId, Is.EqualTo("cn06_standalone_owner"));
        }

        [TestCase("", "cn06_managed")]
        [TestCase("NPI", "Managed Level")]
        public void InvalidExternalBindingIsRejectedWithoutMutation(string owner, string managedLevelId)
        {
            NavigationLevel level = CreateLevel("cn06_original", "invalid_binding_source");

            NavigationEditorResult result = NavigationEditorApi.Validate(
                level,
                NavigationLevelIdBinding.External(owner, managedLevelId));

            Assert.That(result.Status, Is.EqualTo(NavigationEditorResultStatus.Failed));
            Assert.That(result.Succeeded, Is.False);
            Assert.That(level.LevelId, Is.EqualTo("cn06_original"));
        }

        [Test]
        public void ConflictingManagedIdBlocksBakeBeforeAnyArtifactWrite()
        {
            const string conflictId = "cn06_conflict";
            NavigationLevel level = CreateLevel("cn06_first", "first_source");
            CreateLevel(conflictId, "second_source");
            generatedLevelIds.Add(conflictId);

            NavigationEditorResult result = NavigationEditorApi.Bake(
                level,
                NavigationLevelIdBinding.External("NPI", conflictId));

            Assert.That(result.Status, Is.EqualTo(NavigationEditorResultStatus.Failed));
            Assert.That(Format(result), Does.Contain("Duplicate Navigation Level ID"));
            Assert.That(level.LevelId, Is.EqualTo("cn06_first"));
            Assert.That(File.Exists(NavigationArtifactBuilder.GetClientDataPath(conflictId)), Is.False);
            Assert.That(File.Exists(NavigationArtifactBuilder.GetClientManifestPath(conflictId)), Is.False);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(
                NavigationArtifactBuilder.GetClientAssetPath(conflictId)), Is.Null);
        }

        [Test]
        public void ManagedBakeAndReadOnlySummaryExposeVerifiedDelivery()
        {
            const string managedId = "cn06_delivery";
            generatedLevelIds.Add(managedId);
            NavigationLevel level = CreateLevel("cn06_delivery_standalone", "delivery_source");
            NavigationLevelIdBinding binding = NavigationLevelIdBinding.External("NPI", managedId);

            NavigationEditorResult baked = NavigationEditorApi.Bake(level, binding);
            NavigationEditorResult summary = NavigationEditorApi.ReadSummary(level, binding);

            Assert.That(baked.Succeeded, Is.True, Format(baked));
            Assert.That(baked.Status, Is.EqualTo(NavigationEditorResultStatus.Ready));
            Assert.That(summary.Status,
                Is.EqualTo(NavigationEditorResultStatus.Ready)
                    .Or.EqualTo(NavigationEditorResultStatus.Changed),
                Format(summary));
            Assert.That(summary.LevelId, Is.EqualTo(managedId));
            Assert.That(summary.ArtifactPath, Does.EndWith("cn06_delivery.navigation.asset"));
            Assert.That(summary.PayloadPath, Does.EndWith("cn06_delivery.navigation.bytes"));
            Assert.That(summary.ManifestPath, Does.EndWith("cn06_delivery.navigation.manifest.json"));
            Assert.That(summary.Digest, Is.EqualTo(baked.Digest));
            Assert.That(summary.Digest, Has.Length.EqualTo(64));
            Assert.That(summary.PayloadSize, Is.GreaterThan(0));
            Assert.That(summary.PolygonCount, Is.GreaterThan(0));
            Assert.That(summary.SourceMeshCount, Is.EqualTo(1));
            Assert.That(summary.Artifact, Is.SameAs(baked.Artifact));
            Assert.That(level.LevelId, Is.EqualTo("cn06_delivery_standalone"));
        }

        [Test]
        public void PreviewApiReadsWithoutNotificationAndSharesOverlayPreferences()
        {
            NavigationPreviewState previous = NavigationPreviewApi.Current;
            int notifications = 0;
            Action handler = () => notifications++;
            NavigationPreviewApi.Changed += handler;
            try
            {
                NavigationPreviewState read = NavigationPreviewApi.Current;
                Assert.That(notifications, Is.Zero, "Reading preview state must have no side effects.");

                var requested = new NavigationPreviewState(
                    !read.Sources,
                    !read.Baked,
                    !read.Runtime,
                    NavigationPreviewScope.AllLoadedLevels,
                    NavigationPreviewDepth.XRay);
                NavigationPreviewApi.Apply(requested);

                Assert.That(notifications, Is.EqualTo(1));
                Assert.That(NavigationHighlightSettings.SourcesEnabled, Is.EqualTo(requested.Sources));
                Assert.That(NavigationHighlightSettings.BakedEnabled, Is.EqualTo(requested.Baked));
                Assert.That(NavigationHighlightSettings.RuntimeEnabled, Is.EqualTo(requested.Runtime));
                Assert.That(NavigationHighlightSettings.Scope, Is.EqualTo(requested.Scope));
                Assert.That(NavigationHighlightSettings.Depth, Is.EqualTo(requested.Depth));
            }
            finally
            {
                NavigationPreviewApi.Changed -= handler;
                NavigationPreviewApi.Apply(previous);
            }
        }

        [Test]
        public void EditorApiAssemblyHasNoNpiPhysicsOrEftDependency()
        {
            Assembly assembly = typeof(NavigationEditorApi).Assembly;
            Assert.That(assembly, Is.EqualTo(typeof(NavigationBakeCommand).Assembly));
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name, Does.Not.Contain("NPI").IgnoreCase);
                Assert.That(reference.Name, Does.Not.Contain("Physics").IgnoreCase);
                Assert.That(reference.Name, Does.Not.Contain("EFT").IgnoreCase);
            }
        }

        private NavigationLevel CreateLevel(string levelId, string sourceName)
        {
            var levelObject = new GameObject("Level " + sourceName);
            spawned.Add(levelObject);
            NavigationLevel level = levelObject.AddComponent<NavigationLevel>();
            SetLevelId(level, levelId);

            var agent = ScriptableObject.CreateInstance<NavigationAgentProfile>();
            var areas = ScriptableObject.CreateInstance<NavigationAreaCatalog>();
            var performance = ScriptableObject.CreateInstance<NavigationPerformanceProfile>();
            spawned.Add(agent);
            spawned.Add(areas);
            spawned.Add(performance);
            areas.ResetToDefaults();
            level.ConfigureDefaults(agent, areas, performance);

            var sourceObject = new GameObject(sourceName);
            spawned.Add(sourceObject);
            sourceObject.transform.SetParent(levelObject.transform, false);
            MeshFilter filter = sourceObject.AddComponent<MeshFilter>();
            var mesh = new Mesh { name = sourceName + " mesh" };
            spawned.Add(mesh);
            mesh.vertices = new[]
            {
                new Vector3(-5f, 0f, -5f),
                new Vector3(5f, 0f, -5f),
                new Vector3(5f, 0f, 5f),
                new Vector3(-5f, 0f, 5f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            sourceObject.AddComponent<NavigationGeometrySource>();
            return level;
        }

        private static void SetLevelId(NavigationLevel level, string levelId)
        {
            var serialized = new SerializedObject(level);
            serialized.FindProperty("levelId").stringValue = levelId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string Format(NavigationEditorResult result)
        {
            var messages = new List<string>();
            for (int i = 0; i < result.Issues.Count; i++) messages.Add(result.Issues[i].Message);
            return string.Join("\n", messages);
        }
    }
}
