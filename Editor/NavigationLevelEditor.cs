using System;
using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    [CustomEditor(typeof(NavigationLevel))]
    public sealed class NavigationLevelEditor : UnityEditor.Editor
    {
        private const string GeneratedSettingsFolder = "Assets/DataSakura/CustomNavigation/Generated/Settings";

        private string lastBakeMessage;
        private MessageType lastBakeMessageType = MessageType.None;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var level = (NavigationLevel)target;

            NavigationInspectorGUI.Header("Level");
            NavigationInspectorGUI.DrawProperties(serializedObject, "levelId", "description", "geometryRoot");

            NavigationInspectorGUI.Header("Settings");
            DrawSetupSection(level);

            NavigationInspectorGUI.Header("Bake quality");
            NavigationInspectorGUI.DrawBuildSettings(
                serializedObject.FindProperty("buildSettings"),
                "NavigationLevel.BuildSettings");

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10f);
            DrawBakeSection(level);
        }

        private void DrawSetupSection(NavigationLevel level)
        {
            NavigationInspectorGUI.DrawProperties(
                serializedObject,
                "defaultAgentProfile",
                "areaCatalog",
                "performanceProfile");

            bool missingAny = level.DefaultAgentProfile == null
                              || level.AreaCatalog == null
                              || level.PerformanceProfile == null;
            if (!missingAny)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "Some configuration assets are missing. Press the button below to create them " +
                "with default values and wire them up automatically.",
                MessageType.Warning);

            if (GUILayout.Button("Create the missing settings", GUILayout.Height(24f)))
            {
                CreateMissingAssets(level);
            }
        }

        private void DrawBakeSection(NavigationLevel level)
        {
            bool ready = level.IsReadyToBake(out string reason);
            if (!ready)
            {
                EditorGUILayout.HelpBox(reason, MessageType.Warning);
            }

            NavigationArtifactAsset builtArtifact = NavigationArtifactBuilder.LoadClientArtifact(level.LevelId);

            using (new EditorGUI.DisabledScope(!ready))
            {
                if (GUILayout.Button("Bake for Client", GUILayout.Height(32f)))
                {
                    BuildForClient(level);
                }
            }

            using (new EditorGUI.DisabledScope(builtArtifact == null))
            {
                if (GUILayout.Button("Export for Server", GUILayout.Height(26f)))
                {
                    ExportForServer(level);
                }
            }

            if (builtArtifact == null)
            {
                EditorGUILayout.HelpBox(
                    "The client artifact is not built yet - run Bake for Client first.",
                    MessageType.None);
            }

            if (!string.IsNullOrEmpty(lastBakeMessage))
            {
                EditorGUILayout.HelpBox(lastBakeMessage, lastBakeMessageType);
            }

            if (GUILayout.Button("Open Navigation Editor", EditorStyles.miniButton))
            {
                NavigationEditorWindow.Open();
            }
        }

        private void BuildForClient(NavigationLevel level)
        {
            try
            {
                NavigationArtifactBuildResult result = NavigationArtifactBuilder.BuildForClient(level);
                lastBakeMessage =
                    $"Bake finished.\nPolygons: {result.PolygonCount}\nSource meshes: {result.SourceMeshCount}\nHash: {result.Hash}";
                lastBakeMessageType = MessageType.Info;
            }
            catch (Exception exception)
            {
                lastBakeMessage = "Bake failed: " + exception.Message;
                lastBakeMessageType = MessageType.Error;
                Debug.LogException(exception, level);
            }
        }

        private void ExportForServer(NavigationLevel level)
        {
            try
            {
                NavigationServerExportResult export = NavigationArtifactBuilder.ExportForServer(level);
                lastBakeMessage = $"Uploaded to the server:\n{export.ServerDataPath}";
                lastBakeMessageType = MessageType.Info;
            }
            catch (Exception exception)
            {
                lastBakeMessage = "Export failed: " + exception.Message;
                lastBakeMessageType = MessageType.Error;
                Debug.LogException(exception, level);
            }
        }

        private void CreateMissingAssets(NavigationLevel level)
        {
            EnsureFolder(GeneratedSettingsFolder);
            string key = NavigationIdUtility.Sanitize(level.LevelId, "level");

            NavigationAgentProfile agent = level.DefaultAgentProfile
                ?? CreateAsset<NavigationAgentProfile>($"{GeneratedSettingsFolder}/{key}_Agent.asset");

            NavigationAreaCatalog areas = level.AreaCatalog;
            if (areas == null)
            {
                areas = CreateAsset<NavigationAreaCatalog>($"{GeneratedSettingsFolder}/{key}_Areas.asset");
                areas.ResetToDefaults();
                EditorUtility.SetDirty(areas);
            }

            NavigationPerformanceProfile performance = level.PerformanceProfile;
            if (performance == null)
            {
                performance = CreateAsset<NavigationPerformanceProfile>(
                    $"{GeneratedSettingsFolder}/{key}_Performance.asset");
                performance.ApplyStartingPreset(NavigationDeviceTier.MobileMedium);
                EditorUtility.SetDirty(performance);
            }

            Undo.RecordObject(level, "Assign Navigation Profiles");
            level.ConfigureDefaults(agent, areas, performance);
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static T CreateAsset<T>(string path) where T : ScriptableObject
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
