using CustomNavigation.Authoring;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace CustomNavigation.Editor
{
    /// <summary>Native Scene View controls for all Custom Navigation preview layers.</summary>
    [Overlay(typeof(SceneView), OverlayId, "Custom Navigation", true)]
    internal sealed class NavigationSceneViewOverlay : IMGUIOverlay
    {
        internal const string OverlayId = "DataSakura.CustomNavigation.ScenePreview";

        public override void OnGUI()
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);
            NavigationHighlightSettings.SourcesEnabled = EditorGUILayout.ToggleLeft(
                "Sources",
                NavigationHighlightSettings.SourcesEnabled);
            NavigationHighlightSettings.BakedEnabled = EditorGUILayout.ToggleLeft(
                "Baked",
                NavigationHighlightSettings.BakedEnabled);
            NavigationHighlightSettings.RuntimeEnabled = EditorGUILayout.ToggleLeft(
                "Runtime",
                NavigationHighlightSettings.RuntimeEnabled);

            EditorGUILayout.Space(3f);
            NavigationHighlightSettings.Scope = (NavigationPreviewScope)EditorGUILayout.EnumPopup(
                "Scope",
                NavigationHighlightSettings.Scope);
            NavigationHighlightSettings.Depth = (NavigationPreviewDepth)EditorGUILayout.EnumPopup(
                "Visibility",
                NavigationHighlightSettings.Depth);

            EditorGUILayout.Space(4f);
            string status = NavigationHighlightOverlay.GetStatusText();
            MessageType messageType = status.StartsWith("Baked")
                ? MessageType.Info
                : MessageType.Warning;
            EditorGUILayout.HelpBox(status, messageType);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Settings"))
                {
                    SettingsService.OpenUserPreferences(
                        NavigationProjectSettings.PreferencesProviderPath);
                }

                using (new EditorGUI.DisabledScope(
                           NavigationHighlightOverlay.GetPrimaryLevel() == null))
                {
                    if (GUILayout.Button("Frame Level"))
                    {
                        NavigationHighlightOverlay.FramePrimaryLevel();
                    }
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
        }
    }
}
