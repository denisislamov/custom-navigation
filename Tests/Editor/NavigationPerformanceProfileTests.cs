using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CustomNavigation.Authoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor.Tests
{
    public sealed class NavigationPerformanceProfileTests
    {
        [Test]
        public void PresetsRemainMobileOnlyWithoutAnUnmeasuredServerTier()
        {
            Assert.That(
                System.Enum.GetValues(typeof(NavigationDeviceTier)),
                Is.EqualTo(new[]
                {
                    NavigationDeviceTier.MobileLow,
                    NavigationDeviceTier.MobileMedium,
                    NavigationDeviceTier.MobileHigh,
                    NavigationDeviceTier.Custom
                }));
        }

        [Test]
        public void FieldClassificationCoversEverySerializedValue()
        {
            string[] classified = NavigationPerformanceProfileEditor.ActiveSchedulerFields
                .Concat(NavigationPerformanceProfileEditor.ActiveConsumerFields)
                .Concat(NavigationPerformanceProfileEditor.ReservedFields)
                .OrderBy(name => name)
                .ToArray();
            string[] serialized = typeof(NavigationPerformanceProfile)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(field => field.GetCustomAttribute<SerializeField>() != null)
                .Select(field => field.Name)
                .Where(name => name != "deviceTier")
                .OrderBy(name => name)
                .ToArray();

            Assert.That(classified, Is.EqualTo(serialized));
            Assert.That(classified.Distinct().Count(), Is.EqualTo(classified.Length));
        }

        [TestCase(NavigationDeviceTier.MobileLow, 0.5f, 96, 16, 1, 2, 32, 64, 16, 0)]
        [TestCase(NavigationDeviceTier.MobileMedium, 1f, 256, 32, 2, 4, 64, 128, 24, 0)]
        [TestCase(NavigationDeviceTier.MobileHigh, 1.5f, 512, 64, 4, 8, 128, 256, 40, 1)]
        public void ExistingPresetValuesRemainCompatible(
            NavigationDeviceTier tier,
            float frameMilliseconds,
            int iterationsPerFrame,
            int iterationsPerStep,
            int newQueries,
            int concurrent,
            int queued,
            int legacyCacheEntries,
            int legacyMemoryMegabytes,
            int legacyWorkers)
        {
            var profile = ScriptableObject.CreateInstance<NavigationPerformanceProfile>();
            try
            {
                profile.ApplyStartingPreset(tier);

                Assert.That(profile.DeviceTier, Is.EqualTo(tier));
                Assert.That(profile.FrameBudgetMilliseconds, Is.EqualTo(frameMilliseconds));
                Assert.That(profile.MaximumIterationsPerFrame, Is.EqualTo(iterationsPerFrame));
                Assert.That(profile.MaximumIterationsPerQueryStep, Is.EqualTo(iterationsPerStep));
                Assert.That(profile.MaximumNewQueriesPerFrame, Is.EqualTo(newQueries));
                Assert.That(profile.MaximumConcurrentSlicedQueries, Is.EqualTo(concurrent));
                Assert.That(profile.MaximumQueuedQueries, Is.EqualTo(queued));
                Assert.That(profile.RouteCacheEntries, Is.EqualTo(legacyCacheEntries));
                Assert.That(profile.MemoryBudgetMegabytes, Is.EqualTo(legacyMemoryMegabytes));
                Assert.That(profile.BackgroundWorkerCount, Is.EqualTo(legacyWorkers));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void LegacySerializedProfileRoundTripsWithoutFieldLoss()
        {
            var source = ScriptableObject.CreateInstance<NavigationPerformanceProfile>();
            var restored = ScriptableObject.CreateInstance<NavigationPerformanceProfile>();
            try
            {
                var serialized = new SerializedObject(source);
                serialized.FindProperty("deviceTier").enumValueIndex =
                    (int)NavigationDeviceTier.Custom;
                Set(serialized, "frameBudgetMilliseconds", 1.75f);
                Set(serialized, "maximumIterationsPerFrame", 333);
                Set(serialized, "maximumIterationsPerQueryStep", 27);
                Set(serialized, "maximumNewQueriesPerFrame", 3);
                Set(serialized, "maximumConcurrentSlicedQueries", 5);
                Set(serialized, "maximumQueuedQueries", 77);
                Set(serialized, "maximumPathPolygons", 199);
                Set(serialized, "maximumStraightPathPoints", 91);
                Set(serialized, "combatBotMinimumReplanSeconds", 0.31f);
                Set(serialized, "visibleBotMinimumReplanSeconds", 0.62f);
                Set(serialized, "backgroundBotMinimumReplanSeconds", 1.73f);
                Set(serialized, "queryDeadlineSeconds", 0.44f);
                Set(serialized, "routeCacheEntries", 17);
                Set(serialized, "memoryBudgetMegabytes", 29);
                Set(serialized, "backgroundWorkerCount", 3);
                serialized.FindProperty("collectProductionMetrics").boolValue = false;
                Set(serialized, "budgetWarningMultiplier", 1.6f);
                serialized.ApplyModifiedPropertiesWithoutUndo();

                string json = EditorJsonUtility.ToJson(source);
                EditorJsonUtility.FromJsonOverwrite(json, restored);

                var sourceObject = new SerializedObject(source);
                var restoredObject = new SerializedObject(restored);
                string[] fieldNames = typeof(NavigationPerformanceProfile)
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where(field => field.GetCustomAttribute<SerializeField>() != null)
                    .Select(field => field.Name)
                    .ToArray();
                for (int i = 0; i < fieldNames.Length; i++)
                {
                    Assert.That(
                        SerializedProperty.DataEquals(
                            sourceObject.FindProperty(fieldNames[i]),
                            restoredObject.FindProperty(fieldNames[i])),
                        Is.True,
                        $"Serialized field '{fieldNames[i]}' changed during legacy round-trip.");
                }

                Assert.That(restored.RouteCacheEntries, Is.EqualTo(17));
                Assert.That(restored.MemoryBudgetMegabytes, Is.EqualTo(29));
                Assert.That(restored.BackgroundWorkerCount, Is.EqualTo(3));
                Assert.That(restored.CollectProductionMetrics, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(restored);
            }
        }

        private static void Set(SerializedObject serialized, string name, int value)
        {
            serialized.FindProperty(name).intValue = value;
        }

        private static void Set(SerializedObject serialized, string name, float value)
        {
            serialized.FindProperty(name).floatValue = value;
        }
    }
}
