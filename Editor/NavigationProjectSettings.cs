using System;
using System.Collections.Generic;
using System.IO;
using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    [FilePath(SettingsAssetPath, FilePathAttribute.Location.ProjectFolder)]
    internal sealed class NavigationProjectSettings : ScriptableSingleton<NavigationProjectSettings>
    {
        internal const string SettingsAssetPath =
            "ProjectSettings/DataSakuraCustomNavigationSettings.asset";
        internal const string ProjectProviderPath = "Project/DataSakura/Custom Navigation";
        internal const string PreferencesProviderPath =
            "Preferences/DataSakura/Custom Navigation/Scene Preview";
        internal const string DefaultAssetsFolder =
            "Assets/DataSakura/CustomNavigation/Settings";

        [SerializeField] private NavigationAgentProfile defaultAgentProfile;
        [SerializeField] private NavigationAreaCatalog defaultAreaCatalog;
        [SerializeField] private NavigationPerformanceProfile defaultPerformanceProfile;
        [SerializeField] private NavigationBuildSettings defaultBuildSettings =
            new NavigationBuildSettings();

        internal NavigationAgentProfile DefaultAgentProfile => defaultAgentProfile;
        internal NavigationAreaCatalog DefaultAreaCatalog => defaultAreaCatalog;
        internal NavigationPerformanceProfile DefaultPerformanceProfile => defaultPerformanceProfile;
        internal NavigationBuildSettings DefaultBuildSettings =>
            defaultBuildSettings ??= new NavigationBuildSettings();

        internal bool HasAllProfileDefaults => defaultAgentProfile != null
                                               && defaultAreaCatalog != null
                                               && defaultPerformanceProfile != null;

        internal IReadOnlyList<UnityEngine.Object> CreateMissingDefaults(
            string folder = DefaultAssetsFolder,
            bool saveSettings = true)
        {
            EnsureAssetFolder(folder);
            var created = new List<UnityEngine.Object>(3);

            defaultAgentProfile = FindOrCreate(
                defaultAgentProfile,
                folder + "/DefaultAgent.asset",
                null,
                created);
            defaultAreaCatalog = FindOrCreate(
                defaultAreaCatalog,
                folder + "/DefaultAreas.asset",
                catalog => catalog.ResetToDefaults(),
                created);
            defaultPerformanceProfile = FindOrCreate(
                defaultPerformanceProfile,
                folder + "/DefaultRuntimeQueryBudget.asset",
                profile => profile.ApplyStartingPreset(NavigationDeviceTier.MobileMedium),
                created);

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            if (saveSettings)
            {
                Save(true);
            }

            return created;
        }

        internal void ApplyDefaultsTo(NavigationLevel level, bool replaceExistingProfiles)
        {
            if (level == null)
            {
                throw new ArgumentNullException(nameof(level));
            }

            Undo.RecordObject(level, "Apply Custom Navigation Defaults");
            level.ConfigureDefaults(
                replaceExistingProfiles || level.DefaultAgentProfile == null
                    ? defaultAgentProfile
                    : level.DefaultAgentProfile,
                replaceExistingProfiles || level.AreaCatalog == null
                    ? defaultAreaCatalog
                    : level.AreaCatalog,
                replaceExistingProfiles || level.PerformanceProfile == null
                    ? defaultPerformanceProfile
                    : level.PerformanceProfile);
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(DefaultBuildSettings), level.BuildSettings);
            EditorUtility.SetDirty(level);
            PrefabUtility.RecordPrefabInstancePropertyModifications(level);
        }

        internal void SaveProjectSettings()
        {
            Save(true);
        }

        private static T FindOrCreate<T>(
            T current,
            string preferredPath,
            Action<T> initialize,
            ICollection<UnityEngine.Object> created)
            where T : ScriptableObject
        {
            if (current != null)
            {
                return current;
            }

            T existing = AssetDatabase.LoadAssetAtPath<T>(preferredPath);
            if (existing != null)
            {
                return existing;
            }

            string path = AssetDatabase.LoadMainAssetAtPath(preferredPath) == null
                ? preferredPath
                : AssetDatabase.GenerateUniqueAssetPath(preferredPath);
            T asset = CreateInstance<T>();
            asset.name = Path.GetFileNameWithoutExtension(path);
            initialize?.Invoke(asset);
            AssetDatabase.CreateAsset(asset, path);
            EditorUtility.SetDirty(asset);
            created.Add(asset);
            return asset;
        }

        internal static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
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

    internal static class NavigationSettingsProviders
    {
        [SettingsProvider]
        internal static SettingsProvider CreateProjectProvider()
        {
            return new SettingsProvider(
                NavigationProjectSettings.ProjectProviderPath,
                SettingsScope.Project)
            {
                label = "Custom Navigation",
                keywords = new HashSet<string>(new[]
                {
                    "DataSakura", "Navigation", "Agent", "Areas", "Bake", "Runtime", "Budget"
                }),
                guiHandler = _ => DrawProjectSettings()
            };
        }

        [SettingsProvider]
        internal static SettingsProvider CreatePreferencesProvider()
        {
            return new SettingsProvider(
                NavigationProjectSettings.PreferencesProviderPath,
                SettingsScope.User)
            {
                label = "Scene Preview",
                keywords = new HashSet<string>(new[]
                {
                    "DataSakura", "Navigation", "Scene", "Preview", "Highlight"
                }),
                guiHandler = _ => DrawPreviewPreferences()
            };
        }

        private static void DrawProjectSettings()
        {
            NavigationProjectSettings settings = NavigationProjectSettings.instance;
            var serialized = new SerializedObject(settings);
            serialized.Update();

            EditorGUILayout.HelpBox(
                "Project defaults are shared starting points for newly created Navigation Levels. " +
                "Opening this page creates no assets; Create Defaults is the explicit write action.",
                MessageType.Info);

            EditorGUILayout.LabelField("Shared profile defaults", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serialized.FindProperty("defaultAgentProfile"),
                new GUIContent("Agent"));
            EditorGUILayout.PropertyField(serialized.FindProperty("defaultAreaCatalog"),
                new GUIContent("Areas"));
            EditorGUILayout.PropertyField(serialized.FindProperty("defaultPerformanceProfile"),
                new GUIContent("Runtime Query Budget"));

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Bake Quality default", EditorStyles.boldLabel);
            NavigationInspectorGUI.DrawBuildSettings(
                serialized.FindProperty("defaultBuildSettings"),
                "ProjectSettings.BuildSettings",
                settings.DefaultAgentProfile);

            if (serialized.ApplyModifiedProperties())
            {
                settings.SaveProjectSettings();
            }

            EditorGUILayout.Space(10f);
            if (GUILayout.Button("Create Defaults", GUILayout.Height(28f)))
            {
                Undo.RecordObject(settings, "Create Custom Navigation Defaults");
                IReadOnlyList<UnityEngine.Object> created = settings.CreateMissingDefaults();
                Debug.Log(created.Count == 0
                    ? "[CustomNavigation] Project defaults already exist; nothing was overwritten."
                    : $"[CustomNavigation] Created {created.Count} missing project default asset(s).");
            }
        }

        private static void DrawPreviewPreferences()
        {
            EditorGUILayout.HelpBox(
                "Scene Preview is personal editor state shared with the Scene View overlay. " +
                "It is not written to project assets and is never included in a navigation bake.",
                MessageType.Info);
            EditorGUI.BeginChangeCheck();
            NavigationHighlightSettings.SourcesEnabled = EditorGUILayout.ToggleLeft(
                "Sources (sand dotted bounds)",
                NavigationHighlightSettings.SourcesEnabled);
            NavigationHighlightSettings.BakedEnabled = EditorGUILayout.ToggleLeft(
                "Baked (dusty violet surface)",
                NavigationHighlightSettings.BakedEnabled);
            NavigationHighlightSettings.RuntimeEnabled = EditorGUILayout.ToggleLeft(
                "Runtime (light lilac routes)",
                NavigationHighlightSettings.RuntimeEnabled);
            NavigationHighlightSettings.Scope = (NavigationPreviewScope)EditorGUILayout.EnumPopup(
                "Scope",
                NavigationHighlightSettings.Scope);
            NavigationHighlightSettings.Depth = (NavigationPreviewDepth)EditorGUILayout.EnumPopup(
                "Visibility",
                NavigationHighlightSettings.Depth);
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
        }
    }

    internal static class NavigationProfileUsage
    {
        internal static IReadOnlyList<string> Find(UnityEngine.Object profile)
        {
            var usages = new HashSet<string>(StringComparer.Ordinal);
            if (profile == null)
            {
                return Array.Empty<string>();
            }

            NavigationLevel[] loadedLevels = Resources.FindObjectsOfTypeAll<NavigationLevel>();
            for (int i = 0; i < loadedLevels.Length; i++)
            {
                NavigationLevel level = loadedLevels[i];
                if (level == null || !Uses(level, profile))
                {
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(level);
                usages.Add(string.IsNullOrEmpty(path)
                    ? level.gameObject.scene.path + " :: " + level.name
                    : path + " :: " + level.name);
            }

            string profilePath = AssetDatabase.GetAssetPath(profile);
            if (!string.IsNullOrEmpty(profilePath))
            {
                AddDependentAssets("t:Scene", profilePath, usages);
                AddDependentAssets("t:Prefab", profilePath, usages);
            }

            var result = new List<string>(usages);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static bool Uses(NavigationLevel level, UnityEngine.Object profile)
        {
            return level.DefaultAgentProfile == profile
                   || level.AreaCatalog == profile
                   || level.PerformanceProfile == profile;
        }

        private static void AddDependentAssets(
            string filter,
            string profilePath,
            ISet<string> usages)
        {
            string[] guids = AssetDatabase.FindAssets(filter);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string[] dependencies = AssetDatabase.GetDependencies(path, true);
                if (Array.IndexOf(dependencies, profilePath) >= 0)
                {
                    usages.Add(path);
                }
            }
        }
    }

    internal static class NavigationProfileAssets
    {
        internal static T MakeLocalCopy<T>(T source, string suggestedPath)
            where T : ScriptableObject
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            string folder = Path.GetDirectoryName(suggestedPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A local profile copy must be created below the project's Assets folder.",
                    nameof(suggestedPath));
            }

            NavigationProjectSettings.EnsureAssetFolder(folder);
            string path = AssetDatabase.GenerateUniqueAssetPath(suggestedPath);
            T copy = UnityEngine.Object.Instantiate(source);
            copy.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssets();
            return copy;
        }
    }
}
