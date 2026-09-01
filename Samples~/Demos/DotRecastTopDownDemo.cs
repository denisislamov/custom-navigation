using System.Collections.Generic;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using CustomNavigation.UnityAdapter;
using Jitter2.LinearMath;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CustomNavigation
{
    /// <summary>
    /// Local artifact demo over geometry already serialized in the Unity scene.
    /// Runtime creates no level mesh and performs no Recast bake.
    /// </summary>
    public sealed class DotRecastTopDownDemo : MonoBehaviour
    {
        [SerializeField, Tooltip("Local scheduler that loads a prebuilt navmesh artifact without a runtime bake.")]
        private NavigationQuerySchedulerBehaviour navigation;
        [SerializeField, Tooltip("Size of the saved arena geometry along X/Z for point picking and camera framing.")]
        private Vector2 worldSize = new Vector2(28f, 20f);
        [SerializeField, Tooltip("Initial navigation position of the player in the saved level.")]
        private Vector3 initialAgentPosition = new Vector3(-11f, 0f, -7f);
        [SerializeField, Tooltip("First destination used to verify the prebuilt local artifact.")]
        private Vector3 initialDestination = new Vector3(11f, 0f, 7f);
        [SerializeField, Tooltip("Prebuilt mesh of the visual agent; no geometry is created at runtime.")]
        private Mesh agentMesh;
        [SerializeField, Tooltip("Material of the visual agent, saved as a Unity asset.")]
        private Material agentMaterial;
        [SerializeField, Tooltip("Material of the path line and the destination marker, saved as a Unity asset.")]
        private Material pathMaterial;

        private const float AgentVisualHeight = 0.9f;
        private const float MoveSpeed = 4.5f;

        private readonly List<JVector> path = new List<JVector>();
        private Transform agent;
        private Transform targetMarker;
        private Camera worldCamera;
        private LineRenderer pathLine;
        private int waypointIndex;
        private bool requestPending;
        private string status = "Starting...";
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle badgeStyle;

        public void Configure(
            NavigationQuerySchedulerBehaviour scheduler,
            Vector2 arenaSize,
            Vector3 start,
            Vector3 destination,
            Mesh staticAgentMesh,
            Material staticAgentMaterial,
            Material staticPathMaterial)
        {
            navigation = scheduler;
            worldSize = arenaSize;
            initialAgentPosition = start;
            initialDestination = destination;
            agentMesh = staticAgentMesh;
            agentMaterial = staticAgentMaterial;
            pathMaterial = staticPathMaterial;
        }

        private void Start()
        {
            if (navigation == null || !navigation.IsReady)
            {
                Debug.LogError("[CustomNavigation] Top-down demo requires a ready local artifact.", this);
                enabled = false;
                return;
            }

            if (agentMesh == null || agentMaterial == null || pathMaterial == null)
            {
                Debug.LogError(
                    "[CustomNavigation] Top-down demo requires serialized mesh/material assets.",
                    this);
                enabled = false;
                return;
            }

            worldCamera = NavigationDemoIsometricCameraRig.Create(
                transform,
                "Top-down isometric camera",
                new Bounds(
                    new Vector3(0f, 0.8f, 0f),
                    new Vector3(worldSize.x + 0.8f, 2f, worldSize.y + 0.8f)),
                new Color(0.018f, 0.025f, 0.035f, 1f)).WorldCamera;

            var agentObject = new GameObject("Transform-driven agent");
            agentObject.transform.SetParent(transform, false);
            agentObject.transform.position = initialAgentPosition + Vector3.up * (AgentVisualHeight * 0.5f);
            agentObject.transform.localScale = new Vector3(0.72f, 0.9f, 0.72f);
            agentObject.AddComponent<MeshFilter>().sharedMesh = agentMesh;
            agentObject.AddComponent<MeshRenderer>().sharedMaterial = agentMaterial;
            agent = agentObject.transform;

            pathLine = new GameObject("DotRecast straight path").AddComponent<LineRenderer>();
            pathLine.transform.SetParent(transform, false);
            pathLine.sharedMaterial = pathMaterial;
            pathLine.useWorldSpace = true;
            pathLine.startWidth = 0.15f;
            pathLine.endWidth = 0.15f;
            pathLine.numCapVertices = 4;
            pathLine.numCornerVertices = 4;

            targetMarker = CreateTargetMarker().transform;
            targetMarker.gameObject.SetActive(false);
            SetDestination(initialDestination);
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
                    "DotRecast — static isometric level",
                    "LMB / tap: pick a destination\n" +
                    "Geometry: saved in the scene; the runtime loads a prebuilt artifact\n" + status,
                    "LOCAL ARTIFACT / STATIC GEOMETRY",
                    titleStyle,
                    bodyStyle,
                    badgeStyle);
            }
        }

        private void ReadPointer()
        {
            if (requestPending || worldCamera == null || agent == null)
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
                SetDestination(groundPoint);
            }
        }

        private void SetDestination(Vector3 destination)
        {
            if (!navigation.TryProjectPosition(
                    NavigationUnityAdapter.ToJitter(destination),
                    out JVector projectedDestination))
            {
                ClearPath("No DotRecast surface for this point");
                return;
            }

            requestPending = true;
            status = "Calculating local route...";
            navigation.RequestPath(
                NavigationUnityAdapter.ToJitter(AgentGroundPosition()),
                projectedDestination,
                NavigationQueryPriority.PlayerImmediate,
                ApplyPath);
        }

        private void ApplyPath(NavigationPathResult result)
        {
            requestPending = false;
            path.Clear();
            if (!result.Success || result.Points.Length == 0)
            {
                ClearPath("No DotRecast path for this point");
                return;
            }

            path.AddRange(result.Points);
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
            status = $"Path: {path.Count} straight points";
        }

        private void ClearPath(string message)
        {
            requestPending = false;
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
                    status = "Destination reached";
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
            var marker = new GameObject("Selected destination");
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
            if (StableMath.Abs(ray.direction.y) < 0.0001f)
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
    }
}
