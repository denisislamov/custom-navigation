using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Shared drawing helpers for the navigation inspectors. They keep the advanced
    /// parameters collapsed so that the common workflow stays simple.
    /// </summary>
    internal static class NavigationInspectorGUI
    {
        private const string AdvancedPrefix = "CustomNavigation.Inspector.Advanced.";

        public static bool AdvancedFoldout(string key, string label = "Advanced")
        {
            string prefsKey = AdvancedPrefix + key;
            bool expanded = EditorPrefs.GetBool(prefsKey, false);
            bool next = EditorGUILayout.Foldout(expanded, label, true, EditorStyles.foldoutHeader);
            if (next != expanded)
            {
                EditorPrefs.SetBool(prefsKey, next);
            }

            return next;
        }

        public static void Header(string label)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        public static void DrawProperties(SerializedObject target, params string[] propertyNames)
        {
            for (int i = 0; i < propertyNames.Length; i++)
            {
                SerializedProperty property = target.FindProperty(propertyNames[i]);
                if (property != null)
                {
                    EditorGUILayout.PropertyField(property, true);
                }
            }
        }

        public static void DrawChildProperties(SerializedProperty parent, params string[] propertyNames)
        {
            for (int i = 0; i < propertyNames.Length; i++)
            {
                SerializedProperty property = parent.FindPropertyRelative(propertyNames[i]);
                if (property != null)
                {
                    EditorGUILayout.PropertyField(property, true);
                }
            }
        }

        /// <summary>
        /// Draws NavigationBuildSettings: the quality preset is always visible,
        /// raw Recast parameters live in Advanced and only for Custom.
        /// </summary>
        public static void DrawBuildSettings(SerializedProperty buildSettings, string foldoutKey)
        {
            DrawBuildSettings(buildSettings, foldoutKey, null);
        }

        /// <param name="agent">
        /// Needed only to show the recommended cell values.
        /// May be null.
        /// </param>
        public static void DrawBuildSettings(
            SerializedProperty buildSettings,
            string foldoutKey,
            NavigationAgentProfile agent)
        {
            if (buildSettings == null)
            {
                return;
            }

            SerializedProperty quality = buildSettings.FindPropertyRelative("quality");
            SerializedProperty autoCellSize = buildSettings.FindPropertyRelative("autoCellSize");
            EditorGUILayout.PropertyField(quality);

            bool isCustom = quality.enumValueIndex == (int)NavigationBakeQuality.Custom;
            if (!isCustom)
            {
                EditorGUILayout.PropertyField(autoCellSize);
                EditorGUILayout.HelpBox(
                    "The preset drives every Recast parameter automatically. " +
                    "Switch to Custom to set them by hand.",
                    MessageType.None);
            }

            if (!AdvancedFoldout(foldoutKey, "Advanced (raw Recast parameters)"))
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(!isCustom))
            {
                if (isCustom)
                {
                    EditorGUILayout.PropertyField(autoCellSize);
                }

                // Cell sizes are derived from the agent, so recommendations are shown.
                if (agent != null && isCustom)
                {
                    NavigationFieldLabels.DrawWithRecommendation(
                        buildSettings.FindPropertyRelative("cellSize"),
                        agent.Radius / 3f,
                        "agent radius / 3");
                    NavigationFieldLabels.DrawWithRecommendation(
                        buildSettings.FindPropertyRelative("cellHeight"),
                        agent.MaximumClimb * 0.5f,
                        "half of the maximum step");
                }
                else
                {
                    NavigationFieldLabels.DrawChildProperties(buildSettings, "cellSize", "cellHeight");
                }

                NavigationFieldLabels.DrawChildProperties(
                    buildSettings,
                    "minimumRegionArea",
                    "mergedRegionArea",
                    "maximumEdgeLength",
                    "maximumEdgeError",
                    "detailSampleDistance",
                    "detailSampleMaximumError",
                    "maximumVerticesPerPolygon",
                    "tileSizeInCells");
            }
        }
    }
}
