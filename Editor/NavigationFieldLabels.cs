using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Human readable labels and "what happens if" hints for technical fields.
    ///
    /// Serialized field names are NOT changed - only what the user sees is
    /// overridden, so existing assets stay valid.
    /// </summary>
    internal static class NavigationFieldLabels
    {
        private sealed class FieldInfo
        {
            public string Label;
            public string Tooltip;
            public string Units;
        }

        private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>
        {
            // -- Recast: affects the navmesh itself --------------------------
            ["cellSize"] = new FieldInfo
            {
                Label = "Cell size",
                Units = "m",
                Tooltip = "Voxelization step along X/Z. Smaller is more precise near walls but " +
                          "makes the bake slower and the artifact bigger. Recommended: Radius / 3."
            },
            ["cellHeight"] = new FieldInfo
            {
                Label = "Cell height",
                Units = "m",
                Tooltip = "Vertical voxelization step. Smaller means more precise steps and ramps. " +
                          "Recommended: Maximum Climb / 2."
            },
            ["maximumEdgeError"] = new FieldInfo
            {
                Label = "Edge simplification",
                Tooltip = "How aggressively contours are straightened. Higher means fewer polygons " +
                          "but the navmesh edges drift away from the real geometry."
            },
            ["maximumEdgeLength"] = new FieldInfo
            {
                Label = "Max edge length",
                Units = "cells",
                Tooltip = "Long edges are split into parts. Affects mesh density along walls."
            },
            ["minimumRegionArea"] = new FieldInfo
            {
                Label = "Min region area",
                Tooltip = "Navmesh islands smaller than this area are removed. " +
                          "Increase it when useless patches appear."
            },
            ["mergedRegionArea"] = new FieldInfo
            {
                Label = "Merge region area",
                Tooltip = "Small regions are merged into neighbours to reduce the region count."
            },
            ["detailSampleDistance"] = new FieldInfo
            {
                Label = "Detail mesh step",
                Tooltip = "Height detail density. 0 disables the detail mesh - faster, but the " +
                          "height on ramps becomes coarser."
            },
            ["detailSampleMaximumError"] = new FieldInfo
            {
                Label = "Detail mesh error",
                Units = "m",
                Tooltip = "Allowed deviation of the navmesh height from the source geometry."
            },
            ["maximumVerticesPerPolygon"] = new FieldInfo
            {
                Label = "Vertices per polygon",
                Tooltip = "In practice always 6. Lower means more polygons for the same shape."
            },
            ["tileSizeInCells"] = new FieldInfo
            {
                Label = "Tile size",
                Units = "cells",
                Tooltip = "NOT USED in the current version: the navmesh is built as a single tile. " +
                          "Reserved for the tiled pipeline."
            },

            // -- Runtime: affects the client scheduler only ------------------
            ["frameBudgetMilliseconds"] = new FieldInfo
            {
                Label = "Frame budget",
                Units = "ms",
                Tooltip = "How many milliseconds per frame may be spent on pathfinding. " +
                          "Exceeding it simply postpones requests to the next frame."
            },
            ["maximumIterationsPerFrame"] = new FieldInfo
            {
                Label = "Search steps per frame",
                Tooltip = "Total A* iteration limit across all requests. " +
                          "Higher finds paths sooner but raises the CPU load."
            },
            ["maximumIterationsPerQueryStep"] = new FieldInfo
            {
                Label = "Steps per single pass",
                Tooltip = "Work quantum of one request. Lower shares time more fairly between " +
                          "agents but adds overhead."
            },
            ["maximumNewQueriesPerFrame"] = new FieldInfo
            {
                Label = "New requests per frame",
                Tooltip = "How many agents may start a search in one frame. " +
                          "Limits the spike when every bot changes its destination at once."
            },
            ["maximumConcurrentSlicedQueries"] = new FieldInfo
            {
                Label = "Concurrent requests",
                Tooltip = "Size of the working buffer pool. This is the parameter that actually " +
                          "defines the scheduler memory footprint."
            },
            ["maximumQueuedQueries"] = new FieldInfo
            {
                Label = "Backlog limit",
                Tooltip = "Maximum waiting requests, excluding active searches. A full backlog " +
                          "rejects the incoming request unless it has priority to evict the worst queued one."
            },
            ["maximumPathPolygons"] = new FieldInfo
            {
                Label = "Corridor polygons",
                Tooltip = "Polygon-corridor buffer size. Reaching the cap can produce a partial path."
            },
            ["maximumStraightPathPoints"] = new FieldInfo
            {
                Label = "Route points",
                Tooltip = "Buffer size for the resulting path points."
            },
            ["queryDeadlineSeconds"] = new FieldInfo
            {
                Label = "Queue lifetime",
                Units = "s",
                Tooltip = "Maximum wait before admission. An expired request reports a queue " +
                          "expiration; this deadline never aborts an active sliced search."
            },
            ["budgetWarningMultiplier"] = new FieldInfo
            {
                Label = "Warning threshold",
                Tooltip = "Multiplies Frame Budget for the rate-limited console warning. " +
                          "It does not change scheduler execution limits."
            }
        };

        public static GUIContent Get(SerializedProperty property)
        {
            if (property == null)
            {
                return GUIContent.none;
            }

            if (!Fields.TryGetValue(property.name, out FieldInfo info))
            {
                return new GUIContent(property.displayName, property.tooltip);
            }

            string label = string.IsNullOrEmpty(info.Units)
                ? info.Label
                : $"{info.Label} ({info.Units})";
            return new GUIContent(label, info.Tooltip);
        }

        public static void DrawProperty(SerializedProperty property)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, Get(property), true);
            }
        }

        public static void DrawProperties(SerializedObject target, params string[] propertyNames)
        {
            for (int i = 0; i < propertyNames.Length; i++)
            {
                DrawProperty(target.FindProperty(propertyNames[i]));
            }
        }

        public static void DrawChildProperties(SerializedProperty parent, params string[] propertyNames)
        {
            for (int i = 0; i < propertyNames.Length; i++)
            {
                DrawProperty(parent.FindPropertyRelative(propertyNames[i]));
            }
        }

        /// <summary>
        /// A field with a recommended value and an "apply" button when the current
        /// value differs a lot. Computes nothing heavy - this is plain arithmetic.
        /// </summary>
        public static void DrawWithRecommendation(
            SerializedProperty property,
            float recommended,
            string reason)
        {
            if (property == null)
            {
                return;
            }

            DrawProperty(property);
            float current = property.floatValue;
            if (recommended <= 0f || Mathf.Abs(current - recommended) <= recommended * 0.25f)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                EditorGUILayout.LabelField(
                    $"Recommended {recommended:0.###} - {reason}",
                    EditorStyles.miniLabel);
                if (GUILayout.Button("Apply", EditorStyles.miniButton, GUILayout.Width(80f)))
                {
                    property.floatValue = recommended;
                }
            }
        }
    }
}
