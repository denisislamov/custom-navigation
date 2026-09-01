using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using Jitter2.LinearMath;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomNavigation.Editor.Tests
{
    public sealed class NavigationQuerySchedulerTests
    {
        private NavigationArtifactInstance artifact;
        private NavigationAgentProfile agent;

        [OneTimeSetUp]
        public void BuildFixture()
        {
            var root = new GameObject("CN-04 scheduler fixture");
            var floor = new GameObject("Floor");
            Mesh mesh = null;
            NavigationAreaCatalog areas = null;
            NavigationPerformanceProfile buildProfile = null;
            try
            {
                floor.transform.SetParent(root.transform, false);
                mesh = new Mesh { name = "CN-04 floor" };
                mesh.vertices = new[]
                {
                    new Vector3(-8f, 0f, -8f),
                    new Vector3(8f, 0f, -8f),
                    new Vector3(8f, 0f, 8f),
                    new Vector3(-8f, 0f, 8f)
                };
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                mesh.RecalculateBounds();
                floor.AddComponent<MeshFilter>().sharedMesh = mesh;
                floor.AddComponent<NavigationGeometrySource>();

                var blocker = new GameObject("Blocker");
                blocker.transform.SetParent(root.transform, false);
                NavigationModifierVolume modifier = blocker.AddComponent<NavigationModifierVolume>();
                Set(modifier, "center", new Vector3(0f, 1f, 0f));
                Set(modifier, "size", new Vector3(2f, 2f, 7f));

                NavigationLevel level = root.AddComponent<NavigationLevel>();
                Set(level, "levelId", "cn04_scheduler_fixture");
                agent = ScriptableObject.CreateInstance<NavigationAgentProfile>();
                areas = ScriptableObject.CreateInstance<NavigationAreaCatalog>();
                areas.ResetToDefaults();
                buildProfile = ScriptableObject.CreateInstance<NavigationPerformanceProfile>();
                buildProfile.ApplyStartingPreset(NavigationDeviceTier.MobileMedium);
                level.ConfigureDefaults(agent, areas, buildProfile);
                artifact = NavigationArtifactBuilder.BuildInMemoryForSchedulerTests(level);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(areas);
                Object.DestroyImmediate(buildProfile);
            }
        }

        [OneTimeTearDown]
        public void ReleaseFixture()
        {
            Object.DestroyImmediate(agent);
        }

        [Test]
        public void OrdinaryQueueCompletesAndReportsBacklogSeparatelyFromActiveQueries()
        {
            NavigationPerformanceProfile profile = CreateProfile();
            try
            {
                var scheduler = new NavigationQueryScheduler(artifact, profile, agent);
                var results = new List<NavigationPathResult>();
                scheduler.RequestPath(Start, End, NavigationQueryPriority.VisibleBot, results.Add);
                scheduler.RequestPath(End, Start, NavigationQueryPriority.BackgroundBot, results.Add);

                Assert.That(scheduler.Metrics.QueuedQueries, Is.EqualTo(2));
                Assert.That(scheduler.Metrics.ActiveQueries, Is.Zero);
                TickUntil(scheduler, () => results.Count == 2);

                Assert.That(results.TrueForAll(result => result.Success), Is.True);
                Assert.That(scheduler.Metrics.QueuedQueries, Is.Zero);
                Assert.That(scheduler.Metrics.ActiveQueries, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FullBacklogEvictsWorstQueuedRequestForHigherPriority()
        {
            NavigationPerformanceProfile profile = CreateProfile(queued: 2, concurrent: 1, newPerFrame: 1);
            try
            {
                var scheduler = new NavigationQueryScheduler(artifact, profile, agent);
                var results = new List<NavigationPathResult>();
                scheduler.RequestPath(Start, End, NavigationQueryPriority.Prewarm, results.Add);
                scheduler.RequestPath(End, Start, NavigationQueryPriority.Prewarm, results.Add);
                scheduler.RequestPath(Start, End, NavigationQueryPriority.CriticalCorrection, results.Add);

                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(results[0].Message, Does.Contain("evicted"));
                Assert.That(scheduler.Metrics.RejectedQueries, Is.EqualTo(1));
                Assert.That(scheduler.Metrics.QueuedQueries, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void FullBacklogRejectsRequestWithoutHigherPriority()
        {
            NavigationPerformanceProfile profile = CreateProfile(queued: 1, concurrent: 1, newPerFrame: 1);
            try
            {
                var scheduler = new NavigationQueryScheduler(artifact, profile, agent);
                var results = new List<NavigationPathResult>();
                scheduler.RequestPath(Start, End, NavigationQueryPriority.VisibleBot, results.Add);
                scheduler.RequestPath(End, Start, NavigationQueryPriority.BackgroundBot, results.Add);

                Assert.That(results, Has.Count.EqualTo(1));
                Assert.That(results[0].Message, Does.Contain("queue is full"));
                Assert.That(scheduler.Metrics.RejectedQueries, Is.EqualTo(1));
                Assert.That(scheduler.Metrics.QueuedQueries, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void CancellationIsDeliveredOnTheNextTick()
        {
            NavigationPerformanceProfile profile = CreateProfile();
            try
            {
                var scheduler = new NavigationQueryScheduler(artifact, profile, agent);
                NavigationPathResult result = null;
                NavigationPathHandle handle = scheduler.RequestPath(
                    Start,
                    End,
                    NavigationQueryPriority.VisibleBot,
                    value => result = value);
                handle.Cancel();

                Assert.That(result, Is.Null);
                scheduler.Tick();

                Assert.That(result, Is.Not.Null);
                Assert.That(result.IsCanceled, Is.True);
                Assert.That(result.Message, Does.Contain("canceled"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DeadlineExpiresWaitingRequestAndReportsQueueLatency()
        {
            NavigationPerformanceProfile profile = CreateProfile(deadline: 0.05f);
            double now = 10d;
            try
            {
                var scheduler = new NavigationQueryScheduler(artifact, profile, agent, () => now);
                NavigationPathResult result = null;
                scheduler.RequestPath(
                    Start,
                    End,
                    NavigationQueryPriority.BackgroundBot,
                    value => result = value);
                now += 0.075d;

                scheduler.Tick();

                Assert.That(result, Is.Not.Null);
                Assert.That(result.Success, Is.False);
                Assert.That(result.IsCanceled, Is.False);
                Assert.That(result.Message, Is.EqualTo("Navigation request expired in the queue."));
                Assert.That(result.LatencyMilliseconds, Is.EqualTo(75d).Within(0.001d));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DeadlineDoesNotAbortAnAdmittedActiveQuery()
        {
            NavigationPerformanceProfile profile = CreateProfile(deadline: 0.05f);
            double now = 20d;
            try
            {
                var scheduler = new NavigationQueryScheduler(artifact, profile, agent, () => now);
                NavigationPathResult result = null;
                scheduler.RequestPath(
                    Start,
                    End,
                    NavigationQueryPriority.VisibleBot,
                    value => result = value);

                scheduler.Tick(false);
                Assert.That(scheduler.Metrics.QueuedQueries, Is.Zero);
                Assert.That(scheduler.Metrics.ActiveQueries, Is.EqualTo(1));

                now += 5d;
                scheduler.Tick(false);

                Assert.That(result, Is.Null, "An active query must not use the queue deadline.");
                Assert.That(scheduler.Metrics.ActiveQueries, Is.EqualTo(1));
                TickUntil(scheduler, () => result != null);
                Assert.That(result.Message, Is.Not.EqualTo("Navigation request expired in the queue."));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void WorkspaceAndReturnedPathRespectConfiguredResultLimits()
        {
            NavigationPerformanceProfile profile = CreateProfile(pathPolygons: 8, straightPoints: 2);
            try
            {
                var scheduler = new NavigationQueryScheduler(artifact, profile, agent);
                FieldInfo poolField = typeof(NavigationQueryScheduler).GetField(
                    "workspacePool",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var pool = (IEnumerable)poolField.GetValue(scheduler);
                foreach (object workspace in pool)
                {
                    Type type = workspace.GetType();
                    Assert.That(((Array)type.GetField("PolygonPath").GetValue(workspace)).Length,
                        Is.EqualTo(8));
                    Assert.That(((Array)type.GetField("StraightPath").GetValue(workspace)).Length,
                        Is.EqualTo(2));
                }

                NavigationPathResult result = null;
                scheduler.RequestPath(
                    Start,
                    End,
                    NavigationQueryPriority.PlayerImmediate,
                    value => result = value);
                TickUntil(scheduler, () => result != null);

                Assert.That(result.Points.Length, Is.LessThanOrEqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static readonly JVector Start = new JVector(-5f, 0f, -5f);
        private static readonly JVector End = new JVector(5f, 0f, 5f);

        private static NavigationPerformanceProfile CreateProfile(
            int queued = 16,
            int concurrent = 2,
            int newPerFrame = 2,
            float deadline = 0.5f,
            int pathPolygons = 64,
            int straightPoints = 32)
        {
            var profile = ScriptableObject.CreateInstance<NavigationPerformanceProfile>();
            profile.ApplyStartingPreset(NavigationDeviceTier.Custom);
            Set(profile, "frameBudgetMilliseconds", 10f);
            Set(profile, "maximumIterationsPerFrame", 4096);
            Set(profile, "maximumIterationsPerQueryStep", 256);
            Set(profile, "maximumNewQueriesPerFrame", newPerFrame);
            Set(profile, "maximumConcurrentSlicedQueries", concurrent);
            Set(profile, "maximumQueuedQueries", queued);
            Set(profile, "maximumPathPolygons", pathPolygons);
            Set(profile, "maximumStraightPathPoints", straightPoints);
            Set(profile, "queryDeadlineSeconds", deadline);
            return profile;
        }

        private static void Set(Object target, string property, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string property, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string property, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Set(Object target, string property, Vector3 value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(property).vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void TickUntil(
            NavigationQueryScheduler scheduler,
            Func<bool> completed)
        {
            for (int tick = 0; tick < 64 && !completed(); tick++)
            {
                scheduler.Tick();
            }

            Assert.That(completed(), Is.True, "Scheduler did not complete within 64 ticks.");
        }
    }
}
