using System;
using System.Collections.Generic;
using CustomNavigation.Authoring;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CustomNavigation.Runtime
{
    [DisallowMultipleComponent]
    public sealed class MultiLevelNavigationDemo : MonoBehaviour
    {
        [SerializeField, Tooltip("Local scheduler that loads the multi-level navmesh artifact.")]
        private NavigationQuerySchedulerBehaviour navigation;
        [SerializeField, Tooltip("Player start on the lower floor.")]
        private Vector3 playerStart = new Vector3(-11f, 0f, -3f);
        [SerializeField, Tooltip("Starting destination on the upper floor to exercise both ramps.")]
        private Vector3 initialDestination = new Vector3(19f, 5f, 3f);
        [SerializeField, Min(0.1f), Tooltip("Player 3D movement speed along elevated path points.")]
        private float moveSpeed = 4.5f;
        [SerializeField, Tooltip("World-space vertices of walkable surfaces for physics-free destination picking.")]
        private Vector3[] selectionVertices = Array.Empty<Vector3>();
        [SerializeField, Tooltip("Triangle indices of walkable surfaces for the analytic ray intersection.")]
        private int[] selectionTriangles = Array.Empty<int>();

        private const float PlayerHeight = 0.9f;
        private const float PlayerHalfHeight = PlayerHeight * 0.5f;

        private readonly List<Vector3> path = new List<Vector3>();
        private readonly List<Mesh> generatedMeshes = new List<Mesh>();
        private readonly List<Material> generatedMaterials = new List<Material>();

        private Transform player;
        private Camera worldCamera;
        private LineRenderer pathLine;
        private Transform targetMarker;
        private int waypointIndex;
        private bool requestPending;
        private float routeMinimumHeight;
        private float routeMaximumHeight;
        private string status = "Preparing multi-level navigation...";
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle badgeStyle;

        public void Configure(
            NavigationQuerySchedulerBehaviour scheduler,
            Vector3 start,
            Vector3 destination,
            Vector3[] pickVertices,
            int[] pickTriangles)
        {
            navigation = scheduler;
            playerStart = start;
            initialDestination = destination;
            selectionVertices = pickVertices ?? Array.Empty<Vector3>();
            selectionTriangles = pickTriangles ?? Array.Empty<int>();
        }

        private void Start()
        {
            if (navigation == null || !navigation.IsReady)
            {
                Debug.LogError("[CustomNavigation] Multi-level demo requires a ready local scheduler.", this);
                enabled = false;
                return;
            }

            if (selectionVertices.Length == 0 || selectionTriangles.Length < 3)
            {
                Debug.LogError("[CustomNavigation] Multi-level selection geometry is empty.", this);
                enabled = false;
                return;
            }

            worldCamera = CreateCamera();
            Material playerMaterial = CreateMaterial(new Color(1f, 0.75f, 0.12f, 1f));
            Material pathMaterial = CreateMaterial(new Color(1f, 0.92f, 0.26f, 1f));
            Material targetMaterial = CreateMaterial(new Color(0.22f, 1f, 0.72f, 1f));

            Mesh playerMesh = NavigationDemoMeshFactory.CreateCylinder(0.38f, PlayerHeight, 20);
            Mesh targetMesh = NavigationDemoMeshFactory.CreateCylinder(0.5f, 0.08f, 24);
            generatedMeshes.Add(playerMesh);
            generatedMeshes.Add(targetMesh);

            var playerObject = new GameObject("Multi-level player");
            playerObject.transform.SetParent(transform, false);
            playerObject.transform.position = playerStart + Vector3.up * PlayerHalfHeight;
            playerObject.AddComponent<MeshFilter>().sharedMesh = playerMesh;
            playerObject.AddComponent<MeshRenderer>().sharedMaterial = playerMaterial;
            player = playerObject.transform;

            pathLine = new GameObject("Multi-level route").AddComponent<LineRenderer>();
            pathLine.transform.SetParent(transform, false);
            pathLine.useWorldSpace = true;
            pathLine.startWidth = 0.16f;
            pathLine.endWidth = 0.16f;
            pathLine.numCapVertices = 4;
            pathLine.numCornerVertices = 4;
            pathLine.sharedMaterial = pathMaterial;

            var markerObject = new GameObject("Selected destination");
            markerObject.transform.SetParent(transform, false);
            markerObject.AddComponent<MeshFilter>().sharedMesh = targetMesh;
            markerObject.AddComponent<MeshRenderer>().sharedMaterial = targetMaterial;
            markerObject.SetActive(false);
            targetMarker = markerObject.transform;

            RequestPath(initialDestination);
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
            NavigationSchedulerMetrics metrics = navigation != null ? navigation.Metrics : default;
            float currentHeight = player != null ? PlayerNavigationPosition().y : 0f;
            using (NavigationDemoGuiScope gui = NavigationDemoPresentation.BeginSafeAreaGui())
            {
                NavigationDemoPresentation.DrawHeader(
                    gui,
                    "DotRecast — multi-level navigation",
                    "LMB / tap: a point on a platform or a ramp\n" +
                    status + "\n" +
                    $"Height: {currentHeight:0.00} m   " +
                    $"route: {routeMinimumHeight:0.00} -> {routeMaximumHeight:0.00} m\n" +
                    $"Scheduler: {metrics.LastFrameMilliseconds:0.###} ms / " +
                    $"{metrics.LastFrameIterations} iterations",
                    "AUTO 3D / SLOPED TRANSITIONS",
                    titleStyle,
                    bodyStyle,
                    badgeStyle);
            }
        }

        private void ReadPointer()
        {
            if (worldCamera == null || player == null || requestPending)
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
            if (TrySelectSurface(ray, out Vector3 destination))
            {
                RequestPath(destination);
            }
        }

        private void RequestPath(Vector3 destination)
        {
            requestPending = true;
            status = $"Calculating route to Y={destination.y:0.00} m...";
            navigation.RequestPath(
                PlayerNavigationPosition(),
                destination,
                NavigationQueryPriority.PlayerImmediate,
                result => ApplyPath(destination, result));
        }

        private void ApplyPath(Vector3 requestedDestination, NavigationPathResult result)
        {
            requestPending = false;
            path.Clear();
            waypointIndex = 0;
            if (!result.Success || result.Points.Length == 0)
            {
                status = "Route was not found: " + result.Message;
                Debug.LogWarning(
                    $"[CustomNavigation] Multi-level route failed: {result.Message}; " +
                    $"destination={requestedDestination}.",
                    this);
                return;
            }

            routeMinimumHeight = float.PositiveInfinity;
            routeMaximumHeight = float.NegativeInfinity;
            for (int i = 0; i < result.Points.Length; i++)
            {
                Vector3 point = result.Points[i];
                path.Add(point);
                routeMinimumHeight = Mathf.Min(routeMinimumHeight, point.y);
                routeMaximumHeight = Mathf.Max(routeMaximumHeight, point.y);
            }

            waypointIndex = path.Count > 1 ? 1 : 0;
            pathLine.positionCount = path.Count;
            for (int i = 0; i < path.Count; i++)
            {
                pathLine.SetPosition(i, path[i] + Vector3.up * 0.13f);
            }

            Vector3 destination = path[path.Count - 1];
            targetMarker.position = destination + Vector3.up * 0.04f;
            targetMarker.gameObject.SetActive(true);
            status = $"Route ready: {path.Count} points, " +
                     $"Y {routeMinimumHeight:0.00}–{routeMaximumHeight:0.00} m.";
            Debug.Log(
                $"[CustomNavigation] Multi-level route ready: points={path.Count}, " +
                $"height={routeMinimumHeight:0.###}..{routeMaximumHeight:0.###}, " +
                $"iterations={result.Iterations}, latency={result.LatencyMilliseconds:0.###} ms.",
                this);
        }

        private void MovePlayer()
        {
            if (player == null || waypointIndex >= path.Count)
            {
                return;
            }

            Vector3 current = PlayerNavigationPosition();
            Vector3 target = path[waypointIndex];
            Vector3 next = Vector3.MoveTowards(current, target, moveSpeed * Time.deltaTime);
            player.position = next + Vector3.up * PlayerHalfHeight;

            Vector3 facing = target - current;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.001f)
            {
                player.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            }

            if ((next - target).sqrMagnitude <= 0.0025f)
            {
                waypointIndex++;
            }
        }

        private Vector3 PlayerNavigationPosition()
        {
            return player != null
                ? player.position - Vector3.up * PlayerHalfHeight
                : playerStart;
        }

        private bool TrySelectSurface(Ray ray, out Vector3 selectedPoint)
        {
            bool found = false;
            float nearestDistance = float.PositiveInfinity;
            selectedPoint = default;
            for (int i = 0; i + 2 < selectionTriangles.Length; i += 3)
            {
                int first = selectionTriangles[i];
                int second = selectionTriangles[i + 1];
                int third = selectionTriangles[i + 2];
                if (first < 0 || first >= selectionVertices.Length
                    || second < 0 || second >= selectionVertices.Length
                    || third < 0 || third >= selectionVertices.Length)
                {
                    continue;
                }

                if (!TryIntersectTriangle(
                        ray,
                        selectionVertices[first],
                        selectionVertices[second],
                        selectionVertices[third],
                        out float distance)
                    || distance >= nearestDistance)
                {
                    continue;
                }

                nearestDistance = distance;
                selectedPoint = ray.origin + ray.direction * distance;
                found = true;
            }

            return found;
        }

        private static bool TryIntersectTriangle(
            Ray ray,
            Vector3 first,
            Vector3 second,
            Vector3 third,
            out float distance)
        {
            const float epsilon = 0.000001f;
            Vector3 edgeA = second - first;
            Vector3 edgeB = third - first;
            Vector3 cross = Vector3.Cross(ray.direction, edgeB);
            float determinant = Vector3.Dot(edgeA, cross);
            if (Mathf.Abs(determinant) < epsilon)
            {
                distance = 0f;
                return false;
            }

            float inverse = 1f / determinant;
            Vector3 originOffset = ray.origin - first;
            float coordinateA = Vector3.Dot(originOffset, cross) * inverse;
            if (coordinateA < 0f || coordinateA > 1f)
            {
                distance = 0f;
                return false;
            }

            Vector3 secondCross = Vector3.Cross(originOffset, edgeA);
            float coordinateB = Vector3.Dot(ray.direction, secondCross) * inverse;
            if (coordinateB < 0f || coordinateA + coordinateB > 1f)
            {
                distance = 0f;
                return false;
            }

            distance = Vector3.Dot(edgeB, secondCross) * inverse;
            return distance > epsilon;
        }

        private Camera CreateCamera()
        {
            Bounds bounds = new Bounds(selectionVertices[0], Vector3.zero);
            for (int i = 1; i < selectionVertices.Length; i++)
            {
                bounds.Encapsulate(selectionVertices[i]);
            }

            bounds.Expand(new Vector3(1.5f, 2f, 1.5f));
            return NavigationDemoIsometricCameraRig.Create(
                transform,
                "Multi-level isometric camera",
                bounds,
                new Color(0.014f, 0.022f, 0.035f, 1f)).WorldCamera;
        }

        private Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                throw new InvalidOperationException("No compatible unlit shader is available.");
            }

            var material = new Material(shader) { name = "Multi-level runtime material" };
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
                normal = { textColor = new Color(0.82f, 0.88f, 0.94f) }
            };
            badgeStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.25f, 1f, 0.72f) }
            };
        }
    }
}
