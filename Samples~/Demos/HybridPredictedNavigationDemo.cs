using System;
using System.Collections;
using System.Collections.Generic;
using CustomNavigation.Authoring;
using CustomNavigation.UnityAdapter;
using Jitter2.LinearMath;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CustomNavigation.Runtime
{
    [DisallowMultipleComponent]
    public sealed class HybridPredictedNavigationDemo : MonoBehaviour
    {
        [SerializeField, Tooltip("Local budgeted scheduler used for prediction until the server answers.")]
        private NavigationQuerySchedulerBehaviour localNavigation;
        [SerializeField, Tooltip("Base URL of the authoritative standalone navigation server.")]
        private string serverBaseUrl = "http://127.0.0.1:5079";
        [SerializeField, Tooltip("Initial navigation position of the player.")]
        private Vector3 playerStart = new Vector3(-11f, 0f, -7f);
        [SerializeField, Tooltip("First destination for which the local and server paths are compared automatically.")]
        private Vector3 initialDestination = new Vector3(11f, 0f, 7f);
        [SerializeField, Min(0.1f), Tooltip("Movement speed along the predicted/authoritative path, in meters per second.")]
        private float moveSpeed = 4.5f;

        private readonly List<JVector> activePath = new List<JVector>();
        private readonly List<Mesh> generatedMeshes = new List<Mesh>();
        private readonly List<Material> generatedMaterials = new List<Material>();

        private Transform player;
        private Camera worldCamera;
        private LineRenderer pathLine;
        private Transform targetMarker;
        private Material localPathMaterial;
        private Material serverPathMaterial;
        private int waypointIndex;
        private int requestVersion;
        private string status = "Starting...";
        private string lastLocalFingerprint = "none";
        private string lastServerFingerprint = "none";
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle badgeStyle;

        public void Configure(
            NavigationQuerySchedulerBehaviour scheduler,
            string baseUrl,
            Vector3 start,
            Vector3 destination)
        {
            localNavigation = scheduler;
            serverBaseUrl = baseUrl;
            playerStart = start;
            initialDestination = destination;
        }

        private void Start()
        {
            serverBaseUrl = NavigationServerRuntimeSettings.CurrentUrl;
            if (localNavigation == null || !localNavigation.IsReady)
            {
                Debug.LogError("[CustomNavigation] HybridPredicted demo requires local navigation.", this);
                enabled = false;
                return;
            }

            worldCamera = CreateCamera();
            localPathMaterial = CreateMaterial(new Color(1f, 0.82f, 0.18f, 1f));
            serverPathMaterial = CreateMaterial(new Color(0.25f, 1f, 0.7f, 1f));
            Material playerMaterial = CreateMaterial(new Color(1f, 0.46f, 0.12f, 1f));
            Mesh playerMesh = NavigationDemoMeshFactory.CreateCylinder(0.42f, 0.9f);
            generatedMeshes.Add(playerMesh);

            var playerObject = new GameObject("Hybrid predicted player");
            playerObject.transform.SetParent(transform, false);
            playerObject.transform.position = playerStart + Vector3.up * 0.45f;
            playerObject.AddComponent<MeshFilter>().sharedMesh = playerMesh;
            playerObject.AddComponent<MeshRenderer>().sharedMaterial = playerMaterial;
            player = playerObject.transform;

            pathLine = new GameObject("Predicted / authoritative route").AddComponent<LineRenderer>();
            pathLine.transform.SetParent(transform, false);
            pathLine.useWorldSpace = true;
            pathLine.startWidth = 0.15f;
            pathLine.endWidth = 0.15f;
            pathLine.numCapVertices = 4;
            pathLine.numCornerVertices = 4;
            targetMarker = CreateTargetMarker(localPathMaterial).transform;

            RequestDestination(initialDestination);
        }

        private void Update()
        {
            ReadPointer();
            MovePlayer();
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
            using (NavigationDemoGuiScope gui = NavigationDemoPresentation.BeginSafeAreaGui())
            {
                NavigationDemoPresentation.DrawHeader(
                    gui,
                    "DotRecast HybridPredicted",
                    $"Server: {serverBaseUrl}\n" +
                    "LMB / tap: local-first with server confirmation\n" +
                    status + "\n" +
                    $"Local path: {ShortHash(lastLocalFingerprint)}   " +
                    $"Server path: {ShortHash(lastServerFingerprint)}",
                    "LOCAL FIRST / SERVER AUTHORITATIVE",
                    titleStyle,
                    bodyStyle,
                    badgeStyle);
            }
        }

        private void ReadPointer()
        {
            if (worldCamera == null || player == null)
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
            if (TryIntersectHeight(ray, 0f, out Vector3 destination))
            {
                RequestDestination(destination);
            }
        }

        private void RequestDestination(Vector3 destination)
        {
            int version = ++requestVersion;
            string requestId = $"unity-{version}-{Guid.NewGuid():N}";
            JVector requestStart = NavigationUnityAdapter.ToJitter(PlayerGroundPosition());
            JVector canonicalDestination = NavigationUnityAdapter.ToJitter(destination);
            status = $"[{requestId}] calculating local prediction...";
            localNavigation.RequestPath(
                requestStart,
                canonicalDestination,
                NavigationQueryPriority.PlayerImmediate,
                result => OnLocalPath(version, requestId, requestStart, canonicalDestination, result));
        }

        private void OnLocalPath(
            int version,
            string requestId,
            JVector requestStart,
            JVector destination,
            NavigationPathResult result)
        {
            if (version != requestVersion)
            {
                return;
            }

            string localFingerprint;
            if (result.Success && result.Points.Length > 0)
            {
                localFingerprint = NavigationPathFingerprint.Compute(result.Points);
                ApplyPath(result.Points, localPathMaterial);
                status = $"[{requestId}] local route active; waiting for server...";
            }
            else
            {
                localFingerprint = "local-failed";
                status = $"[{requestId}] local route failed; asking server...";
                Debug.LogWarning(
                    $"[CustomNavigation] [{requestId}] Local prediction failed: " +
                    $"{result.Message}, iterations={result.Iterations}, " +
                    $"latency={result.LatencyMilliseconds:0.###} ms.",
                    this);
            }

            lastLocalFingerprint = localFingerprint;
            StartCoroutine(RequestServerPath(
                version,
                requestId,
                requestStart,
                destination,
                localFingerprint,
                result.Success));
        }

        private IEnumerator RequestServerPath(
            int version,
            string requestId,
            JVector requestStart,
            JVector destination,
            string localFingerprint,
            bool localSucceeded)
        {
            NavigationServerPathResult serverResult = null;
            yield return NavigationServerPathClient.RequestPath(
                serverBaseUrl,
                requestId,
                requestStart,
                destination,
                localNavigation.Scheduler.Artifact.ArtifactHash,
                localFingerprint,
                value => serverResult = value);

            if (version != requestVersion)
            {
                yield break;
            }

            if (serverResult == null || !serverResult.Success)
            {
                string error = serverResult?.Message ?? "No server response.";
                status = $"[{requestId}] server unavailable; local route remains active: {error}";
                Debug.LogWarning(
                    $"[CustomNavigation] [{requestId}] Authoritative navigation unavailable; " +
                    $"continuing local prediction. Error: {error}",
                    this);
                yield break;
            }

            JVector[] serverPoints = serverResult.Points;
            string calculatedServerFingerprint = serverResult.PathFingerprint;
            lastServerFingerprint = calculatedServerFingerprint;
            string localArtifact = localNavigation.Scheduler.Artifact.ArtifactHash;
            bool artifactMismatch = !string.Equals(
                localArtifact,
                serverResult.ArtifactHash,
                StringComparison.OrdinalIgnoreCase);
            bool pathMismatch = !string.Equals(
                localFingerprint,
                calculatedServerFingerprint,
                StringComparison.OrdinalIgnoreCase);
            bool responseFingerprintMismatch = !string.Equals(
                serverResult.PathFingerprint,
                calculatedServerFingerprint,
                StringComparison.OrdinalIgnoreCase);
            bool mismatch = !localSucceeded
                            || artifactMismatch
                            || pathMismatch
                            || responseFingerprintMismatch
                            || serverResult.ServerMismatchDetected;

            if (mismatch)
            {
                Debug.LogWarning(
                    $"[CustomNavigation] [MISMATCH] [{requestId}] Applying authoritative correction. " +
                    $"localArtifact={localArtifact}, serverArtifact={serverResult.ArtifactHash}, " +
                    $"localPath={localFingerprint}, serverPath={calculatedServerFingerprint}, " +
                    $"serverReported={serverResult.ServerMismatchDetected}.",
                    this);
                ApplyPath(serverPoints, serverPathMaterial);
                status = $"[{requestId}] WARNING: mismatch detected; server route applied.";
            }
            else
            {
                pathLine.sharedMaterial = serverPathMaterial;
                status = $"[{requestId}] server confirmed local route ({serverPoints.Length} points).";
            }
        }

        private void ApplyPath(IReadOnlyList<JVector> points, Material material)
        {
            activePath.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                activePath.Add(points[i]);
            }

            waypointIndex = activePath.Count > 1 ? 1 : 0;
            pathLine.sharedMaterial = material;
            pathLine.positionCount = activePath.Count;
            for (int i = 0; i < activePath.Count; i++)
            {
                pathLine.SetPosition(
                    i,
                    NavigationUnityAdapter.ToUnity(activePath[i]) + Vector3.up * 0.13f);
            }

            Vector3 destination = NavigationUnityAdapter.ToUnity(activePath[activePath.Count - 1]);
            targetMarker.position = destination + Vector3.up * 0.14f;
            targetMarker.gameObject.SetActive(true);
        }

        private void MovePlayer()
        {
            if (player == null || waypointIndex >= activePath.Count)
            {
                return;
            }

            Vector3 target = NavigationUnityAdapter.ToUnity(activePath[waypointIndex]);
            Vector3 next = Vector3.MoveTowards(
                PlayerGroundPosition(),
                target,
                moveSpeed * Time.deltaTime);
            player.position = next + Vector3.up * 0.45f;
            if ((next - target).sqrMagnitude <= 0.0025f)
            {
                waypointIndex++;
            }
        }

        private Vector3 PlayerGroundPosition()
        {
            Vector3 value = player.position;
            value.y = 0f;
            return value;
        }

        private Camera CreateCamera()
        {
            return NavigationDemoIsometricCameraRig.Create(
                transform,
                "Hybrid isometric camera",
                new Bounds(new Vector3(0f, 0.8f, 0f), new Vector3(28.8f, 2f, 20.8f)),
                new Color(0.018f, 0.025f, 0.035f, 1f)).WorldCamera;
        }

        private GameObject CreateTargetMarker(Material material)
        {
            var marker = new GameObject("Hybrid destination");
            marker.transform.SetParent(transform, false);
            var line = marker.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = true;
            line.startWidth = 0.1f;
            line.endWidth = 0.1f;
            line.positionCount = 40;
            for (int i = 0; i < line.positionCount; i++)
            {
                float angle = Mathf.PI * 2f * i / line.positionCount;
                line.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * 0.55f,
                    0f,
                    Mathf.Sin(angle) * 0.55f));
            }

            marker.SetActive(false);
            return marker;
        }

        private Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                throw new InvalidOperationException("No compatible unlit shader is available.");
            }

            var material = new Material(shader) { name = "Hybrid navigation material" };
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

        private static string ShortHash(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "none"
                : value.Length <= 12
                    ? value
                    : value.Substring(0, 12);
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

    }
}
