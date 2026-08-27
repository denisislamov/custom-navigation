using UnityEngine;

namespace CustomNavigation.Authoring
{
    [CreateAssetMenu(
        fileName = "NavigationPerformanceProfile",
        menuName = "DataSakura/Custom Navigation/Performance Profile")]
    public sealed class NavigationPerformanceProfile : ScriptableObject
    {
        [SerializeField, Tooltip("Target mobile device class. The preset sets starting values, not guarantees.")]
        private NavigationDeviceTier deviceTier = NavigationDeviceTier.MobileMedium;
        [SerializeField, Min(0.05f), Tooltip("Maximum CPU time of the local navigation scheduler per frame, in milliseconds.")]
        private float frameBudgetMilliseconds = 1f;
        [SerializeField, Min(1), Tooltip("Total A* iteration limit for all navigation queries per frame.")]
        private int maximumIterationsPerFrame = 256;
        [SerializeField, Min(1), Tooltip("Maximum iteration quantum of a single sliced query per scheduler step.")]
        private int maximumIterationsPerQueryStep = 32;
        [SerializeField, Min(1), Tooltip("How many new requests may be started in a single frame.")]
        private int maximumNewQueriesPerFrame = 2;
        [SerializeField, Min(1), Tooltip("Maximum number of simultaneously active sliced queries and their workspaces.")]
        private int maximumConcurrentSlicedQueries = 4;
        [SerializeField, Min(1), Tooltip("Hard queue limit; protects memory during mass bot replanning.")]
        private int maximumQueuedQueries = 64;
        [SerializeField, Min(8), Tooltip("Maximum number of polygons in the corridor of a single result.")]
        private int maximumPathPolygons = 256;
        [SerializeField, Min(2), Tooltip("Maximum number of resulting path points, including polygon crossings on ramps.")]
        private int maximumStraightPathPoints = 128;
        [SerializeField, Min(0.05f), Tooltip("Consumer pacing value used by the bundled Local Bots sample; the scheduler does not enforce it.")]
        private float combatBotMinimumReplanSeconds = 0.25f;
        [SerializeField, Min(0.05f), Tooltip("Consumer pacing value used by the bundled Local Bots sample for visible bots; the scheduler does not enforce it.")]
        private float visibleBotMinimumReplanSeconds = 0.5f;
        [SerializeField, Min(0.05f), Tooltip("Consumer pacing value used by the bundled Local Bots sample for background bots; the scheduler does not enforce it.")]
        private float backgroundBotMinimumReplanSeconds = 1.5f;
        [SerializeField, Min(0.05f), Tooltip("Maximum wait before admission. Expiration applies only in the backlog; it never aborts an active sliced search.")]
        private float queryDeadlineSeconds = 0.5f;
        [SerializeField, Min(0), Tooltip("Reserved serialized value. No route cache is implemented or sized by this field.")]
        private int routeCacheEntries = 128;
        [SerializeField, Min(1), Tooltip("Reserved serialized value. Current runtime does not enforce a memory budget.")]
        private int memoryBudgetMegabytes = 24;
        [SerializeField, Range(0, 4), Tooltip("Reserved serialized value. NavigationQueryScheduler remains owner-thread only.")]
        private int backgroundWorkerCount;
        [SerializeField, Tooltip("Reserved serialized value. Scheduler metrics exist in memory, but no production telemetry collector reads this flag.")]
        private bool collectProductionMetrics = true;
        [SerializeField, Min(1f), Tooltip("NavigationQuerySchedulerBehaviour logs a rate-limited warning above Frame Budget multiplied by this factor.")]
        private float budgetWarningMultiplier = 1.25f;

        public NavigationDeviceTier DeviceTier => deviceTier;
        public float FrameBudgetMilliseconds => frameBudgetMilliseconds;
        public int MaximumIterationsPerFrame => maximumIterationsPerFrame;
        public int MaximumIterationsPerQueryStep => maximumIterationsPerQueryStep;
        public int MaximumNewQueriesPerFrame => maximumNewQueriesPerFrame;
        public int MaximumConcurrentSlicedQueries => maximumConcurrentSlicedQueries;
        public int MaximumQueuedQueries => maximumQueuedQueries;
        public int MaximumPathPolygons => maximumPathPolygons;
        public int MaximumStraightPathPoints => maximumStraightPathPoints;
        public float CombatBotMinimumReplanSeconds => combatBotMinimumReplanSeconds;
        public float VisibleBotMinimumReplanSeconds => visibleBotMinimumReplanSeconds;
        public float BackgroundBotMinimumReplanSeconds => backgroundBotMinimumReplanSeconds;
        public float QueryDeadlineSeconds => queryDeadlineSeconds;
        public int RouteCacheEntries => routeCacheEntries;
        public int MemoryBudgetMegabytes => memoryBudgetMegabytes;
        public int BackgroundWorkerCount => backgroundWorkerCount;
        public bool CollectProductionMetrics => collectProductionMetrics;
        public float BudgetWarningMultiplier => budgetWarningMultiplier;

        public void ApplyStartingPreset(NavigationDeviceTier tier)
        {
            deviceTier = tier;
            switch (tier)
            {
                case NavigationDeviceTier.MobileLow:
                    SetBudget(0.5f, 96, 16, 1, 2, 32, 64, 16, 0);
                    break;
                case NavigationDeviceTier.MobileHigh:
                    SetBudget(1.5f, 512, 64, 4, 8, 128, 256, 40, 1);
                    break;
                case NavigationDeviceTier.Custom:
                    break;
                default:
                    SetBudget(1f, 256, 32, 2, 4, 64, 128, 24, 0);
                    break;
            }
        }

        private void SetBudget(
            float milliseconds,
            int iterationsPerFrame,
            int iterationsPerStep,
            int newQueries,
            int concurrentQueries,
            int queuedQueries,
            int cacheEntries,
            int memoryMegabytes,
            int workers)
        {
            frameBudgetMilliseconds = milliseconds;
            maximumIterationsPerFrame = iterationsPerFrame;
            maximumIterationsPerQueryStep = iterationsPerStep;
            maximumNewQueriesPerFrame = newQueries;
            maximumConcurrentSlicedQueries = concurrentQueries;
            maximumQueuedQueries = queuedQueries;
            routeCacheEntries = cacheEntries;
            memoryBudgetMegabytes = memoryMegabytes;
            backgroundWorkerCount = workers;
        }

        private void OnValidate()
        {
            frameBudgetMilliseconds = Mathf.Max(0.05f, frameBudgetMilliseconds);
            maximumIterationsPerFrame = Mathf.Max(1, maximumIterationsPerFrame);
            maximumIterationsPerQueryStep = Mathf.Clamp(
                maximumIterationsPerQueryStep,
                1,
                maximumIterationsPerFrame);
            maximumNewQueriesPerFrame = Mathf.Max(1, maximumNewQueriesPerFrame);
            maximumConcurrentSlicedQueries = Mathf.Max(1, maximumConcurrentSlicedQueries);
            maximumQueuedQueries = Mathf.Max(maximumConcurrentSlicedQueries, maximumQueuedQueries);
            maximumPathPolygons = Mathf.Max(8, maximumPathPolygons);
            maximumStraightPathPoints = Mathf.Max(2, maximumStraightPathPoints);
            combatBotMinimumReplanSeconds = Mathf.Max(0.05f, combatBotMinimumReplanSeconds);
            visibleBotMinimumReplanSeconds = Mathf.Max(0.05f, visibleBotMinimumReplanSeconds);
            backgroundBotMinimumReplanSeconds = Mathf.Max(0.05f, backgroundBotMinimumReplanSeconds);
            queryDeadlineSeconds = Mathf.Max(0.05f, queryDeadlineSeconds);
            routeCacheEntries = Mathf.Max(0, routeCacheEntries);
            memoryBudgetMegabytes = Mathf.Max(1, memoryBudgetMegabytes);
            backgroundWorkerCount = Mathf.Clamp(backgroundWorkerCount, 0, 4);
            budgetWarningMultiplier = Mathf.Max(1f, budgetWarningMultiplier);
        }
    }
}
