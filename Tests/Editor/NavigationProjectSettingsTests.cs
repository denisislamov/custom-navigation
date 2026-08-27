using System;
using System.IO;
using CustomNavigation.Authoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomNavigation.Editor.Tests
{
    public sealed class NavigationProjectSettingsTests
    {
        private const string TestRoot = "Assets/__CustomNavigationProjectSettingsTests";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestRoot);
            AssetDatabase.Refresh();
        }

        [Test]
        public void ProvidersUseSeparateProjectAndUserPaths()
        {
            SettingsProvider project = NavigationSettingsProviders.CreateProjectProvider();
            SettingsProvider preferences = NavigationSettingsProviders.CreatePreferencesProvider();

            Assert.That(project.settingsPath,
                Is.EqualTo("Project/DataSakura/Custom Navigation"));
            Assert.That(project.scope, Is.EqualTo(SettingsScope.Project));
            Assert.That(preferences.settingsPath,
                Is.EqualTo("Preferences/DataSakura/Custom Navigation/Scene Preview"));
            Assert.That(preferences.scope, Is.EqualTo(SettingsScope.User));
            Assert.That(
                NavigationHighlightSettings.EnabledPreferenceKey,
                Is.Not.EqualTo(NavigationProjectSettings.SettingsAssetPath));
        }

        [Test]
        public void CreatingProviderDescriptorsDoesNotCreateFiles()
        {
            bool settingsExisted = File.Exists(NavigationProjectSettings.SettingsAssetPath);
            string[] assetsBefore = AssetDatabase.GetAllAssetPaths();

            NavigationSettingsProviders.CreateProjectProvider();
            NavigationSettingsProviders.CreatePreferencesProvider();

            Assert.That(File.Exists(NavigationProjectSettings.SettingsAssetPath),
                Is.EqualTo(settingsExisted));
            Assert.That(AssetDatabase.GetAllAssetPaths(), Is.EqualTo(assetsBefore));
        }

        [Test]
        public void LoadingProjectSettingsDoesNotCreateItsSettingsFile()
        {
            bool settingsExisted = File.Exists(NavigationProjectSettings.SettingsAssetPath);

            _ = NavigationProjectSettings.instance;

            Assert.That(File.Exists(NavigationProjectSettings.SettingsAssetPath),
                Is.EqualTo(settingsExisted));
        }

        [Test]
        public void CreateMissingDefaultsPreservesExistingProfileAndIsIdempotent()
        {
            NavigationProjectSettings settings =
                ScriptableObject.CreateInstance<NavigationProjectSettings>();
            try
            {
                NavigationProjectSettings.EnsureAssetFolder(TestRoot);
                var existingAgent = ScriptableObject.CreateInstance<NavigationAgentProfile>();
                AssetDatabase.CreateAsset(existingAgent, TestRoot + "/ExistingAgent.asset");

                var serialized = new SerializedObject(settings);
                serialized.Update();
                serialized.FindProperty("defaultAgentProfile").objectReferenceValue = existingAgent;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var first = settings.CreateMissingDefaults(TestRoot, false);
                NavigationAgentProfile firstAgent = settings.DefaultAgentProfile;
                NavigationAreaCatalog firstAreas = settings.DefaultAreaCatalog;
                NavigationPerformanceProfile firstPerformance = settings.DefaultPerformanceProfile;
                var second = settings.CreateMissingDefaults(TestRoot, false);

                Assert.That(first, Has.Count.EqualTo(2));
                Assert.That(second, Is.Empty);
                Assert.That(firstAgent, Is.SameAs(existingAgent));
                Assert.That(settings.DefaultAgentProfile, Is.SameAs(firstAgent));
                Assert.That(settings.DefaultAreaCatalog, Is.SameAs(firstAreas));
                Assert.That(settings.DefaultPerformanceProfile, Is.SameAs(firstPerformance));
                Assert.That(firstAreas.Areas, Is.Not.Empty);
                Assert.That(firstPerformance.DeviceTier,
                    Is.EqualTo(NavigationDeviceTier.MobileMedium));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ApplyingDefaultsPreservesExistingProfilesAndSupportsUndo()
        {
            NavigationProjectSettings settings =
                ScriptableObject.CreateInstance<NavigationProjectSettings>();
            var root = new GameObject("CN-02 settings test");
            try
            {
                NavigationProjectSettings.EnsureAssetFolder(TestRoot);
                NavigationAgentProfile existingAgent = CreateAsset<NavigationAgentProfile>(
                    TestRoot + "/ExistingAgent.asset");
                NavigationAgentProfile defaultAgent = CreateAsset<NavigationAgentProfile>(
                    TestRoot + "/DefaultAgent.asset");
                NavigationAreaCatalog defaultAreas = CreateAsset<NavigationAreaCatalog>(
                    TestRoot + "/DefaultAreas.asset");
                NavigationPerformanceProfile defaultPerformance =
                    CreateAsset<NavigationPerformanceProfile>(TestRoot + "/DefaultBudget.asset");

                var settingsObject = new SerializedObject(settings);
                settingsObject.Update();
                settingsObject.FindProperty("defaultAgentProfile").objectReferenceValue = defaultAgent;
                settingsObject.FindProperty("defaultAreaCatalog").objectReferenceValue = defaultAreas;
                settingsObject.FindProperty("defaultPerformanceProfile").objectReferenceValue =
                    defaultPerformance;
                settingsObject.FindProperty("defaultBuildSettings.quality").enumValueIndex =
                    (int)NavigationBakeQuality.HighDetail;
                settingsObject.ApplyModifiedPropertiesWithoutUndo();

                NavigationLevel level = root.AddComponent<NavigationLevel>();
                level.ConfigureDefaults(existingAgent, null, null);
                settings.ApplyDefaultsTo(level, false);

                Assert.That(level.DefaultAgentProfile, Is.SameAs(existingAgent));
                Assert.That(level.AreaCatalog, Is.SameAs(defaultAreas));
                Assert.That(level.PerformanceProfile, Is.SameAs(defaultPerformance));
                Assert.That(level.BuildSettings.Quality,
                    Is.EqualTo(NavigationBakeQuality.HighDetail));

                settings.ApplyDefaultsTo(level, true);
                Assert.That(level.DefaultAgentProfile, Is.SameAs(defaultAgent));
                Undo.PerformUndo();
                Assert.That(level.DefaultAgentProfile, Is.SameAs(existingAgent));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void SharedProfileUsageListsBothLoadedLevels()
        {
            NavigationProjectSettings.EnsureAssetFolder(TestRoot);
            NavigationAgentProfile shared = CreateAsset<NavigationAgentProfile>(
                TestRoot + "/SharedAgent.asset");
            var firstObject = new GameObject("First Navigation Level");
            var secondObject = new GameObject("Second Navigation Level");
            try
            {
                firstObject.AddComponent<NavigationLevel>().ConfigureDefaults(shared, null, null);
                secondObject.AddComponent<NavigationLevel>().ConfigureDefaults(shared, null, null);

                var usages = NavigationProfileUsage.Find(shared);

                Assert.That(usages, Has.Some.Contains("First Navigation Level"));
                Assert.That(usages, Has.Some.Contains("Second Navigation Level"));
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void MakeLocalCopyPreservesValuesWithoutMutatingSharedProfile()
        {
            NavigationProjectSettings.EnsureAssetFolder(TestRoot);
            NavigationPerformanceProfile shared = CreateAsset<NavigationPerformanceProfile>(
                TestRoot + "/SharedBudget.asset");
            shared.ApplyStartingPreset(NavigationDeviceTier.MobileHigh);

            NavigationPerformanceProfile copy = NavigationProfileAssets.MakeLocalCopy(
                shared,
                TestRoot + "/LocalBudget.asset");

            Assert.That(copy, Is.Not.SameAs(shared));
            Assert.That(copy.DeviceTier, Is.EqualTo(shared.DeviceTier));
            Assert.That(copy.FrameBudgetMilliseconds,
                Is.EqualTo(shared.FrameBudgetMilliseconds));

            copy.ApplyStartingPreset(NavigationDeviceTier.MobileLow);
            Assert.That(shared.DeviceTier, Is.EqualTo(NavigationDeviceTier.MobileHigh));
            Assert.That(copy.DeviceTier, Is.EqualTo(NavigationDeviceTier.MobileLow));
        }

        [Test]
        public void ApplyingDefaultsToPrefabInstanceRecordsOverrides()
        {
            NavigationProjectSettings.EnsureAssetFolder(TestRoot);
            NavigationAgentProfile defaultAgent = CreateAsset<NavigationAgentProfile>(
                TestRoot + "/PrefabDefaultAgent.asset");
            NavigationAreaCatalog defaultAreas = CreateAsset<NavigationAreaCatalog>(
                TestRoot + "/PrefabDefaultAreas.asset");
            NavigationPerformanceProfile defaultPerformance =
                CreateAsset<NavigationPerformanceProfile>(TestRoot + "/PrefabDefaultBudget.asset");
            NavigationProjectSettings settings =
                ScriptableObject.CreateInstance<NavigationProjectSettings>();
            var prefabSource = new GameObject("Navigation Prefab");
            GameObject instance = null;
            try
            {
                prefabSource.AddComponent<NavigationLevel>();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(
                    prefabSource,
                    TestRoot + "/NavigationLevel.prefab");
                Object.DestroyImmediate(prefabSource);
                prefabSource = null;
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

                var settingsObject = new SerializedObject(settings);
                settingsObject.Update();
                settingsObject.FindProperty("defaultAgentProfile").objectReferenceValue = defaultAgent;
                settingsObject.FindProperty("defaultAreaCatalog").objectReferenceValue = defaultAreas;
                settingsObject.FindProperty("defaultPerformanceProfile").objectReferenceValue =
                    defaultPerformance;
                settingsObject.ApplyModifiedPropertiesWithoutUndo();

                NavigationLevel level = instance.GetComponent<NavigationLevel>();
                settings.ApplyDefaultsTo(level, true);

                PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(level);
                Assert.That(Array.Exists(modifications,
                    modification => modification.propertyPath == "defaultAgentProfile"), Is.True);
                Assert.That(Array.Exists(modifications,
                    modification => modification.propertyPath == "areaCatalog"), Is.True);
                Assert.That(Array.Exists(modifications,
                    modification => modification.propertyPath == "performanceProfile"), Is.True);
            }
            finally
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }

                if (prefabSource != null)
                {
                    Object.DestroyImmediate(prefabSource);
                }

                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void RuntimeBudgetChangesAndReplacementDoNotChangeBakedGeometry()
        {
            var root = new GameObject("CN-02 deterministic bake");
            var floor = new GameObject("Floor");
            Mesh mesh = null;
            NavigationAgentProfile agent = null;
            NavigationAreaCatalog areas = null;
            NavigationPerformanceProfile originalBudget = null;
            NavigationPerformanceProfile replacementBudget = null;
            NavigationArtifactAsset artifact = null;
            try
            {
                floor.transform.SetParent(root.transform, false);
                mesh = new Mesh { name = "CN-02 floor" };
                mesh.vertices = new[]
                {
                    new Vector3(-5f, 0f, -5f),
                    new Vector3(5f, 0f, -5f),
                    new Vector3(5f, 0f, 5f),
                    new Vector3(-5f, 0f, 5f)
                };
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                mesh.RecalculateBounds();
                floor.AddComponent<MeshFilter>().sharedMesh = mesh;
                floor.AddComponent<NavigationGeometrySource>();

                NavigationLevel level = root.AddComponent<NavigationLevel>();
                var levelObject = new SerializedObject(level);
                levelObject.Update();
                levelObject.FindProperty("levelId").stringValue = "cn02_bake_test";
                levelObject.ApplyModifiedPropertiesWithoutUndo();

                agent = ScriptableObject.CreateInstance<NavigationAgentProfile>();
                areas = ScriptableObject.CreateInstance<NavigationAreaCatalog>();
                areas.ResetToDefaults();
                originalBudget = ScriptableObject.CreateInstance<NavigationPerformanceProfile>();
                originalBudget.ApplyStartingPreset(NavigationDeviceTier.MobileLow);
                level.ConfigureDefaults(agent, areas, originalBudget);

                NavigationArtifactBuildResult baseline =
                    NavigationArtifactBuilder.BuildForClient(level);
                artifact = baseline.Asset;

                var budgetObject = new SerializedObject(originalBudget);
                budgetObject.Update();
                budgetObject.FindProperty("frameBudgetMilliseconds").floatValue = 3.5f;
                budgetObject.FindProperty("maximumQueuedQueries").intValue = 127;
                budgetObject.ApplyModifiedPropertiesWithoutUndo();
                NavigationArtifactBuildResult changedBudget =
                    NavigationArtifactBuilder.BuildForClient(level);

                replacementBudget = ScriptableObject.CreateInstance<NavigationPerformanceProfile>();
                replacementBudget.ApplyStartingPreset(NavigationDeviceTier.MobileHigh);
                level.ConfigureDefaults(agent, areas, replacementBudget);
                NavigationArtifactBuildResult replacement =
                    NavigationArtifactBuilder.BuildForClient(level);

                Assert.That(changedBudget.Hash, Is.EqualTo(baseline.Hash));
                Assert.That(changedBudget.Data, Is.EqualTo(baseline.Data));
                Assert.That(replacement.Hash, Is.EqualTo(baseline.Hash));
                Assert.That(replacement.Data, Is.EqualTo(baseline.Data));
            }
            finally
            {
                if (artifact != null)
                {
                    NavigationArtifactBuilder.DeleteClientArtifact(artifact);
                }

                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(agent);
                Object.DestroyImmediate(areas);
                Object.DestroyImmediate(originalBudget);
                Object.DestroyImmediate(replacementBudget);
            }
        }

        private static T CreateAsset<T>(string path) where T : ScriptableObject
        {
            T asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
