using System;
using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Navigation Scene View tools: the agent reference, the Path Probe points,
    /// and drawing of the last found route and analysis results.
    ///
    /// Key principle: there are NO computations here. Handles only move points,
    /// drawing comes from the cache. Every computation starts from a button in the Navigation window.
    /// </summary>
    [InitializeOnLoad]
    internal static class NavigationSceneTools
    {
        private const string PrefsPrefix = "CustomNavigation.SceneTools.";

        private static readonly Color StartColor = new Color(0.35f, 1f, 0.55f, 1f);
        private static readonly Color DestinationColor = new Color(1f, 0.45f, 0.3f, 1f);
        private static readonly Color LocalPathColor = new Color(0.3f, 0.9f, 1f, 1f);
        private static readonly Color ServerPathColor = new Color(1f, 0.85f, 0.2f, 1f);
        private static readonly Color AgentColor = new Color(0.2f, 0.8f, 1f, 1f);

        private static Material analysisMaterial;

        // -- State (survives a domain reload through SessionState) --------------
        public static bool ProbeEnabled
        {
            get => SessionState.GetBool(PrefsPrefix + "ProbeEnabled", false);
            set
            {
                SessionState.SetBool(PrefsPrefix + "ProbeEnabled", value);
                SceneView.RepaintAll();
            }
        }

        public static bool AgentPreviewEnabled
        {
            get => SessionState.GetBool(PrefsPrefix + "AgentPreview", false);
            set
            {
                SessionState.SetBool(PrefsPrefix + "AgentPreview", value);
                SceneView.RepaintAll();
            }
        }

        public static bool ShowAnalysis
        {
            get => SessionState.GetBool(PrefsPrefix + "ShowAnalysis", true);
            set
            {
                SessionState.SetBool(PrefsPrefix + "ShowAnalysis", value);
                SceneView.RepaintAll();
            }
        }

        public static Vector3 ProbeStart
        {
            get => GetVector("ProbeStart", new Vector3(-5f, 0f, -5f));
            set => SetVector("ProbeStart", value);
        }

        public static Vector3 ProbeDestination
        {
            get => GetVector("ProbeDestination", new Vector3(5f, 0f, 5f));
            set => SetVector("ProbeDestination", value);
        }

        public static Vector3 AgentPreviewPosition
        {
            get => GetVector("AgentPreview.Position", Vector3.zero);
            set => SetVector("AgentPreview.Position", value);
        }

        // -- Cached results -----------------------------------------------------
        public static NavigationProbeResult LocalResult { get; private set; }
        public static Vector3[] ServerPath { get; private set; } = Array.Empty<Vector3>();
        public static string ServerMessage { get; private set; } = string.Empty;
        public static NavigationNavmeshAnalysis Analysis { get; private set; }
        public static NavigationAgentProfile PreviewAgent { get; set; }

        /// <summary>Whether a handle was dragged: the window highlights that the result is stale.</summary>
        public static bool ProbePointsMovedSinceQuery
        {
            get => SessionState.GetBool(PrefsPrefix + "ProbeDirty", false);
            private set => SessionState.SetBool(PrefsPrefix + "ProbeDirty", value);
        }

        static NavigationSceneTools()
        {
            SceneView.duringSceneGui += OnSceneGui;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseGraphics;
            EditorApplication.quitting += ReleaseGraphics;
        }

        public static void SetLocalResult(NavigationProbeResult result)
        {
            LocalResult = result;
            ProbePointsMovedSinceQuery = false;
            SceneView.RepaintAll();
        }

        public static void SetServerPath(Vector3[] points, string message)
        {
            ServerPath = points ?? Array.Empty<Vector3>();
            ServerMessage = message ?? string.Empty;
            SceneView.RepaintAll();
        }

        public static void ClearProbeResults()
        {
            LocalResult = null;
            ServerPath = Array.Empty<Vector3>();
            ServerMessage = string.Empty;
            ProbePointsMovedSinceQuery = false;
            SceneView.RepaintAll();
        }

        public static NavigationNavmeshAnalysis EnsureAnalysis()
        {
            return Analysis ??= new NavigationNavmeshAnalysis();
        }

        public static void ClearAnalysis()
        {
            Analysis?.Dispose();
            Analysis = null;
            SceneView.RepaintAll();
        }

        private static void ReleaseGraphics()
        {
            Analysis?.Dispose();
            Analysis = null;
            if (analysisMaterial != null)
            {
                Object.DestroyImmediate(analysisMaterial);
                analysisMaterial = null;
            }
        }

        // -- Drawing -------------------------------------------------------------
        private static void OnSceneGui(SceneView sceneView)
        {
            if (!NavigationHighlightSettings.Enabled)
            {
                return;
            }

            DrawAnalysis();
            DrawAgentPreview();
            DrawProbe();
        }

        private static void DrawAnalysis()
        {
            if (!ShowAnalysis || Analysis?.Overlay == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            EnsureAnalysisMaterial();
            GL.PushMatrix();
            GL.MultMatrix(Handles.matrix);
            analysisMaterial.SetPass(0);
            Graphics.DrawMeshNow(Analysis.Overlay, Matrix4x4.identity);
            GL.PopMatrix();
        }

        private static void EnsureAnalysisMaterial()
        {
            if (analysisMaterial != null)
            {
                return;
            }

            analysisMaterial = new Material(Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            analysisMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            analysisMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            analysisMaterial.SetInt("_Cull", (int)CullMode.Off);
            analysisMaterial.SetInt("_ZWrite", 0);
            analysisMaterial.SetInt("_ZTest", (int)CompareFunction.LessEqual);
        }

        private static void DrawAgentPreview()
        {
            if (!AgentPreviewEnabled)
            {
                return;
            }

            NavigationAgentProfile agent = ResolvePreviewAgent();
            if (agent == null)
            {
                return;
            }

            Vector3 position = AgentPreviewPosition;
            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                AgentPreviewPosition = moved;
                position = moved;
            }

            float radius = agent.Radius;
            float height = agent.Height;
            Handles.color = AgentColor;
            Handles.DrawWireDisc(position, Vector3.up, radius);
            Handles.DrawWireDisc(position + Vector3.up * height, Vector3.up, radius);
            Handles.DrawWireDisc(position + Vector3.up * agent.MaximumClimb, Vector3.up, radius * 0.98f);

            for (int i = 0; i < 4; i++)
            {
                float angle = i * Mathf.PI * 0.5f;
                var offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Handles.DrawLine(position + offset, position + offset + Vector3.up * height);
            }

            Handles.Label(
                position + Vector3.up * (height + 0.25f),
                $"Agent {agent.Height:0.##} x R {agent.Radius:0.##} m\n" +
                $"Passage >= {agent.Radius * 2f:0.##} m, step <= {agent.MaximumClimb:0.##} m");
        }

        /// <summary>
        /// A static profile reference is lost after a recompile.
        /// It is restored once and cached, so the scene is not searched every frame.
        /// </summary>
        private static NavigationAgentProfile ResolvePreviewAgent()
        {
            if (PreviewAgent != null)
            {
                return PreviewAgent;
            }

            NavigationLevel[] levels = Object.FindObjectsByType<NavigationLevel>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] != null && levels[i].DefaultAgentProfile != null)
                {
                    PreviewAgent = levels[i].DefaultAgentProfile;
                    return PreviewAgent;
                }
            }

            // No profile: turn the preview off so it is not searched for every frame.
            AgentPreviewEnabled = false;
            return null;
        }

        private static void DrawProbe()
        {
            if (!ProbeEnabled)
            {
                return;
            }

            Vector3 start = ProbeStart;
            Vector3 destination = ProbeDestination;

            EditorGUI.BeginChangeCheck();
            Vector3 nextStart = Handles.PositionHandle(start, Quaternion.identity);
            Vector3 nextDestination = Handles.PositionHandle(destination, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                // Dragging computes NOTHING: it only moves the point and marks
                // the previous result as stale.
                ProbeStart = nextStart;
                ProbeDestination = nextDestination;
                ProbePointsMovedSinceQuery = true;
                start = nextStart;
                destination = nextDestination;
            }

            Handles.color = StartColor;
            Handles.SphereHandleCap(0, start, Quaternion.identity, 0.35f, EventType.Repaint);
            Handles.Label(start + Vector3.up * 0.6f, "Start");

            Handles.color = DestinationColor;
            Handles.SphereHandleCap(0, destination, Quaternion.identity, 0.35f, EventType.Repaint);
            Handles.Label(destination + Vector3.up * 0.6f, "Destination");

            DrawCachedPath();
        }

        private static void DrawCachedPath()
        {
            NavigationProbeResult result = LocalResult;
            if (result != null && result.Success && result.Points.Length > 1)
            {
                Handles.color = ProbePointsMovedSinceQuery
                    ? new Color(LocalPathColor.r, LocalPathColor.g, LocalPathColor.b, 0.35f)
                    : LocalPathColor;
                Handles.DrawAAPolyLine(5f, result.Points);
                for (int i = 0; i < result.Points.Length; i++)
                {
                    Handles.DrawSolidDisc(result.Points[i] + Vector3.up * 0.02f, Vector3.up, 0.09f);
                }

                Vector3 middle = result.Points[result.Points.Length / 2];
                Handles.Label(
                    middle + Vector3.up * 0.5f,
                    $"Local: {result.Length:0.##} m, {result.Points.Length} points" +
                    (ProbePointsMovedSinceQuery ? "\n(points moved - press Find Path)" : string.Empty));
            }

            if (ServerPath.Length > 1)
            {
                Handles.color = ServerPathColor;
                Handles.DrawAAPolyLine(3f, ServerPath);
                Handles.Label(ServerPath[ServerPath.Length / 2] + Vector3.up * 0.9f, "Server");
            }

            if (result != null && !result.Success)
            {
                if (result.HasProjectedStart)
                {
                    DrawProjectionHint(ProbeStart, result.ProjectedStart, StartColor);
                }

                if (result.HasProjectedDestination)
                {
                    DrawProjectionHint(ProbeDestination, result.ProjectedDestination, DestinationColor);
                }
            }
        }

        private static void DrawProjectionHint(Vector3 from, Vector3 to, Color color)
        {
            Handles.color = color;
            Handles.DrawDottedLine(from, to, 3f);
            Handles.DrawWireDisc(to, Vector3.up, 0.3f);
        }

        // -- Position storage -----------------------------------------------------
        private static Vector3 GetVector(string key, Vector3 fallback)
        {
            string value = SessionState.GetString(PrefsPrefix + key, string.Empty);
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            string[] parts = value.Split('|');
            if (parts.Length != 3
                || !float.TryParse(parts[0], out float x)
                || !float.TryParse(parts[1], out float y)
                || !float.TryParse(parts[2], out float z))
            {
                return fallback;
            }

            return new Vector3(x, y, z);
        }

        private static void SetVector(string key, Vector3 value)
        {
            SessionState.SetString(PrefsPrefix + key, $"{value.x}|{value.y}|{value.z}");
        }
    }
}





