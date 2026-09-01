using System;
using System.Collections;
using System.Collections.Generic;
using CustomNavigation.Runtime;
using CustomNavigation.UnityAdapter;
using Jitter2.LinearMath;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace CustomNavigation
{
    /// <summary>
    /// Thin HTTP client over geometry already serialized in the Unity scene.
    /// The server validates and queries the artifact exported from that same geometry.
    /// </summary>
    public sealed class ServerNavigationTopDownDemo : MonoBehaviour
    {
        [SerializeField, Tooltip("Base URL of the standalone navigation server for health and path requests.")]
        private string serverUrl = "http://127.0.0.1:5079";
        [SerializeField, Tooltip("Level id of the saved Unity scene expected from the server /health response.")]
        private string expectedLevelId = "local_bots_arena";
        [SerializeField, Tooltip("SHA-256 of the navmesh artifact exported from this scene geometry.")]
        private string expectedArtifactHash;
        [SerializeField, Tooltip("Size of the saved arena geometry along X/Z for point picking and camera framing.")]
        private Vector2 worldSize = new Vector2(28f, 20f);
        [SerializeField, Tooltip("Initial navigation position of the server agent.")]
        private Vector3 agentStart = new Vector3(-11f, 0f, -7f);
        [SerializeField, Tooltip("First destination sent in the authoritative server query.")]
        private Vector3 initialDestination = new Vector3(11f, 0f, 7f);
        [SerializeField, Tooltip("Prebuilt mesh of the visual agent; no geometry is created at runtime.")]
        private Mesh agentMesh;
        [SerializeField, Tooltip("Material of the visual agent, saved as a Unity asset.")]
        private Material agentMaterial;
        [SerializeField, Tooltip("Material of the server path and the destination marker, saved as a Unity asset.")]
        private Material pathMaterial;

        private const float AgentVisualHeight = 0.9f;
        private const float MoveSpeed = 4.5f;

        private readonly List<JVector> path = new List<JVector>();
        private Transform agent;
        private Transform targetMarker;
        private Camera worldCamera;
        private LineRenderer pathLine;
        private int waypointIndex;
        private int requestVersion;
        private bool serverReady;
        private string status = "Connecting to navigation server...";
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle badgeStyle;

        public void Configure(
            string levelId,
            string artifactHash,
            Vector2 arenaSize,
            Vector3 start,
            Vector3 destination,
            Mesh staticAgentMesh,
            Material staticAgentMaterial,
            Material staticPathMaterial)
        {
            expectedLevelId = levelId;
            expectedArtifactHash = artifactHash;
            worldSize = arenaSize;
            agentStart = start;
            initialDestination = destination;
            agentMesh = staticAgentMesh;
            agentMaterial = staticAgentMaterial;
            pathMaterial = staticPathMaterial;
        }

        private void Awake()
        {
            serverUrl = NavigationServerRuntimeSettings.CurrentUrl;
        }

        private IEnumerator Start()
        {
            if (agentMesh == null || agentMaterial == null || pathMaterial == null)
            {
                Debug.LogError(
                    "[CustomNavigation] Server client requires serialized mesh/material assets.",
                    this);
                enabled = false;
                yield break;
            }

            BuildStaticPresentation();
            yield return ValidateServerArtifact();
            if (serverReady)
            {
                yield return RequestPath(initialDestination);
            }
        }

        private void Update()
        {
            ReadPointer();
            MoveAgent();
        }

        private void OnGUI()
        {
            EnsureGuiStyles();
            using (NavigationDemoGuiScope gui = NavigationDemoPresentation.BeginSafeAreaGui())
            {
                NavigationDemoPresentation.DrawHeader(
                    gui,
                    "DotRecast server — static Unity level",
                    $"Server: {serverUrl}\n" +
                    "Geometry is saved in the client scene; the server holds the same artifact\n" +
                    "LMB / tap: request an authoritative path\n" + status,
                    "SERVER PATH / STATIC GEOMETRY",
                    titleStyle,
                    bodyStyle,
                    badgeStyle);
            }
        }

        private IEnumerator ValidateServerArtifact()
        {
            using (UnityWebRequest request = UnityWebRequest.Get(BuildUrl("/health")))
            {
                request.timeout = 5;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    status = "Server unavailable: " + request.error;
                    yield break;
                }

                ServerHealthResponse response;
                try
                {
                    response = JsonUtility.FromJson<ServerHealthResponse>(request.downloadHandler.text);
                }
                catch (Exception exception)
                {
                    status = "Invalid /health response: " + exception.Message;
                    yield break;
                }

                if (response == null
                    || !string.Equals(response.status, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    status = "Invalid /health response";
                    yield break;
                }

                if (!string.Equals(response.levelId, expectedLevelId, StringComparison.Ordinal)
                    || !string.Equals(response.artifactHash, expectedArtifactHash, StringComparison.Ordinal))
                {
                    status = $"[MISMATCH] Scene={expectedLevelId}/{ShortHash(expectedArtifactHash)}, " +
                             $"server={response.levelId}/{ShortHash(response.artifactHash)}";
                    Debug.LogWarning("[CustomNavigation] " + status, this);
                    yield break;
                }

                serverReady = true;
                status = $"Artifact verified: {response.levelId}/{ShortHash(response.artifactHash)}";
            }
        }

        private IEnumerator RequestPath(Vector3 destination)
        {
            if (!serverReady || agent == null)
            {
                yield break;
            }

            int version = ++requestVersion;
            waypointIndex = path.Count;
            NavigationServerPathResult result = null;
            yield return NavigationServerPathClient.RequestPath(
                serverUrl,
                $"server-scene-{version}-{Guid.NewGuid():N}",
                expectedLevelId,
                NavigationUnityAdapter.ToJitter(AgentGroundPosition()),
                NavigationUnityAdapter.ToJitter(destination),
                expectedArtifactHash,
                string.Empty,
                value => result = value);

            if (version != requestVersion)
            {
                yield break;
            }

            if (result == null || !result.Success || result.Points.Length == 0)
            {
                ClearPath(result != null && !string.IsNullOrEmpty(result.Message)
                    ? result.Message
                    : "Server returned no route");
                yield break;
            }

            if (!string.Equals(result.ArtifactHash, expectedArtifactHash, StringComparison.Ordinal))
            {
                string mismatch = $"[MISMATCH] /path artifact={ShortHash(result.ArtifactHash)}, " +
                                  $"scene={ShortHash(expectedArtifactHash)}";
                Debug.LogWarning("[CustomNavigation] " + mismatch, this);
                ClearPath(mismatch);
                yield break;
            }

            ApplyServerPath(result.Points, result.Message);
        }

        private void ApplyServerPath(JVector[] points, string message)
        {
            path.Clear();
            for (int i = 0; i < points.Length; i++)
            {
                path.Add(points[i]);
            }

            if (path.Count == 0)
            {
                ClearPath("Server returned no usable route points");
                return;
            }

            waypointIndex = path.Count > 1 ? 1 : 0;
            pathLine.positionCount = path.Count;
            for (int i = 0; i < path.Count; i++)
            {
                pathLine.SetPosition(
                    i,
                    NavigationUnityAdapter.ToUnity(path[i]) + Vector3.up * 0.13f);
            }

            Vector3 selectedPoint = NavigationUnityAdapter.ToUnity(path[path.Count - 1]);
            targetMarker.position = selectedPoint + Vector3.up * 0.14f;
            targetMarker.gameObject.SetActive(true);
            status = string.IsNullOrEmpty(message)
                ? $"Server route: {path.Count} straight points"
                : $"{message} ({path.Count} points)";
        }

        private void ClearPath(string message)
        {
            path.Clear();
            waypointIndex = 0;
            if (pathLine != null)
            {
                pathLine.positionCount = 0;
            }
            if (targetMarker != null)
            {
                targetMarker.gameObject.SetActive(false);
            }
            status = message;
        }

        private void BuildStaticPresentation()
        {
            worldCamera = NavigationDemoIsometricCameraRig.Create(
                transform,
                "Server client isometric camera",
                new Bounds(
                    new Vector3(0f, 0.8f, 0f),
                    new Vector3(worldSize.x + 0.8f, 2f, worldSize.y + 0.8f)),
                new Color(0.018f, 0.025f, 0.035f, 1f)).WorldCamera;

            var agentObject = new GameObject("Server-driven agent");
            agentObject.transform.SetParent(transform, false);
            agentObject.transform.position = agentStart + Vector3.up * (AgentVisualHeight * 0.5f);
            agentObject.transform.localScale = new Vector3(0.72f, 0.9f, 0.72f);
            agentObject.AddComponent<MeshFilter>().sharedMesh = agentMesh;
            agentObject.AddComponent<MeshRenderer>().sharedMaterial = agentMaterial;
            agent = agentObject.transform;

            pathLine = new GameObject("Server route response").AddComponent<LineRenderer>();
            pathLine.transform.SetParent(transform, false);
            pathLine.sharedMaterial = pathMaterial;
            pathLine.useWorldSpace = true;
            pathLine.startWidth = 0.15f;
            pathLine.endWidth = 0.15f;
            pathLine.numCapVertices = 4;
            pathLine.numCornerVertices = 4;

            targetMarker = CreateTargetMarker().transform;
            targetMarker.gameObject.SetActive(false);
        }

        private void ReadPointer()
        {
            if (!serverReady || worldCamera == null || agent == null)
            {
                return;
            }

            Vector2 screenPosition;
            bool pressed;
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

            if (!pressed || !worldCamera.pixelRect.Contains(screenPosition))
            {
                return;
            }

            Ray pointerRay = worldCamera.ScreenPointToRay(screenPosition);
            if (TryIntersectGround(pointerRay, out Vector3 groundPoint))
            {
                StartCoroutine(RequestPath(groundPoint));
            }
        }

        private void MoveAgent()
        {
            if (agent == null || waypointIndex >= path.Count)
            {
                return;
            }

            Vector3 waypoint = NavigationUnityAdapter.ToUnity(path[waypointIndex])
                               + Vector3.up * (AgentVisualHeight * 0.5f);
            Vector3 offset = waypoint - agent.position;
            offset.y = 0f;
            if (offset.sqrMagnitude < 0.0025f)
            {
                agent.position = waypoint;
                waypointIndex++;
                if (waypointIndex >= path.Count)
                {
                    status = "Destination reached using the server route";
                }
                return;
            }

            agent.position = Vector3.MoveTowards(agent.position, waypoint, MoveSpeed * Time.deltaTime);
            agent.rotation = Quaternion.Slerp(
                agent.rotation,
                Quaternion.LookRotation(offset.normalized, Vector3.up),
                12f * Time.deltaTime);
        }

        private Vector3 AgentGroundPosition()
        {
            return agent.position - Vector3.up * (AgentVisualHeight * 0.5f);
        }

        private GameObject CreateTargetMarker()
        {
            var marker = new GameObject("Server destination");
            marker.transform.SetParent(transform, false);
            var line = marker.AddComponent<LineRenderer>();
            line.sharedMaterial = pathMaterial;
            line.useWorldSpace = false;
            line.loop = true;
            line.startWidth = 0.1f;
            line.endWidth = 0.1f;
            line.positionCount = 40;
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = Mathf.PI * 2f * i / line.positionCount;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * 0.55f, 0f, Mathf.Sin(angle) * 0.55f));
            }
            return marker;
        }

        private static bool TryIntersectGround(Ray ray, out Vector3 point)
        {
            if (Mathf.Abs(ray.direction.y) < 0.0001f)
            {
                point = default;
                return false;
            }

            float distance = -ray.origin.y / ray.direction.y;
            if (distance < 0f)
            {
                point = default;
                return false;
            }

            point = ray.origin + ray.direction * distance;
            return true;
        }

        private string BuildUrl(string pathValue)
        {
            return serverUrl.TrimEnd('/') + pathValue;
        }

        private static string ShortHash(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "none";
            }
            return value.Length <= 12 ? value : value.Substring(0, 12);
        }

        private void EnsureGuiStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = new Color(0.82f, 0.9f, 0.95f, 1f) }
            };
            badgeStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.2f, 1f, 0.72f, 1f) }
            };
        }

        [Serializable]
        private sealed class ServerHealthResponse
        {
            public string status;
            public string levelId;
            public string artifactHash;
        }

    }
}
