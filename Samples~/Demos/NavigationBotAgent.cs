using System;
using CustomNavigation.Authoring;
using UnityEngine;

namespace CustomNavigation.Runtime
{
    /// <summary>
    /// Autonomous bot patrolling a <see cref="NavigationWaypointRoute"/>.
    /// The path is computed locally, on the authoritative server, or locally with a server
    /// correction - see <see cref="NavigationComputeMode"/>, the same scheme as in the scene
    /// DotRecastHybridPredicted.
    /// Can be used as a prefab: assigning the scheduler and the route in the Inspector is enough.
    /// </summary>
    [AddComponentMenu("Custom Navigation/Bot Agent")]
    public sealed class NavigationBotAgent : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Path computation")]
        [SerializeField, Tooltip(
            "Where the bot path is computed:\n" +
            "- Local Only: local navmesh only;\n" +
            "- Server Only: authoritative navigation server only;\n" +
            "- Server Predicted: immediate local prediction plus a server correction.")]
        private NavigationComputeMode computeMode = NavigationComputeMode.LocalOnly;

        [SerializeField, Tooltip(
            "Navigation server address. Leave empty to take it from NavigationServerSettings (Navigation -> Server).")]
        private string serverUrlOverride = string.Empty;

        [SerializeField, Tooltip(
            "Delay before retrying when a path could not be obtained (locally or from the server).")]
        [Range(0.1f, 10f)]
        private float retryDelaySeconds = 0.5f;

        [Header("Navigation")]
        [SerializeField, Tooltip("Scheduler with a loaded local artifact. " +
                                 "Required for Local Only and Server Predicted; optional in Server Only.")]
        private NavigationQuerySchedulerBehaviour navigation;

        [SerializeField, Tooltip("Waypoint route the bot will follow.")]
        private NavigationWaypointRoute route;

        [SerializeField, Tooltip("Starting waypoint index. 0 starts from the first one.")]
        private int startWaypointIndex;

        [Header("Movement")]
        [SerializeField, Tooltip("Movement speed (units per second)."), Range(0.5f, 20f)]
        private float moveSpeed = 3f;

        [SerializeField, Tooltip("Arrival radius: how close the bot must be to count a path point as reached."), Range(0.05f, 2f)]
        private float arrivalRadius = 0.4f;

        [SerializeField, Tooltip("How far to lift the bot above the navmesh surface so the model does not sink into the floor."), Range(0f, 3f)]
        private float groundOffset;

        [SerializeField, Tooltip("Snap the bot to the nearest navmesh point on start. Saves a prefab that is not placed exactly on the floor.")]
        private bool snapToNavMeshOnStart = true;

        [SerializeField, Tooltip("Pause at each waypoint before moving to the next one."), Range(0f, 10f)]
        private float waitAtWaypointSeconds;

        [SerializeField, Tooltip("Turn smoothing (degrees per frame)."), Range(1f, 720f)]
        private float rotationSpeed = 360f;

        [SerializeField]
        private NavigationQueryPriority queryPriority = NavigationQueryPriority.CombatBot;

        [Header("Visualization")]
        [SerializeField, Tooltip("LineRenderer used to draw the current path. Optional: one is created automatically when not assigned.")]
        private LineRenderer pathLine;

        [SerializeField, Tooltip("Path line color.")]
        private Color pathLineColor = new Color(0.2f, 0.8f, 0.2f, 0.7f);

        [SerializeField, Tooltip("Line color when the path came from the authoritative server.")]
        private Color serverPathLineColor = new Color(0.25f, 1f, 0.7f, 0.8f);

        [SerializeField, Tooltip("Draw the path in Play Mode.")]
        private bool showPath = true;

        // ── Runtime state ─────────────────────────────────────────────────────
        private enum BotState { Idle, RequestingPath, FollowingPath, WaitingAtWaypoint, Done }

        private const float WaypointHeightWarningThreshold = 1.5f;

        private BotState state = BotState.Idle;
        private Vector3[] currentPath;
        private int pathPointIndex;
        private int waypointIndex;
        private int patrolDirection = 1;
        private float waitTimer;
        private NavigationPathHandle pendingHandle;
        private bool hasPendingHandle;
        private int requestVersion;
        private Material pathLineMaterial;
        private Coroutine serverRequestRoutine;

        private bool UsesLocalNavigation => computeMode != NavigationComputeMode.ServerOnly;

        private string ServerBaseUrl => string.IsNullOrWhiteSpace(serverUrlOverride)
            ? NavigationServerRuntimeSettings.CurrentUrl
            : serverUrlOverride;

        private string LocalArtifactHash => navigation != null && navigation.Artifact != null
            ? navigation.Artifact.ArtifactHash
            : string.Empty;

        /// <summary>Current path computation mode. Lets a spawner configure the prefab at runtime.</summary>
        public NavigationComputeMode ComputeMode => computeMode;

        public void Configure(
            NavigationComputeMode mode,
            NavigationQuerySchedulerBehaviour scheduler,
            NavigationWaypointRoute waypointRoute)
        {
            computeMode = mode;
            navigation = scheduler;
            route = waypointRoute;
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────
        private void Start()
        {
            bool localReady = navigation != null && navigation.IsReady;
            if (UsesLocalNavigation && !localReady)
            {
                Debug.LogError(
                    $"[NavigationBotAgent] Mode {computeMode} requires a ready " +
                    "NavigationQuerySchedulerBehaviour. Assign it in the Navigation field.",
                    this);
                enabled = false;
                return;
            }

            if (route == null || route.Count == 0)
            {
                Debug.LogError("[NavigationBotAgent] No NavigationWaypointRoute assigned or route is empty.", this);
                enabled = false;
                return;
            }

            waypointIndex = Mathf.Clamp(startWaypointIndex, 0, route.Count - 1);
            patrolDirection = 1;

            if (snapToNavMeshOnStart && localReady)
            {
                SnapToNavMesh();
            }

            if (showPath)
            {
                EnsurePathLine();
            }

            if (localReady)
            {
                ValidateWaypoints();
            }

            RequestPath();
        }

        /// <summary>
        /// Warns when a waypoint hangs in the air or below the floor: the navmesh is searched
        /// within a limited vertical corridor, so a point at y=0 in a multi-level
        /// scene silently snaps to the lowest floor.
        /// </summary>
        private void ValidateWaypoints()
        {
            for (int i = 0; i < route.Count; i++)
            {
                if (!route.TryGetPosition(i, out Vector3 authored))
                {
                    Debug.LogWarning(
                        $"[NavigationBotAgent] Waypoint {i} is empty - assign a Transform in the route.",
                        this);
                    continue;
                }

                if (!navigation.TryProjectPosition(authored, out Vector3 projected))
                {
                    Debug.LogWarning(
                        $"[NavigationBotAgent] Waypoint {i} ({authored}) is off the navmesh - " +
                        "the bot will not be able to reach it. Move the point onto a walkable surface.",
                        this);
                    continue;
                }

                float verticalDelta = Mathf.Abs(projected.y - authored.y);
                if (verticalDelta > WaypointHeightWarningThreshold)
                {
                    Debug.LogWarning(
                        $"[NavigationBotAgent] Waypoint {i} sits at height y={authored.y:0.##}, " +
                        $"while the nearest navmesh is at y={projected.y:0.##} (delta {verticalDelta:0.##}). " +
                        "In a multi-level scene this usually means the point stayed at y=0 " +
                        "and the bot will walk on the lowest floor. Raise the waypoint to the intended floor.",
                        this);
                }
            }
        }

        /// <summary>
        /// Places the bot on the nearest navmesh polygon. Without this a prefab dropped
        /// by eye (for example at y=0 in a multi-level scene) would start below the floor.
        /// </summary>
        private void SnapToNavMesh()
        {
            if (navigation.TryProjectPosition(transform.position, out Vector3 projected))
            {
                transform.position = projected + Vector3.up * groundOffset;
                return;
            }

            Debug.LogWarning(
                "[NavigationBotAgent] Could not project the start position onto the navmesh. " +
                "Move the bot closer to a walkable surface.",
                this);
        }

        private void Update()
        {
            switch (state)
            {
                case BotState.FollowingPath:
                    UpdateFollowPath();
                    break;
                case BotState.WaitingAtWaypoint:
                    UpdateWaiting();
                    break;
            }
        }

        private void OnDestroy()
        {
            CancelPending();
            if (pathLineMaterial != null)
            {
                Destroy(pathLineMaterial);
                pathLineMaterial = null;
            }
        }

        // ── Movement ──────────────────────────────────────────────────────────
        private void UpdateFollowPath()
        {
            if (currentPath == null || pathPointIndex >= currentPath.Length)
            {
                OnWaypointReached();
                return;
            }

            Vector3 myPos = transform.position;
            Vector3 target = currentPath[pathPointIndex] + Vector3.up * groundOffset;

            // Move in 3D so that the bot climbs and descends ramps and
            // multi-level geometry instead of crawling at a single height.
            transform.position = Vector3.MoveTowards(myPos, target, moveSpeed * Time.deltaTime);

            // Rotation uses the horizontal component only: otherwise the bot
            // tips over on slopes.
            Vector3 flatDirection = target - myPos;
            flatDirection.y = 0f;
            if (flatDirection.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }

            if (Vector3.Distance(transform.position, target) <= arrivalRadius)
            {
                pathPointIndex++;
                if (pathPointIndex >= currentPath.Length)
                {
                    OnWaypointReached();
                    return;
                }
            }

            UpdatePathLine();
        }

        private void UpdateWaiting()
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                AdvanceWaypoint();
            }
        }

        private void OnWaypointReached()
        {
            HidePathLine();
            if (waitAtWaypointSeconds > 0f)
            {
                state = BotState.WaitingAtWaypoint;
                waitTimer = waitAtWaypointSeconds;
            }
            else
            {
                AdvanceWaypoint();
            }
        }

        private void AdvanceWaypoint()
        {
            int next = route.NextIndex(waypointIndex, ref patrolDirection);
            if (next < 0)
            {
                state = BotState.Done;
                HidePathLine();
                return;
            }

            waypointIndex = next;
            RequestPath();
        }

        // ── Navigation request ────────────────────────────────────────────────
        private void RequestPath()
        {
            if (!route.TryGetPosition(waypointIndex, out Vector3 destination))
            {
                AdvanceWaypoint();
                return;
            }

            CancelPending();
            state = BotState.RequestingPath;
            int version = ++requestVersion;
            Vector3 start = transform.position;

            if (computeMode == NavigationComputeMode.ServerOnly)
            {
                StartServerRequest(version, start, destination, string.Empty, false);
                return;
            }

            pendingHandle = navigation.RequestPath(
                start,
                destination,
                queryPriority,
                result => OnLocalPathReceived(version, start, destination, result));
            hasPendingHandle = true;
        }

        private void OnLocalPathReceived(
            int version,
            Vector3 start,
            Vector3 destination,
            NavigationPathResult result)
        {
            hasPendingHandle = false;

            if (result.IsCanceled || version != requestVersion)
            {
                return;
            }

            bool localSucceeded = result.Success && result.Points != null && result.Points.Length > 0;
            if (localSucceeded)
            {
                ApplyPath(result.Points, false);
            }
            else
            {
                Debug.LogWarning(
                    $"[NavigationBotAgent] Local path request failed (waypoint {waypointIndex}): {result.Message}",
                    this);
            }

            if (computeMode == NavigationComputeMode.ServerPredicted)
            {
                string localFingerprint = localSucceeded
                    ? NavigationPathFingerprint.Compute(result.Points)
                    : "local-failed";
                StartServerRequest(version, start, destination, localFingerprint, localSucceeded);
                return;
            }

            if (!localSucceeded)
            {
                RetryLater();
            }
        }

        private void StartServerRequest(
            int version,
            Vector3 start,
            Vector3 destination,
            string localFingerprint,
            bool localSucceeded)
        {
            StopServerRequest();
            string requestId = $"bot-{GetInstanceID()}-{version}";
            serverRequestRoutine = StartCoroutine(NavigationServerPathClient.RequestPath(
                ServerBaseUrl,
                requestId,
                start,
                destination,
                LocalArtifactHash,
                localFingerprint,
                serverResult => OnServerPathReceived(
                    version, requestId, localFingerprint, localSucceeded, serverResult)));
        }

        private void OnServerPathReceived(
            int version,
            string requestId,
            string localFingerprint,
            bool localSucceeded,
            NavigationServerPathResult result)
        {
            serverRequestRoutine = null;
            if (version != requestVersion)
            {
                return;
            }

            if (!result.Success)
            {
                Debug.LogWarning(
                    $"[NavigationBotAgent] [{requestId}] Authoritative path unavailable: {result.Message}",
                    this);

                if (computeMode == NavigationComputeMode.ServerPredicted && localSucceeded)
                {
                    // The local prediction is already applied - just keep following it to the next waypoint.
                    return;
                }

                RetryLater();
                return;
            }

            bool mismatch = !localSucceeded
                            || result.ServerMismatchDetected
                            || !string.Equals(
                                localFingerprint,
                                result.PathFingerprint,
                                StringComparison.OrdinalIgnoreCase)
                            || !string.Equals(
                                LocalArtifactHash,
                                result.ArtifactHash,
                                StringComparison.OrdinalIgnoreCase);

            if (computeMode == NavigationComputeMode.ServerPredicted && !mismatch)
            {
                // The server confirmed the local path: recolor the line but do not disturb the route.
                SetPathLineColor(serverPathLineColor);
                return;
            }

            if (computeMode == NavigationComputeMode.ServerPredicted)
            {
                Debug.LogWarning(
                    $"[NavigationBotAgent] [MISMATCH] [{requestId}] Applying authoritative correction. " +
                    $"localArtifact={LocalArtifactHash}, serverArtifact={result.ArtifactHash}, " +
                    $"localPath={localFingerprint}, serverPath={result.PathFingerprint}.",
                    this);
            }

            ApplyPath(result.Points, true);
        }

        private void ApplyPath(Vector3[] points, bool fromServer)
        {
            currentPath = points;
            pathPointIndex = 0;
            state = BotState.FollowingPath;
            SetPathLineColor(fromServer ? serverPathLineColor : pathLineColor);
            UpdatePathLine();
        }

        private void RetryLater()
        {
            state = BotState.Idle;
            CancelInvoke(nameof(RequestPath));
            Invoke(nameof(RequestPath), retryDelaySeconds);
        }

        private void StopServerRequest()
        {
            if (serverRequestRoutine != null)
            {
                StopCoroutine(serverRequestRoutine);
                serverRequestRoutine = null;
            }
        }

        private void CancelPending()
        {
            CancelInvoke(nameof(RequestPath));
            StopServerRequest();
            if (hasPendingHandle)
            {
                pendingHandle.Cancel();
                hasPendingHandle = false;
            }
        }

        // ── Path line ─────────────────────────────────────────────────────────
        private void EnsurePathLine()
        {
            if (pathLine != null) return;

            var go = new GameObject("[NavigationBot] Path Line");
            go.transform.SetParent(transform, false);
            pathLine = go.AddComponent<LineRenderer>();
            pathLine.useWorldSpace = true;
            pathLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pathLine.receiveShadows = false;
            pathLine.widthMultiplier = 0.04f;

            pathLineMaterial = new Material(Shader.Find("Sprites/Default")) { color = pathLineColor };
            pathLine.sharedMaterial = pathLineMaterial;
        }

        private void SetPathLineColor(Color color)
        {
            if (pathLineMaterial != null)
            {
                pathLineMaterial.color = color;
            }
        }

        private void UpdatePathLine()
        {
            if (!showPath || pathLine == null || currentPath == null) return;
            int remaining = currentPath.Length - pathPointIndex;
            if (remaining <= 0)
            {
                pathLine.positionCount = 0;
                return;
            }

            pathLine.positionCount = remaining + 1;
            pathLine.SetPosition(0, transform.position);
            for (int i = 0; i < remaining; i++)
            {
                pathLine.SetPosition(i + 1, currentPath[pathPointIndex + i] + Vector3.up * 0.05f);
            }
        }

        private void HidePathLine()
        {
            if (pathLine != null) pathLine.positionCount = 0;
        }

        // ── Gizmos ────────────────────────────────────────────────────────────
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (route == null) return;

            // Current target waypoint
            if (Application.isPlaying && state == BotState.FollowingPath
                && route.TryGetPosition(waypointIndex, out Vector3 wPos))
            {
                Gizmos.color = new Color(0.1f, 0.9f, 0.2f, 0.8f);
                Gizmos.DrawWireSphere(wPos, arrivalRadius + 0.1f);
                Gizmos.DrawLine(transform.position, wPos);
            }
        }
#endif
    }
}
