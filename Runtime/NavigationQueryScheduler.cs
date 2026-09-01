using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CustomNavigation.Authoring;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Jitter2.LinearMath;
using Real = System.Single;

namespace CustomNavigation.Runtime
{
    public readonly struct NavigationPathHandle
    {
        private readonly NavigationQueryScheduler scheduler;

        public long RequestId { get; }

        internal NavigationPathHandle(NavigationQueryScheduler owner, long requestId)
        {
            scheduler = owner;
            RequestId = requestId;
        }

        public void Cancel()
        {
            scheduler?.Cancel(RequestId);
        }
    }

    public sealed class NavigationPathResult
    {
        public long RequestId { get; }
        public bool Success { get; }
        public bool IsPartial { get; }
        public bool IsCanceled { get; }
        public string Message { get; }
        public JVector[] Points { get; }
        public int Iterations { get; }
        public double LatencyMilliseconds { get; }

        internal NavigationPathResult(
            long requestId,
            bool success,
            bool isPartial,
            bool isCanceled,
            string message,
            JVector[] points,
            int iterations,
            double latencyMilliseconds)
        {
            RequestId = requestId;
            Success = success;
            IsPartial = isPartial;
            IsCanceled = isCanceled;
            Message = message;
            Points = points ?? Array.Empty<JVector>();
            Iterations = iterations;
            LatencyMilliseconds = latencyMilliseconds;
        }
    }

    public readonly struct NavigationSchedulerMetrics
    {
        public readonly int QueuedQueries;
        public readonly int ActiveQueries;
        public readonly int CompletedQueries;
        public readonly int RejectedQueries;
        public readonly long TotalIterations;
        public readonly int LastFrameIterations;
        public readonly double LastFrameMilliseconds;

        internal NavigationSchedulerMetrics(
            int queuedQueries,
            int activeQueries,
            int completedQueries,
            int rejectedQueries,
            long totalIterations,
            int lastFrameIterations,
            double lastFrameMilliseconds)
        {
            QueuedQueries = queuedQueries;
            ActiveQueries = activeQueries;
            CompletedQueries = completedQueries;
            RejectedQueries = rejectedQueries;
            TotalIterations = totalIterations;
            LastFrameIterations = lastFrameIterations;
            LastFrameMilliseconds = lastFrameMilliseconds;
        }
    }

    public sealed class NavigationQueryScheduler
    {
        private static readonly double TimestampToSeconds = 1d / Stopwatch.Frequency;

        private readonly NavigationArtifactInstance artifact;
        private readonly NavigationPerformanceProfile performance;
        private readonly IDtQueryFilter filter;
        private readonly RcVec3f nearestPolyExtents;
        private readonly DtNavMeshQuery projectionQuery;
        private readonly List<PendingQuery> queued = new List<PendingQuery>();
        private readonly List<ActiveQuery> active = new List<ActiveQuery>();
        private readonly Stack<QueryWorkspace> workspacePool = new Stack<QueryWorkspace>();
        private readonly HashSet<long> canceled = new HashSet<long>();
        private readonly int ownerThreadId;
        private readonly Func<double> timeProvider;

        private long nextRequestId;
        private long enqueueSequence;
        private int roundRobinIndex;
        private int completedQueries;
        private int rejectedQueries;
        private long totalIterations;
        private int lastFrameIterations;
        private double lastFrameMilliseconds;

        public NavigationArtifactInstance Artifact => artifact;
        public NavigationPerformanceProfile PerformanceProfile => performance;
        public NavigationSchedulerMetrics Metrics => new NavigationSchedulerMetrics(
            queued.Count,
            active.Count,
            completedQueries,
            rejectedQueries,
            totalIterations,
            lastFrameIterations,
            lastFrameMilliseconds);

        public NavigationQueryScheduler(
            NavigationArtifactInstance loadedArtifact,
            NavigationPerformanceProfile performanceProfile,
            NavigationAgentProfile agentProfile)
            : this(loadedArtifact, performanceProfile, agentProfile, null)
        {
        }

        internal NavigationQueryScheduler(
            NavigationArtifactInstance loadedArtifact,
            NavigationPerformanceProfile performanceProfile,
            NavigationAgentProfile agentProfile,
            Func<double> schedulerTimeProvider)
        {
            CanonicalJitterContract.ValidateLoadedAssembly();

            artifact = loadedArtifact ?? throw new ArgumentNullException(nameof(loadedArtifact));
            performance = performanceProfile ?? throw new ArgumentNullException(nameof(performanceProfile));
            if (agentProfile == null)
            {
                throw new ArgumentNullException(nameof(agentProfile));
            }

            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            timeProvider = schedulerTimeProvider ?? NowSeconds;
            filter = CreateFilter(agentProfile);
            Real horizontalExtent = agentProfile.Radius * 4f;
            Real verticalExtent = agentProfile.Height * 2f;
            horizontalExtent = horizontalExtent < 1f ? 1f : horizontalExtent;
            verticalExtent = verticalExtent < 2f ? 2f : verticalExtent;
            nearestPolyExtents = new RcVec3f(horizontalExtent, verticalExtent, horizontalExtent);
            projectionQuery = artifact.CreateQuery();

            for (int i = 0; i < performance.MaximumConcurrentSlicedQueries; i++)
            {
                workspacePool.Push(new QueryWorkspace(
                    artifact.CreateQuery(),
                    performance.MaximumPathPolygons,
                    performance.MaximumStraightPathPoints));
            }
        }

        public bool TryProjectPosition(JVector position, out JVector projectedPosition)
        {
            EnsureOwnerThread();
            NavigationJitterValidation.RequireFinite(position, nameof(position));
            DtStatus status = projectionQuery.FindNearestPoly(
                NavigationDotRecastAdapter.ToDotRecast(in position),
                nearestPolyExtents,
                filter,
                out long polygonReference,
                out RcVec3f nearestPoint,
                out _);
            if (status.Failed() || polygonReference == 0)
            {
                projectedPosition = default;
                return false;
            }

            projectedPosition = NavigationDotRecastAdapter.FromDotRecast(in nearestPoint);
            return true;
        }

        public NavigationPathHandle RequestPath(
            JVector start,
            JVector destination,
            NavigationQueryPriority priority,
            Action<NavigationPathResult> completion)
        {
            EnsureOwnerThread();
            if (completion == null)
            {
                throw new ArgumentNullException(nameof(completion));
            }

            NavigationJitterValidation.RequireFinite(start, nameof(start));
            NavigationJitterValidation.RequireFinite(destination, nameof(destination));

            long requestId = ++nextRequestId;
            var request = new PendingQuery(
                requestId,
                ++enqueueSequence,
                priority,
                start,
                destination,
                CurrentTimeSeconds(),
                completion);

            if (queued.Count >= performance.MaximumQueuedQueries)
            {
                int worstIndex = FindWorstQueuedIndex();
                if (worstIndex >= 0 && priority < queued[worstIndex].Priority)
                {
                    PendingQuery evicted = queued[worstIndex];
                    queued.RemoveAt(worstIndex);
                    rejectedQueries++;
                    Complete(
                        evicted,
                        false,
                        false,
                        false,
                        "Navigation queue evicted this request for a higher-priority query.");
                }
                else
                {
                    rejectedQueries++;
                    Complete(
                        request,
                        false,
                        false,
                        false,
                        "Navigation queue is full for the current mobile performance profile.");
                    return new NavigationPathHandle(this, requestId);
                }
            }

            queued.Add(request);
            return new NavigationPathHandle(this, requestId);
        }

        public void Cancel(long requestId)
        {
            EnsureOwnerThread();
            if (requestId > 0)
            {
                canceled.Add(requestId);
            }
        }

        public void Tick()
        {
            Tick(true);
        }

        internal void Tick(bool processActiveQueries)
        {
            EnsureOwnerThread();
            long frameStart = Stopwatch.GetTimestamp();
            lastFrameIterations = 0;

            ExpireAndCancelQueued();
            AdmitQueries(frameStart);

            while (processActiveQueries
                   && active.Count > 0
                   && lastFrameIterations < performance.MaximumIterationsPerFrame
                   && ElapsedMilliseconds(frameStart) < performance.FrameBudgetMilliseconds)
            {
                if (roundRobinIndex >= active.Count)
                {
                    roundRobinIndex = 0;
                }

                ActiveQuery query = active[roundRobinIndex];
                if (canceled.Remove(query.Request.RequestId))
                {
                    FinishActive(
                        roundRobinIndex,
                        false,
                        false,
                        true,
                        "Navigation request was canceled.",
                        Array.Empty<JVector>());
                    continue;
                }

                int remainingFrameIterations = performance.MaximumIterationsPerFrame - lastFrameIterations;
                int stepIterations = Math.Min(
                    performance.MaximumIterationsPerQueryStep,
                    remainingFrameIterations);
                DtStatus status = query.Query.UpdateSlicedFindPath(stepIterations, out int completedIterations);
                query.Iterations += completedIterations;
                lastFrameIterations += completedIterations;
                totalIterations += completedIterations;

                if (status.InProgress())
                {
                    roundRobinIndex++;
                    continue;
                }

                if (status.Failed())
                {
                    FinishActive(
                        roundRobinIndex,
                        false,
                        false,
                        false,
                        "DotRecast sliced query failed.",
                        Array.Empty<JVector>());
                    continue;
                }

                FinalizeActive(roundRobinIndex);
            }

            lastFrameMilliseconds = ElapsedMilliseconds(frameStart);
        }

        public void CancelAll(string reason = "Navigation scheduler stopped.")
        {
            EnsureOwnerThread();
            for (int i = queued.Count - 1; i >= 0; i--)
            {
                PendingQuery request = queued[i];
                queued.RemoveAt(i);
                Complete(request, false, false, true, reason);
            }

            while (active.Count > 0)
            {
                FinishActive(0, false, false, true, reason, Array.Empty<JVector>());
            }

            canceled.Clear();
        }

        private void AdmitQueries(long frameStart)
        {
            int admitted = 0;
            while (queued.Count > 0
                   && active.Count < performance.MaximumConcurrentSlicedQueries
                   && admitted < performance.MaximumNewQueriesPerFrame
                   && ElapsedMilliseconds(frameStart) < performance.FrameBudgetMilliseconds)
            {
                int nextIndex = FindBestQueuedIndex();
                PendingQuery request = queued[nextIndex];
                queued.RemoveAt(nextIndex);

                if (canceled.Remove(request.RequestId))
                {
                    Complete(request, false, false, true, "Navigation request was canceled.");
                    continue;
                }

                if (CurrentTimeSeconds() - request.CreatedAtSeconds > performance.QueryDeadlineSeconds)
                {
                    Complete(request, false, false, false, "Navigation request expired in the queue.");
                    continue;
                }

                admitted++;
                StartQuery(request);
            }
        }

        private void StartQuery(PendingQuery request)
        {
            QueryWorkspace workspace = workspacePool.Pop();
            DtNavMeshQuery query = workspace.Query;
            JVector canonicalStart = request.Start;
            JVector canonicalEnd = request.Destination;
            RcVec3f requestedStart = NavigationDotRecastAdapter.ToDotRecast(in canonicalStart);
            RcVec3f requestedEnd = NavigationDotRecastAdapter.ToDotRecast(in canonicalEnd);
            DtStatus startStatus = query.FindNearestPoly(
                requestedStart,
                nearestPolyExtents,
                filter,
                out long startRef,
                out RcVec3f nearestStart,
                out _);
            DtStatus endStatus = query.FindNearestPoly(
                requestedEnd,
                nearestPolyExtents,
                filter,
                out long endRef,
                out RcVec3f nearestEnd,
                out _);

            if (startStatus.Failed() || endStatus.Failed() || startRef == 0 || endRef == 0)
            {
                workspacePool.Push(workspace);
                Complete(
                    request,
                    false,
                    false,
                    false,
                    "Start or destination is outside the navigation artifact.");
                return;
            }

            DtStatus initStatus = query.InitSlicedFindPath(
                startRef,
                endRef,
                nearestStart,
                nearestEnd,
                filter,
                0);
            if (initStatus.Failed())
            {
                workspacePool.Push(workspace);
                Complete(request, false, false, false, "DotRecast could not initialize sliced pathfinding.");
                return;
            }

            active.Add(new ActiveQuery(request, workspace, nearestStart, nearestEnd));
            if (!initStatus.InProgress())
            {
                FinalizeActive(active.Count - 1);
            }
        }

        private void FinalizeActive(int activeIndex)
        {
            ActiveQuery query = active[activeIndex];
            long[] polygonPath = query.Workspace.PolygonPath;
            DtStatus pathStatus = query.Query.FinalizeSlicedFindPath(
                polygonPath.AsSpan(),
                out int polygonCount,
                polygonPath.Length);
            if (pathStatus.Failed() || polygonCount == 0)
            {
                FinishActive(
                    activeIndex,
                    false,
                    false,
                    false,
                    "DotRecast returned no polygon corridor.",
                    Array.Empty<JVector>());
                return;
            }

            DtStraightPath[] straightPath = query.Workspace.StraightPath;
            DtStatus straightStatus = query.Query.FindStraightPath(
                NavigationDotRecastAdapter.ToDotRecast(in query.NearestStart),
                NavigationDotRecastAdapter.ToDotRecast(in query.NearestEnd),
                polygonPath.AsSpan(),
                polygonCount,
                straightPath.AsSpan(),
                out int pointCount,
                straightPath.Length,
                DtStraightPathOptions.DT_STRAIGHTPATH_ALL_CROSSINGS);
            if (straightStatus.Failed() || pointCount == 0)
            {
                FinishActive(
                    activeIndex,
                    false,
                    false,
                    false,
                    "DotRecast returned no straight path.",
                    Array.Empty<JVector>());
                return;
            }

            var points = new JVector[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                RcVec3f point = straightPath[i].pos;
                points[i] = NavigationDotRecastAdapter.FromDotRecast(in point);
            }

            bool partial = pathStatus.IsPartial();
            FinishActive(
                activeIndex,
                true,
                partial,
                false,
                partial ? "DotRecast returned a partial path." : "DotRecast path completed.",
                points);
        }

        private void FinishActive(
            int activeIndex,
            bool success,
            bool partial,
            bool wasCanceled,
            string message,
            JVector[] points)
        {
            ActiveQuery query = active[activeIndex];
            active.RemoveAt(activeIndex);
            workspacePool.Push(query.Workspace);
            if (roundRobinIndex >= active.Count)
            {
                roundRobinIndex = 0;
            }

            Complete(
                query.Request,
                success,
                partial,
                wasCanceled,
                message,
                points,
                query.Iterations);
        }

        private void ExpireAndCancelQueued()
        {
            double now = CurrentTimeSeconds();
            for (int i = queued.Count - 1; i >= 0; i--)
            {
                PendingQuery request = queued[i];
                bool wasCanceled = canceled.Remove(request.RequestId);
                bool expired = now - request.CreatedAtSeconds > performance.QueryDeadlineSeconds;
                if (!wasCanceled && !expired)
                {
                    continue;
                }

                queued.RemoveAt(i);
                Complete(
                    request,
                    false,
                    false,
                    wasCanceled,
                    wasCanceled
                        ? "Navigation request was canceled."
                        : "Navigation request expired in the queue.");
            }
        }

        private void Complete(
            PendingQuery request,
            bool success,
            bool partial,
            bool wasCanceled,
            string message,
            JVector[] points = null,
            int iterations = 0)
        {
            completedQueries++;
            var result = new NavigationPathResult(
                request.RequestId,
                success,
                partial,
                wasCanceled,
                message,
                points,
                iterations,
                (CurrentTimeSeconds() - request.CreatedAtSeconds) * 1000d);
            try
            {
                request.Completion(result);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
        }

        private int FindBestQueuedIndex()
        {
            int best = 0;
            for (int i = 1; i < queued.Count; i++)
            {
                if (queued[i].Priority < queued[best].Priority
                    || (queued[i].Priority == queued[best].Priority
                        && queued[i].Sequence < queued[best].Sequence))
                {
                    best = i;
                }
            }

            return best;
        }

        private int FindWorstQueuedIndex()
        {
            if (queued.Count == 0)
            {
                return -1;
            }

            int worst = 0;
            for (int i = 1; i < queued.Count; i++)
            {
                if (queued[i].Priority > queued[worst].Priority
                    || (queued[i].Priority == queued[worst].Priority
                        && queued[i].Sequence < queued[worst].Sequence))
                {
                    worst = i;
                }
            }

            return worst;
        }

        private static IDtQueryFilter CreateFilter(NavigationAgentProfile agent)
        {
            var costs = new float[64];
            for (int areaId = 0; areaId < costs.Length; areaId++)
            {
                costs[areaId] = agent.GetAreaCost(areaId);
            }

            return new DtQueryDefaultFilter(
                agent.IncludedPolygonFlags,
                agent.ExcludedPolygonFlags,
                costs);
        }

        private void EnsureOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "NavigationQueryScheduler must be requested and ticked from its owner thread.");
            }
        }

        private static double NowSeconds()
        {
            return Stopwatch.GetTimestamp() * TimestampToSeconds;
        }

        private double CurrentTimeSeconds()
        {
            return timeProvider();
        }

        private static double ElapsedMilliseconds(long startTimestamp)
        {
            return (Stopwatch.GetTimestamp() - startTimestamp) * TimestampToSeconds * 1000d;
        }

        private sealed class PendingQuery
        {
            public readonly long RequestId;
            public readonly long Sequence;
            public readonly NavigationQueryPriority Priority;
            public readonly JVector Start;
            public readonly JVector Destination;
            public readonly double CreatedAtSeconds;
            public readonly Action<NavigationPathResult> Completion;

            public PendingQuery(
                long requestId,
                long sequence,
                NavigationQueryPriority priority,
                JVector start,
                JVector destination,
                double createdAtSeconds,
                Action<NavigationPathResult> completion)
            {
                RequestId = requestId;
                Sequence = sequence;
                Priority = priority;
                Start = start;
                Destination = destination;
                CreatedAtSeconds = createdAtSeconds;
                Completion = completion;
            }
        }

        private sealed class ActiveQuery
        {
            public readonly PendingQuery Request;
            public readonly QueryWorkspace Workspace;
            public readonly JVector NearestStart;
            public readonly JVector NearestEnd;
            public int Iterations;

            public DtNavMeshQuery Query => Workspace.Query;

            public ActiveQuery(
                PendingQuery request,
                QueryWorkspace workspace,
                RcVec3f nearestStart,
                RcVec3f nearestEnd)
            {
                Request = request;
                Workspace = workspace;
                NearestStart = NavigationDotRecastAdapter.FromDotRecast(in nearestStart);
                NearestEnd = NavigationDotRecastAdapter.FromDotRecast(in nearestEnd);
            }
        }

        private sealed class QueryWorkspace
        {
            public readonly DtNavMeshQuery Query;
            public readonly long[] PolygonPath;
            public readonly DtStraightPath[] StraightPath;

            public QueryWorkspace(
                DtNavMeshQuery query,
                int maximumPathPolygons,
                int maximumStraightPathPoints)
            {
                Query = query;
                PolygonPath = new long[maximumPathPolygons];
                StraightPath = new DtStraightPath[maximumStraightPathPoints];
            }
        }
    }
}
