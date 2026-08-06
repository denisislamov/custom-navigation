using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Agent parameter diagram: Height, Radius, Maximum Climb and Maximum Slope
    /// with callouts filled in from the current profile values.
    ///
    /// Drawn procedurally in the GUI: it is pure arithmetic over the profile fields,
    /// with no scene or navmesh access, so it creates no background work.
    /// </summary>
    internal static class NavigationAgentDiagram
    {
        private const float DiagramHeight = 210f;
        private const string FoldoutKey = "CustomNavigation.AgentDiagram";

        private static readonly Color Ground = new Color(0.32f, 0.34f, 0.38f, 1f);
        private static readonly Color Step = new Color(0.42f, 0.45f, 0.5f, 1f);
        private static readonly Color AgentFill = new Color(0.2f, 0.8f, 1f, 0.35f);
        private static readonly Color AgentLine = new Color(0.35f, 0.9f, 1f, 1f);
        private static readonly Color Dimension = new Color(1f, 0.82f, 0.25f, 1f);
        private static readonly Color SlopeLine = new Color(0.55f, 1f, 0.6f, 1f);
        private static readonly Color Background = new Color(0.16f, 0.17f, 0.19f, 1f);

        /// <summary>Collapsible "How it works" block with the diagram and the derived values.</summary>
        public static void DrawFoldout(NavigationAgentProfile profile, NavigationLevel level = null)
        {
            if (profile == null)
            {
                return;
            }

            string prefsKey = FoldoutKey + ".Expanded";
            bool expanded = EditorPrefs.GetBool(prefsKey, true);
            bool next = EditorGUILayout.Foldout(expanded, "How it works (agent diagram)", true, EditorStyles.foldoutHeader);
            if (next != expanded)
            {
                EditorPrefs.SetBool(prefsKey, next);
            }

            if (!next)
            {
                return;
            }

            Draw(profile);
            DrawDerivedValues(profile, level);
        }

        public static void Draw(NavigationAgentProfile profile)
        {
            Rect rect = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(DiagramHeight));
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            EditorGUI.DrawRect(rect, Background);
            DrawAgentSection(new Rect(rect.x, rect.y, rect.width * 0.36f, rect.height), profile);
            DrawClimbSection(new Rect(rect.x + rect.width * 0.36f, rect.y, rect.width * 0.3f, rect.height), profile);
            DrawSlopeSection(new Rect(rect.x + rect.width * 0.66f, rect.y, rect.width * 0.34f, rect.height), profile);
        }

        // -- Section 1: height and radius --------------------------------------
        private static void DrawAgentSection(Rect area, NavigationAgentProfile profile)
        {
            float groundY = area.yMax - 42f;
            float agentPixelHeight = area.height - 90f;
            float scale = agentPixelHeight / Mathf.Max(0.01f, profile.Height);
            float agentPixelRadius = Mathf.Clamp(profile.Radius * scale, 6f, area.width * 0.22f);
            float centerX = area.center.x;

            DrawLine(new Vector2(area.x + 8f, groundY), new Vector2(area.xMax - 8f, groundY), Ground, 3f);

            // Wall on the right: shows that the navmesh keeps a Radius gap from it.
            var wall = new Rect(area.xMax - 22f, groundY - agentPixelHeight - 10f, 12f, agentPixelHeight + 10f);
            EditorGUI.DrawRect(wall, Step);

            // Agent capsule.
            var body = new Rect(
                centerX - agentPixelRadius,
                groundY - agentPixelHeight,
                agentPixelRadius * 2f,
                agentPixelHeight);
            EditorGUI.DrawRect(body, AgentFill);
            DrawRectOutline(body, AgentLine);

            // Height.
            DrawVerticalDimension(
                new Vector2(area.x + 18f, groundY - agentPixelHeight),
                new Vector2(area.x + 18f, groundY),
                $"Height {profile.Height:0.##} m");

            // Radius.
            DrawHorizontalDimension(
                new Vector2(centerX, groundY + 14f),
                new Vector2(centerX + agentPixelRadius, groundY + 14f),
                $"Radius {profile.Radius:0.##} m");

            // Gap from the wall.
            DrawLine(
                new Vector2(centerX + agentPixelRadius, groundY - agentPixelHeight * 0.5f),
                new Vector2(wall.x, groundY - agentPixelHeight * 0.5f),
                Dimension,
                1f);

            DrawCaption(area, "Height and radius");
        }

        // -- Section 2: maximum step -------------------------------------------
        private static void DrawClimbSection(Rect area, NavigationAgentProfile profile)
        {
            float groundY = area.yMax - 42f;
            // The step is drawn relative to the height so the proportion stays honest.
            float scale = (area.height - 90f) / Mathf.Max(0.01f, profile.Height);
            float stepPixelHeight = Mathf.Clamp(profile.MaximumClimb * scale, 4f, area.height * 0.45f);

            float midX = area.center.x;
            DrawLine(new Vector2(area.x + 8f, groundY), new Vector2(midX, groundY), Ground, 3f);

            var step = new Rect(midX, groundY - stepPixelHeight, area.xMax - 12f - midX, stepPixelHeight + 3f);
            EditorGUI.DrawRect(step, Step);

            DrawVerticalDimension(
                new Vector2(midX - 16f, groundY - stepPixelHeight),
                new Vector2(midX - 16f, groundY),
                $"Climb {profile.MaximumClimb:0.##} m");

            var hintRect = new Rect(area.x + 6f, area.yMax - 34f, area.width - 12f, 30f);
            GUI.Label(
                hintRect,
                "A taller step needs a NavigationLink",
                MiniStyle());

            DrawCaption(area, "Maximum step");
        }

        // -- Section 3: maximum slope ------------------------------------------
        private static void DrawSlopeSection(Rect area, NavigationAgentProfile profile)
        {
            float groundY = area.yMax - 42f;
            float baseLength = area.width * 0.6f;
            var origin = new Vector2(area.x + 20f, groundY);
            var horizontal = new Vector2(origin.x + baseLength, groundY);

            float radians = profile.MaximumSlope * Mathf.Deg2Rad;
            var slopeEnd = new Vector2(
                origin.x + baseLength * Mathf.Cos(radians),
                groundY - baseLength * Mathf.Sin(radians));

            DrawLine(origin, horizontal, Ground, 3f);
            DrawLine(origin, slopeEnd, SlopeLine, 3f);

            Handles.BeginGUI();
            Handles.color = Dimension;
            Handles.DrawWireArc(
                new Vector3(origin.x, origin.y, 0f),
                Vector3.forward,
                Vector3.right,
                -profile.MaximumSlope,
                42f);
            Handles.EndGUI();

            var labelRect = new Rect(origin.x + 46f, groundY - 34f, 120f, 18f);
            GUI.Label(labelRect, $"{profile.MaximumSlope:0.#}°", DimensionStyle());

            var hintRect = new Rect(area.x + 6f, area.yMax - 34f, area.width - 12f, 30f);
            GUI.Label(hintRect, "Steeper surfaces are not walkable", MiniStyle());

            DrawCaption(area, "Maximum slope");
        }

        // -- Derived values ----------------------------------------------------
        private static void DrawDerivedValues(NavigationAgentProfile profile, NavigationLevel level)
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Derived values", EditorStyles.miniBoldLabel);
                Row("Minimum passage width", $"{profile.Radius * 2f:0.##} m");
                Row("Doorway with clearance", $"{profile.Radius * 2f + 0.2f:0.##} m");
                Row("Maximum step without a link", $"{profile.MaximumClimb:0.##} m");
                Row("Minimum vertical clearance", $"{profile.Height:0.##} m");
            }

            NavigationBuildSettings settings = level != null ? level.BuildSettings : null;
            if (settings == null)
            {
                return;
            }

            if (profile.Radius < settings.CellSize * 2f)
            {
                EditorGUILayout.HelpBox(
                    $"The agent radius ({profile.Radius:0.###} m) is smaller than two Recast cells " +
                    $"({settings.CellSize:0.###} m). The navmesh will be imprecise near walls - " +
                    "reduce the cell size in Build or increase the radius.",
                    MessageType.Warning);
            }

            if (profile.MaximumClimb < settings.CellHeight * 2f)
            {
                EditorGUILayout.HelpBox(
                    $"Maximum Climb ({profile.MaximumClimb:0.###} m) is smaller than two cell heights " +
                    $"({settings.CellHeight:0.###} m). Recast will not reliably tell a step " +
                    "from an obstacle.",
                    MessageType.Warning);
            }
        }

        private static void Row(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
                EditorGUILayout.LabelField(value, EditorStyles.miniBoldLabel, GUILayout.Width(90f));
            }
        }

        // -- Drawing primitives ------------------------------------------------
        private static void DrawCaption(Rect area, string text)
        {
            var rect = new Rect(area.x + 6f, area.y + 4f, area.width - 12f, 16f);
            GUI.Label(rect, text, CaptionStyle());
        }

        private static void DrawVerticalDimension(Vector2 top, Vector2 bottom, string label)
        {
            DrawLine(top, bottom, Dimension, 1f);
            DrawLine(top + Vector2.left * 4f, top + Vector2.right * 4f, Dimension, 1f);
            DrawLine(bottom + Vector2.left * 4f, bottom + Vector2.right * 4f, Dimension, 1f);
            var rect = new Rect(top.x - 4f, (top.y + bottom.y) * 0.5f - 9f, 130f, 18f);
            GUI.Label(rect, label, DimensionStyle());
        }

        private static void DrawHorizontalDimension(Vector2 left, Vector2 right, string label)
        {
            DrawLine(left, right, Dimension, 1f);
            DrawLine(left + Vector2.up * 4f, left + Vector2.down * 4f, Dimension, 1f);
            DrawLine(right + Vector2.up * 4f, right + Vector2.down * 4f, Dimension, 1f);
            var rect = new Rect(left.x - 10f, left.y + 4f, 140f, 18f);
            GUI.Label(rect, label, DimensionStyle());
        }

        private static void DrawLine(Vector2 from, Vector2 to, Color color, float width)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawAAPolyLine(width, new Vector3(from.x, from.y, 0f), new Vector3(to.x, to.y, 0f));
            Handles.EndGUI();
        }

        private static void DrawRectOutline(Rect rect, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;
            Handles.DrawAAPolyLine(
                1.5f,
                new Vector3(rect.xMin, rect.yMin, 0f),
                new Vector3(rect.xMax, rect.yMin, 0f),
                new Vector3(rect.xMax, rect.yMax, 0f),
                new Vector3(rect.xMin, rect.yMax, 0f),
                new Vector3(rect.xMin, rect.yMin, 0f));
            Handles.EndGUI();
        }

        private static GUIStyle captionStyle;
        private static GUIStyle dimensionStyle;
        private static GUIStyle miniStyle;

        private static GUIStyle CaptionStyle()
        {
            return captionStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = new Color(0.8f, 0.85f, 0.9f, 1f) }
            };
        }

        private static GUIStyle DimensionStyle()
        {
            return dimensionStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Dimension }
            };
        }

        private static GUIStyle MiniStyle()
        {
            return miniStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal = { textColor = new Color(0.65f, 0.68f, 0.72f, 1f) }
            };
        }
    }
}

