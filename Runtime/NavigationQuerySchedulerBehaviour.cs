using System;
using CustomNavigation.Authoring;
using UnityEngine;

namespace CustomNavigation.Runtime
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    [AddComponentMenu("DataSakura/Custom Navigation/Query Scheduler")]
    public sealed class NavigationQuerySchedulerBehaviour : MonoBehaviour
    {
        [SerializeField, Tooltip("Prebuilt navmesh artifact. The runtime loads it without a Recast bake.")]
        private NavigationArtifactAsset artifact;
        [SerializeField, Tooltip("CPU, iteration, queue and memory limits of the local scheduler.")]
        private NavigationPerformanceProfile performanceProfile;
        [SerializeField, Tooltip("Agent flags, area costs and nearest-poly extents for path queries.")]
        private NavigationAgentProfile agentProfile;
        [SerializeField, Tooltip("Log a rate-limited warning when the navigation frame budget is exceeded.")]
        private bool logBudgetWarnings = true;

        private NavigationQueryScheduler scheduler;
        private float nextBudgetWarningTime;

        public bool IsReady => scheduler != null;
        public NavigationArtifactAsset Artifact => artifact;
        public NavigationQueryScheduler Scheduler => scheduler;
        public NavigationSchedulerMetrics Metrics => scheduler != null
            ? scheduler.Metrics
            : default;

        public void Configure(
            NavigationArtifactAsset navigationArtifact,
            NavigationPerformanceProfile mobilePerformance,
            NavigationAgentProfile navigationAgent)
        {
            artifact = navigationArtifact;
            performanceProfile = mobilePerformance;
            agentProfile = navigationAgent;
        }

        private void Awake()
        {
            try
            {
                NavigationArtifactInstance instance = NavigationArtifactLoader.Load(artifact);
                scheduler = new NavigationQueryScheduler(instance, performanceProfile, agentProfile);
                Debug.Log(
                    $"[CustomNavigation] Local artifact ready: level={instance.LevelId}, " +
                    $"hash={instance.ArtifactHash}, polygons={instance.PolygonCount}, " +
                    $"tier={performanceProfile.DeviceTier}.",
                    this);
            }
            catch (Exception exception)
            {
                Debug.LogError("[CustomNavigation] Local navigation initialization failed.", this);
                Debug.LogException(exception, this);
                enabled = false;
            }
        }

        private void Update()
        {
            scheduler?.Tick();
            if (!logBudgetWarnings || scheduler == null || Time.unscaledTime < nextBudgetWarningTime)
            {
                return;
            }

            NavigationSchedulerMetrics metrics = scheduler.Metrics;
            float warningThreshold = performanceProfile.FrameBudgetMilliseconds
                                     * performanceProfile.BudgetWarningMultiplier;
            if (metrics.LastFrameMilliseconds > warningThreshold)
            {
                nextBudgetWarningTime = Time.unscaledTime + 5f;
                Debug.LogWarning(
                    $"[CustomNavigation] Local query budget exceeded: " +
                    $"{metrics.LastFrameMilliseconds:0.###} ms > {warningThreshold:0.###} ms, " +
                    $"iterations={metrics.LastFrameIterations}, active={metrics.ActiveQueries}, " +
                    $"queued={metrics.QueuedQueries}, tier={performanceProfile.DeviceTier}.",
                    this);
            }
        }

        private void OnDestroy()
        {
            scheduler?.CancelAll("Navigation scheduler was destroyed.");
        }

        public NavigationPathHandle RequestPath(
            Vector3 start,
            Vector3 destination,
            NavigationQueryPriority priority,
            Action<NavigationPathResult> completion)
        {
            if (scheduler == null)
            {
                throw new InvalidOperationException("Navigation scheduler is not ready.");
            }

            return scheduler.RequestPath(start, destination, priority, completion);
        }

        public bool TryProjectPosition(Vector3 position, out Vector3 projectedPosition)
        {
            if (scheduler == null)
            {
                projectedPosition = default;
                return false;
            }

            return scheduler.TryProjectPosition(position, out projectedPosition);
        }
    }
}
