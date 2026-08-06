using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    [CustomEditor(typeof(NavigationAgentProfile))]
    public sealed class NavigationAgentProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            NavigationAgentDiagram.DrawFoldout((NavigationAgentProfile)target);
            EditorGUILayout.Space(6f);

            EditorGUILayout.LabelField("Agent dimensions", EditorStyles.boldLabel);
            NavigationInspectorGUI.DrawProperties(
                serializedObject, "height", "radius", "maximumClimb", "maximumSlope");

            EditorGUILayout.HelpBox(
                "These four parameters define the navmesh shape, just like in Unity NavMesh. " +
                "The bake voxel size is derived from the radius automatically.",
                MessageType.Info);

            EditorGUILayout.Space(8f);
            if (NavigationInspectorGUI.AdvancedFoldout("AgentProfile", "Advanced"))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    NavigationInspectorGUI.DrawProperties(
                        serializedObject,
                        "profileId",
                        "allowedMovement",
                        "forbiddenMovement",
                        "areaCosts");
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
