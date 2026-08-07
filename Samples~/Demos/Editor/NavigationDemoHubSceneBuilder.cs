using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomNavigation.Editor
{
    public static class NavigationDemoHubSceneBuilder
    {
        public const string ScenePath = "Assets/CustomNavigation/Scene/DotRecastDemoHub.unity";
        private const string TopDownScenePath = "Assets/CustomNavigation/Scene/DotRecastTopDown.unity";
        private const string ServerClientScenePath = "Assets/CustomNavigation/Scene/DotRecastServerClient.unity";

        private static readonly string[] DemoScenePaths =
        {
            ScenePath,
            TopDownScenePath,
            ServerClientScenePath,
            LocalBotsDemoSceneBuilder.ScenePath,
            HybridPredictedDemoSceneBuilder.ScenePath,
            MultiLevelDemoSceneBuilder.ScenePath
        };

        public static void Rebuild()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Custom Navigation Demo Hub");
            NavigationDemoHub hub = root.AddComponent<NavigationDemoHub>();
            hub.Configure(
                "Custom Navigation / DotRecast",
                "Start catalog of physics-free navigation samples. Pick a scenario " +
                "to check the local, server, hybrid or multi-level mode. " +
                "During the demo use the back button in the lower left corner or Escape/Back.",
                CreateEntries());

            EditorUtility.SetDirty(hub);
            EditorSceneManager.MarkSceneDirty(scene);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureBuildSettings();
            AssetDatabase.SaveAssets();
            Selection.activeObject = root;
            Debug.Log(
                $"[CustomNavigation] Rebuilt demo hub with {hub.Scenes.Length} level entries. " +
                "The hub is first in Build Settings.",
                root);
        }

        public static void ReviewDemoLevels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(
                    "[CustomNavigation] Stop Play Mode before reviewing demo levels. " +
                    "The review opens every scene in Edit Mode.");
                return;
            }

            string previousScenePath = SceneManager.GetActiveScene().path;
            var findings = new List<string>();
            try
            {
                EnsureBuildSettings();
                ReviewHub(findings);
                ReviewTopDown(findings);
                ReviewServerClient(findings);
                ReviewLocalOnly(findings);
                ReviewHybrid(findings);
                ReviewMultiLevel(findings);
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(previousScenePath) && File.Exists(previousScenePath))
                {
                    EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
                }
            }

            if (findings.Count > 0)
            {
                throw new InvalidOperationException(
                    "Demo level review failed:\n- " + string.Join("\n- ", findings));
            }

            Debug.Log(
                "[CustomNavigation] Demo level review passed: hub + 5 launchable scenes, " +
                "required runtime components, descriptions and build order are valid.");
        }

        private static NavigationDemoSceneEntry[] CreateEntries()
        {
            return new[]
            {
                new NavigationDemoSceneEntry(
                    "Top-Down Static Local",
                    Path.GetFileNameWithoutExtension(TopDownScenePath),
                    "The geometry is saved in the scene, a prebuilt artifact is loaded without a runtime bake, " +
                    "and the Transform moves along the local Detour path.",
                    "LOCAL / STATIC LEVEL",
                    false),
                new NavigationDemoSceneEntry(
                    "Standalone Server Client",
                    Path.GetFileNameWithoutExtension(ServerClientScenePath),
                    "A thin HTTP client with no local DotRecast. The geometry is saved in the Unity scene " +
                    "while the standalone server loads the matching artifact and returns the path.",
                    "SERVER / STATIC LEVEL",
                    true),
                new NavigationDemoSceneEntry(
                    "LocalOnly Mobile Bots",
                    Path.GetFileNameWithoutExtension(LocalBotsDemoSceneBuilder.ScenePath),
                    "The player and 24 bots use a prebuilt artifact and a shared sliced scheduler with mobile " +
                    "CPU/iteration/queue budgets.",
                    "LOCAL ONLY",
                    false),
                new NavigationDemoSceneEntry(
                    "Hybrid Predicted",
                    Path.GetFileNameWithoutExtension(HybridPredictedDemoSceneBuilder.ScenePath),
                    "The local route hides the latency, then the authoritative server confirms or " +
                    "corrects it with a warning on both sides.",
                    "HYBRID PREDICTED",
                    true),
                new NavigationDemoSceneEntry(
                    "Multi-Level Ramps",
                    Path.GetFileNameWithoutExtension(MultiLevelDemoSceneBuilder.ScenePath),
                    "Three platforms at Y=0/2.5/5 m and two sloped ramps without manual floor tagging. " +
                    "The path contains polygon crossings and preserves the vertical profile.",
                    "LOCAL / 3D",
                    false)
            };
        }

        private static void EnsureBuildSettings()
        {
            var ordered = new List<EditorBuildSettingsScene>();
            for (int i = 0; i < DemoScenePaths.Length; i++)
            {
                if (File.Exists(DemoScenePaths[i]))
                {
                    ordered.Add(new EditorBuildSettingsScene(DemoScenePaths[i], true));
                }
            }

            ordered.AddRange(EditorBuildSettings.scenes.Where(existing =>
                !DemoScenePaths.Contains(existing.path, StringComparer.Ordinal)));
            EditorBuildSettings.scenes = ordered.ToArray();
        }

        private static void ReviewHub(List<string> findings)
        {
            Scene scene = OpenRequiredScene(ScenePath, findings);
            if (!scene.IsValid())
            {
                return;
            }

            NavigationDemoHub hub = FindComponent<NavigationDemoHub>(scene);
            if (hub == null)
            {
                findings.Add("Demo Hub has no NavigationDemoHub component.");
                return;
            }

            if (string.IsNullOrWhiteSpace(hub.Description))
            {
                findings.Add("Demo Hub description is empty.");
            }

            if (hub.Scenes == null || hub.Scenes.Length != 5)
            {
                findings.Add("Demo Hub must contain exactly 5 reviewed demo entries.");
            }

            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            if (buildScenes.Length == 0
                || !string.Equals(buildScenes[0].path, ScenePath, StringComparison.Ordinal)
                || !buildScenes[0].enabled)
            {
                findings.Add("Demo Hub is not the first enabled scene in Build Settings.");
            }
        }

        private static void ReviewTopDown(List<string> findings)
        {
            Scene scene = OpenRequiredScene(TopDownScenePath, findings);
            if (scene.IsValid() && !HasComponent(scene, "DotRecastTopDownDemo"))
            {
                findings.Add("Top-Down scene has no DotRecastTopDownDemo component.");
            }
        }

        private static void ReviewServerClient(List<string> findings)
        {
            Scene scene = OpenRequiredScene(ServerClientScenePath, findings);
            if (scene.IsValid() && !HasComponent(scene, "ServerNavigationTopDownDemo"))
            {
                findings.Add("Server Client scene has no ServerNavigationTopDownDemo component.");
            }
        }

        private static void ReviewLocalOnly(List<string> findings)
        {
            Scene scene = OpenRequiredScene(LocalBotsDemoSceneBuilder.ScenePath, findings);
            if (!scene.IsValid())
            {
                return;
            }

            if (FindComponent<LocalOnlyBotsNavigationDemo>(scene) == null)
            {
                findings.Add("LocalOnly scene has no LocalOnlyBotsNavigationDemo component.");
            }

            ReviewNavigationLevel(scene, "LocalOnly", findings);
        }

        private static void ReviewHybrid(List<string> findings)
        {
            Scene scene = OpenRequiredScene(HybridPredictedDemoSceneBuilder.ScenePath, findings);
            if (!scene.IsValid())
            {
                return;
            }

            if (FindComponent<HybridPredictedNavigationDemo>(scene) == null)
            {
                findings.Add("Hybrid scene has no HybridPredictedNavigationDemo component.");
            }

            ReviewNavigationLevel(scene, "Hybrid", findings);
        }

        private static void ReviewMultiLevel(List<string> findings)
        {
            Scene scene = OpenRequiredScene(MultiLevelDemoSceneBuilder.ScenePath, findings);
            if (!scene.IsValid())
            {
                return;
            }

            if (FindComponent<MultiLevelNavigationDemo>(scene) == null)
            {
                findings.Add("Multi-Level scene has no MultiLevelNavigationDemo component.");
            }

            ReviewNavigationLevel(scene, "Multi-Level", findings);
        }

        private static void ReviewNavigationLevel(
            Scene scene,
            string label,
            List<string> findings)
        {
            NavigationLevel level = FindComponent<NavigationLevel>(scene);
            if (level == null)
            {
                findings.Add($"{label} scene has no NavigationLevel component.");
                return;
            }

            if (string.IsNullOrWhiteSpace(level.Description))
            {
                findings.Add($"{label} NavigationLevel description is empty.");
            }
        }

        private static Scene OpenRequiredScene(string path, List<string> findings)
        {
            if (!File.Exists(path))
            {
                findings.Add($"Scene is missing: {path}");
                return default;
            }

            EditorBuildSettingsScene buildEntry = EditorBuildSettings.scenes.FirstOrDefault(
                value => string.Equals(value.path, path, StringComparison.Ordinal));
            if (buildEntry == null || !buildEntry.enabled)
            {
                findings.Add($"Scene is not enabled in Build Settings: {path}");
            }

            return EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        private static T FindComponent<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T value = root.GetComponentInChildren<T>(true);
                if (value != null)
                {
                    return value;
                }
            }

            return null;
        }

        private static bool HasComponent(Scene scene, string typeName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Component component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component != null
                        && string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
