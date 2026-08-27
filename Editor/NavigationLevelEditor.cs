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
        private NavigationValidationReport validationReport = NavigationValidationReport.NotEvaluated;
        private bool showAdvanced;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var level = (NavigationLevel)target;

            NavigationInspectorGUI.Header("Level");
            NavigationInspectorGUI.DrawProperties(serializedObject, "levelId");

            NavigationInspectorGUI.Header("Geometry Root");
            NavigationInspectorGUI.DrawProperties(serializedObject, "geometryRoot");

            NavigationInspectorGUI.Header("Settings");
            DrawSetupSection(level);

            serializedObject.ApplyModifiedProperties();

            NavigationInspectorGUI.Header("Bake Status");
            DrawBakeStatus(level);
            DrawPrimaryActions(level);

            EditorGUILayout.Space(6f);
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);
            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                serializedObject.Update();
                NavigationInspectorGUI.DrawProperties(serializedObject, "description");
                NavigationInspectorGUI.DrawBuildSettings(
                    serializedObject.FindProperty("buildSettings"),
                    "NavigationLevel.BuildSettings");
                serializedObject.ApplyModifiedProperties();
                DrawAdvancedActions(level);
                EditorGUI.indentLevel--;
            }
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

        private void DrawBakeStatus(NavigationLevel level)
        {
            NavigationArtifactAsset artifact = NavigationArtifactBuilder.LoadClientArtifact(level.LevelId);
            if (artifact == null)
            {
                EditorGUILayout.HelpBox("Not baked", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"Ready · {artifact.PolygonCount} polygons · {artifact.SourceMeshCount} sources · " +
                    $"{(artifact.NavigationData != null ? artifact.NavigationData.bytes.Length / 1024f : 0f):0.#} KB",
                    MessageType.Info);
            }

            if (validationReport.Evaluated)
            {
                MessageType type = validationReport.HasErrors ? MessageType.Error
                    : validationReport.WarningCount > 0 ? MessageType.Warning
                    : MessageType.Info;
                string message = validationReport.HasErrors
                    ? $"Validation: {validationReport.ErrorCount} error(s). " +
                      validationReport.DescribeFirstError()
                    : validationReport.WarningCount > 0
                        ? $"Validation: {validationReport.WarningCount} warning(s)."
                        : "Validation: ready.";
                EditorGUILayout.HelpBox(message, type);
            }

            if (!string.IsNullOrEmpty(lastBakeMessage))
            {
                EditorGUILayout.HelpBox(lastBakeMessage, lastBakeMessageType);
            }
        }

        private void DrawPrimaryActions(NavigationLevel level)
        {
            bool ready = level.IsReadyToBake(out string reason);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate", GUILayout.Height(28f)))
                {
                    validationReport = NavigationValidationReport.Create(level);
                }

                using (new EditorGUI.DisabledScope(!ready))
                {
                    if (GUILayout.Button("Bake", GUILayout.Height(28f)))
                    {
                        BuildForClient(level);
                    }
                }

                if (GUILayout.Button("Open", GUILayout.Height(28f)))
                {
                    NavigationEditorWindow.Open();
                }
            }

            if (!ready)
            {
                EditorGUILayout.HelpBox(reason, MessageType.Warning);
            }
        }

        private void DrawAdvancedActions(NavigationLevel level)
        {
            NavigationArtifactAsset builtArtifact = NavigationArtifactBuilder.LoadClientArtifact(level.LevelId);
            using (new EditorGUI.DisabledScope(builtArtifact == null))
            {
                if (GUILayout.Button("Export for Server", EditorStyles.miniButton))
                {
                    ExportForServer(level);
                }
            }
        }

        private void BuildForClient(NavigationLevel level)
        {
            try
            {
                NavigationArtifactBuildResult result = NavigationArtifactBuilder.BuildForClient(level);
                validationReport = NavigationValidationReport.Create(level);
                lastBakeMessage =
                    $"Bake finished.\nPolygons: {result.PolygonCount}\n" +
                    $"Source meshes: {result.SourceMeshCount}\nSize: {result.ByteSize} bytes\n" +
                    result.ClientDataPath;
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
            NavigationProjectSettings projectSettings = NavigationProjectSettings.instance;

            NavigationAgentProfile agent = level.DefaultAgentProfile
                ?? projectSettings.DefaultAgentProfile
                ?? CreateAsset<NavigationAgentProfile>($"{GeneratedSettingsFolder}/{key}_Agent.asset");

            NavigationAreaCatalog areas = level.AreaCatalog ?? projectSettings.DefaultAreaCatalog;
            if (areas == null)
            {
                areas = CreateAsset<NavigationAreaCatalog>($"{GeneratedSettingsFolder}/{key}_Areas.asset");
                areas.ResetToDefaults();
                EditorUtility.SetDirty(areas);
            }

            NavigationPerformanceProfile performance =
                level.PerformanceProfile ?? projectSettings.DefaultPerformanceProfile;
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
            PrefabUtility.RecordPrefabInstancePropertyModifications(level);
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
