using CustomNavigation.Runtime;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Hides fields that do not apply to the selected <see cref="NavigationComputeMode"/>,
    /// so the level designer never has to guess which settings actually work.
    /// </summary>
    [CustomEditor(typeof(NavigationBotAgent))]
    public sealed class NavigationBotAgentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty computeMode = serializedObject.FindProperty("computeMode");
            var mode = (NavigationComputeMode)computeMode.enumValueIndex;
            bool usesLocal = mode != NavigationComputeMode.ServerOnly;
            bool usesServer = mode != NavigationComputeMode.LocalOnly;

            NavigationInspectorGUI.Header("Where the path is computed");
            EditorGUILayout.PropertyField(computeMode);
            EditorGUILayout.HelpBox(DescribeMode(mode), MessageType.Info);

            if (usesServer)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("serverUrlOverride"));
                if (GUILayout.Button("Open server settings", EditorStyles.miniButton))
                {
                    NavigationEditorWindow.OpenServerTab();
                }
            }

            NavigationInspectorGUI.Header("Navigation");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("navigation"));
            if (!usesLocal)
            {
                EditorGUILayout.HelpBox(
                    "In Server Only mode the local scheduler is optional. " +
                    "When assigned, it is used only for waypoint snapping and validation.",
                    MessageType.None);
            }

            NavigationInspectorGUI.DrawProperties(serializedObject, "route", "startWaypointIndex");

            NavigationInspectorGUI.Header("Movement");
            NavigationInspectorGUI.DrawProperties(
                serializedObject,
                "moveSpeed",
                "arrivalRadius",
                "groundOffset",
                "snapToNavMeshOnStart",
                "waitAtWaypointSeconds",
                "rotationSpeed",
                "retryDelaySeconds");

            if (usesLocal)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("queryPriority"));
            }

            NavigationInspectorGUI.Header("Visualization");
            NavigationInspectorGUI.DrawProperties(serializedObject, "showPath", "pathLine", "pathLineColor");
            if (usesServer)
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("serverPathLineColor"));
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static string DescribeMode(NavigationComputeMode mode)
        {
            return mode switch
            {
                NavigationComputeMode.ServerOnly =>
                    "Server Only: the path always comes from the authoritative navigation server. " +
                    "Without a network the bot will not move.",
                NavigationComputeMode.ServerPredicted =>
                    "Server Predicted: the local path is applied immediately, then the server confirms " +
                    "or corrects it. This is how the DotRecastHybridPredicted scene works.",
                _ =>
                    "Local Only: the path is computed from the local navmesh artifact only. No network needed."
            };
        }
    }
}
