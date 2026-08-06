using System;
using System.Collections.Generic;
using CustomNavigation.Authoring;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CustomNavigation.Runtime
{
    [DisallowMultipleComponent]
    public sealed class LocalOnlyBotsNavigationDemo : MonoBehaviour
    {
        [SerializeField, Tooltip("Local scheduler shared by the player and every bot in the scene.")]
        private NavigationQuerySchedulerBehaviour navigation;
        [SerializeField, Tooltip("Size of the available demo arena along X/Z used to generate bot destinations.")]
        private Vector2 worldSize = new Vector2(28f, 20f);
        [SerializeField, Range(1, 64), Tooltip("Number of bots competing for the shared mobile query budget.")]
        private int botCount = 24;
        [SerializeField, Min(0.1f), Tooltip("Player movement speed along path points, in meters per second.")]
        private float playerMoveSpeed = 5f;
        [SerializeField, Min(0.1f), Tooltip("Bot movement speed along local routes.")]
        private float botMoveSpeed = 2.8f;
        [SerializeField, Range(16, 256), Tooltip("Number of pre-projected points bots pick destinations from, avoiding extra nearest-poly queries at play time.")]
        private int botTargetPoolSize = 96;
        [SerializeField, Tooltip("Initial navigation position of the player.")]
        private Vector3 playerStart = new Vector3(-11f, 0f, -7f);
        [SerializeField, Range(30f, 75f), Tooltip("Downward tilt of the isometric camera, in degrees.")]
        private float cameraPitch = 52f;
        [SerializeField, Range(-180f, 180f), Tooltip("Isometric camera rotation around the level, in degrees.")]
        private float cameraYaw = 45f;
        [SerializeField, Min(0f), Tooltip("Extra padding around the framed level, in world-space meters.")]
        private float cameraFramingPadding = 1.25f;

        private readonly List<AgentState> bots = new List<AgentState>();
        private readonly List<Mesh> generatedMeshes = new List<Mesh>();
        private readonly List<Material> generatedMaterials = new List<Material>();
        private readonly List<Vector3> botTargetPool = new List<Vector3>();
        private readonly System.Random random = new System.Random(1337);

        private AgentState player;
        private Camera worldCamera;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle badgeStyle;
        private GameObject playerDestinationMarker;
        private string playerStatus = "Pick a point on the arena";

        public void Configure(
            NavigationQuerySchedulerBehaviour scheduler,
            Vector2 size,
            int numberOfBots,
            Vector3 initialPlayerPosition)
        {
            navigation = scheduler;
            worldSize = size;
            botCount = numberOfBots;
            playerStart = initialPlayerPosition;
        }

        private void Start()
        {
            if (navigation == null || !navigation.IsReady)
            {
                Debug.LogError("[CustomNavigation] LocalOnly demo requires a ready query scheduler.", this);
                enabled = false;
                return;
            }

            worldCamera = NavigationDemoIsometricCameraRig.Create(
                transform,
                "LocalOnly isometric camera",
                new Bounds(
                    new Vector3(0f, 0.8f, 0f),
                    new Vector3(worldSize.x + 0.8f, 2f, worldSize.y + 0.8f)),
                new Color(0.018f, 0.025f, 0.035f, 1f),
                cameraPitch,
                cameraYaw,
                cameraFramingPadding).WorldCamera;
            Mesh agentMesh = NavigationDemoMeshFactory.CreateCylinder(0.36f, 0.82f);
            generatedMeshes.Add(agentMesh);
            Mesh markerMesh = NavigationDemoMeshFactory.CreateCylinder(0.16f, 0.08f);
            generatedMeshes.Add(markerMesh);
            Material playerMaterial = CreateMaterial(new Color(1f, 0.78f, 0.16f, 1f));
            Material combatMaterial = CreateMaterial(new Color(0.96f, 0.25f, 0.18f, 1f));
            Material visibleMaterial = CreateMaterial(new Color(0.2f, 0.76f, 1f, 1f));
            Material backgroundMaterial = CreateMaterial(new Color(0.48f, 0.56f, 0.65f, 1f));
            BuildBotTargetPool();

            Vector3 validPlayerStart = ProjectPositionOrFallback(playerStart, Vector3.zero);
            player = CreateAgent(
                "Local player",
                validPlayerStart,
                NavigationQueryPriority.PlayerImmediate,
                playerMoveSpeed,
                agentMesh,
                playerMaterial);
            playerDestinationMarker = CreateDestinationMarker(markerMesh, playerMaterial);

            for (int i = 0; i < botCount; i++)
            {
                NavigationQueryPriority priority = i < 4
                    ? NavigationQueryPriority.CombatBot
                    : i < 12
                        ? NavigationQueryPriority.VisibleBot
                        : NavigationQueryPriority.BackgroundBot;
                Material material = priority == NavigationQueryPriority.CombatBot
                    ? combatMaterial
                    : priority == NavigationQueryPriority.VisibleBot
                        ? visibleMaterial
                        : backgroundMaterial;
                Vector3 start = RandomNavigationPoint();
                AgentState bot = CreateAgent(
                    $"Bot {i + 1:00} [{priority}]",
                    start,
                    priority,
                    botMoveSpeed,
                    agentMesh,
                    material);
                bot.NextReplanTime = Time.unscaledTime + i * 0.06f;
                bots.Add(bot);
            }
        }

        private void Update()
        {
            ReadPlayerPointer();
            MoveAgent(player);

            for (int i = 0; i < bots.Count; i++)
            {
                AgentState bot = bots[i];
                MoveAgent(bot);
                if (!bot.RequestPending
                    && bot.WaypointIndex >= bot.Path.Count
                    && Time.unscaledTime >= bot.NextReplanTime)
                {
                    RequestBotPath(bot);
                }
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < generatedMeshes.Count; i++)
            {
                Destroy(generatedMeshes[i]);
            }

            for (int i = 0; i < generatedMaterials.Count; i++)
            {
                Destroy(generatedMaterials[i]);
            }
        }

        private void OnGUI()
        {
            EnsureGuiStyles();
            NavigationSchedulerMetrics metrics = navigation != null ? navigation.Metrics : default;
            using (NavigationDemoGuiScope gui = NavigationDemoPresentation.BeginSafeAreaGui())
            {
                NavigationDemoPresentation.DrawHeader(
                    gui,
                    "DotRecast LocalOnly — mobile bot budget",
                    "LMB / tap: player route\n" +
                    $"Player: {playerStatus}\n" +
                    $"Bots: {bots.Count}   active: {metrics.ActiveQueries}   queued: {metrics.QueuedQueries}\n" +
                    $"Navigation per frame: {metrics.LastFrameMilliseconds:0.###} ms / " +
                    $"{metrics.LastFrameIterations} iterations\n" +
                    $"Completed: {metrics.CompletedQueries}   Rejected: {metrics.RejectedQueries}",
                    "LOCAL ARTIFACT / NO RUNTIME BAKE",
                    titleStyle,
                    bodyStyle,
                    badgeStyle);
            }
        }

        private void ReadPlayerPointer()
        {
            if (player == null || worldCamera == null || player.RequestPending)
            {
                return;
            }

            bool pressed;
            Vector2 screenPosition;
            if (Mouse.current != null)
            {
                pressed = Mouse.current.leftButton.wasPressedThisFrame;
                screenPosition = Mouse.current.position.ReadValue();
            }
            else if (Touchscreen.current != null)
            {
                pressed = Touchscreen.current.primaryTouch.press.wasPressedThisFrame;
                screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            }
            else
            {
                return;
            }

            if (!pressed)
            {
                return;
            }

            if (!worldCamera.pixelRect.Contains(screenPosition))
            {
                return;
            }

            Ray ray = worldCamera.ScreenPointToRay(screenPosition);
            if (TryIntersectHeight(ray, 0f, out Vector3 destination)
                && IsInsideArena(destination)
                && navigation.TryProjectPosition(destination, out Vector3 projectedDestination))
            {
                playerStatus = $"destination X={projectedDestination.x:0.0}, Z={projectedDestination.z:0.0}";
                ShowDestinationMarker(projectedDestination);
                RequestPath(player, projectedDestination);
            }
        }

        private void RequestBotPath(AgentState bot)
        {
            RequestPath(bot, RandomNavigationPoint());
        }

        private void RequestPath(AgentState agent, Vector3 destination)
        {
            agent.RequestPending = true;
            navigation.RequestPath(
                AgentGroundPosition(agent),
                destination,
                agent.Priority,
                result => ApplyPath(agent, result));
        }

        private void ApplyPath(AgentState agent, NavigationPathResult result)
        {
            agent.RequestPending = false;
            agent.Path.Clear();
            agent.WaypointIndex = 0;
            if (!result.Success || result.Points.Length == 0)
            {
                agent.NextReplanTime = Time.unscaledTime + 0.25f;
                if (agent == player)
                {
                    playerStatus = "no route found";
                    playerDestinationMarker?.SetActive(false);
                    Debug.LogWarning($"[CustomNavigation] Player path failed: {result.Message}", this);
                }

                return;
            }

            agent.Path.AddRange(result.Points);
            agent.WaypointIndex = agent.Path.Count > 1 ? 1 : 0;
            agent.NextReplanTime = Time.unscaledTime + GetReplanInterval(agent.Priority);
            if (agent == player)
            {
                playerStatus = $"route: {result.Points.Length} points";
                ShowDestinationMarker(result.Points[result.Points.Length - 1]);
                Debug.Log(
                    $"[CustomNavigation] Player path accepted: points={result.Points.Length}, " +
                    $"latency={result.LatencyMilliseconds:0.###} ms, partial={result.IsPartial}.",
                    this);
            }
        }

        private void MoveAgent(AgentState agent)
        {
            if (agent == null || agent.WaypointIndex >= agent.Path.Count)
            {
                return;
            }

            Vector3 target = agent.Path[agent.WaypointIndex];
            Vector3 current = AgentGroundPosition(agent);
            Vector3 next = Vector3.MoveTowards(current, target, agent.MoveSpeed * Time.deltaTime);
            agent.Transform.position = next + Vector3.up * agent.HalfHeight;
            if ((next - target).sqrMagnitude <= 0.0025f)
            {
                agent.WaypointIndex++;
            }
        }

        private float GetReplanInterval(NavigationQueryPriority priority)
        {
            NavigationPerformanceProfile profile = navigation.Scheduler.PerformanceProfile;
            return priority switch
            {
                NavigationQueryPriority.CombatBot => profile.CombatBotMinimumReplanSeconds,
                NavigationQueryPriority.VisibleBot => profile.VisibleBotMinimumReplanSeconds,
                NavigationQueryPriority.BackgroundBot => profile.BackgroundBotMinimumReplanSeconds,
                _ => 0.1f
            };
        }

        private AgentState CreateAgent(
            string objectName,
            Vector3 groundPosition,
            NavigationQueryPriority priority,
            float moveSpeed,
            Mesh mesh,
            Material material)
        {
            const float height = 0.82f;
            var agentObject = new GameObject(objectName);
            agentObject.transform.SetParent(transform, false);
            agentObject.transform.position = groundPosition + Vector3.up * (height * 0.5f);
            agentObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            agentObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return new AgentState(
                agentObject.transform,
                priority,
                moveSpeed,
                height * 0.5f);
        }

        private Vector3 RandomPoint()
        {
            float x = ((float)random.NextDouble() - 0.5f) * (worldSize.x - 2f);
            float z = ((float)random.NextDouble() - 0.5f) * (worldSize.y - 2f);
            return new Vector3(x, 0f, z);
        }

        private Vector3 RandomNavigationPoint()
        {
            if (botTargetPool.Count > 0)
            {
                return botTargetPool[random.Next(botTargetPool.Count)];
            }

            return ProjectPositionOrFallback(Vector3.zero, playerStart);
        }

        private void BuildBotTargetPool()
        {
            const int maximumAttempts = 48;
            const float maximumHorizontalProjection = 0.2f;
            float maximumProjectionSquared = maximumHorizontalProjection * maximumHorizontalProjection;
            int attempts = Mathf.Max(maximumAttempts, botTargetPoolSize * 8);
            botTargetPool.Clear();
            for (int i = 0; i < attempts && botTargetPool.Count < botTargetPoolSize; i++)
            {
                Vector3 candidate = RandomPoint();
                if (!navigation.TryProjectPosition(candidate, out Vector3 projected))
                {
                    continue;
                }

                Vector2 offset = new Vector2(projected.x - candidate.x, projected.z - candidate.z);
                if (offset.sqrMagnitude <= maximumProjectionSquared)
                {
                    botTargetPool.Add(projected);
                }
            }

            if (botTargetPool.Count == 0)
            {
                botTargetPool.Add(ProjectPositionOrFallback(Vector3.zero, playerStart));
            }

            if (botTargetPool.Count < botTargetPoolSize)
            {
                Debug.LogWarning(
                    $"[CustomNavigation] Bot target pool contains {botTargetPool.Count}/" +
                    $"{botTargetPoolSize} valid navigation points.",
                    this);
            }
        }

        private Vector3 ProjectPositionOrFallback(Vector3 requested, Vector3 fallback)
        {
            if (navigation.TryProjectPosition(requested, out Vector3 projected))
            {
                return projected;
            }

            if (navigation.TryProjectPosition(fallback, out projected))
            {
                return projected;
            }

            throw new InvalidOperationException(
                "LocalOnly demo could not project a required point onto the navigation artifact.");
        }

        private static Vector3 AgentGroundPosition(AgentState agent)
        {
            return agent.Transform.position - Vector3.up * agent.HalfHeight;
        }

        private GameObject CreateDestinationMarker(Mesh mesh, Material material)
        {
            var marker = new GameObject("Player destination marker");
            marker.transform.SetParent(transform, false);
            marker.AddComponent<MeshFilter>().sharedMesh = mesh;
            marker.AddComponent<MeshRenderer>().sharedMaterial = material;
            marker.SetActive(false);
            return marker;
        }

        private void ShowDestinationMarker(Vector3 groundPosition)
        {
            if (playerDestinationMarker == null)
            {
                return;
            }

            playerDestinationMarker.transform.position = groundPosition + Vector3.up * 0.04f;
            playerDestinationMarker.SetActive(true);
        }

        private bool IsInsideArena(Vector3 point)
        {
            return Mathf.Abs(point.x) <= worldSize.x * 0.5f
                   && Mathf.Abs(point.z) <= worldSize.y * 0.5f;
        }

        private Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                throw new InvalidOperationException("No compatible unlit shader is available.");
            }

            var material = new Material(shader) { name = "Local navigation demo material" };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            generatedMaterials.Add(material);
            return material;
        }

        private static bool TryIntersectHeight(Ray ray, float height, out Vector3 point)
        {
            if (Mathf.Abs(ray.direction.y) < 0.0001f)
            {
                point = default;
                return false;
            }

            float distance = (height - ray.origin.y) / ray.direction.y;
            if (distance < 0f)
            {
                point = default;
                return false;
            }

            point = ray.origin + ray.direction * distance;
            return true;
        }

        private void EnsureGuiStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.82f, 0.88f, 0.92f) }
            };
            badgeStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.25f, 1f, 0.7f) }
            };
        }

        private sealed class AgentState
        {
            public readonly Transform Transform;
            public readonly NavigationQueryPriority Priority;
            public readonly float MoveSpeed;
            public readonly float HalfHeight;
            public readonly List<Vector3> Path = new List<Vector3>();
            public int WaypointIndex;
            public bool RequestPending;
            public float NextReplanTime;

            public AgentState(
                Transform transform,
                NavigationQueryPriority priority,
                float moveSpeed,
                float halfHeight)
            {
                Transform = transform;
                Priority = priority;
                MoveSpeed = moveSpeed;
                HalfHeight = halfHeight;
            }
        }
    }
}
