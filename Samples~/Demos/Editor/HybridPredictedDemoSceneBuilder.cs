using System;
using System.Collections.Generic;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomNavigation.Editor
{
    public static class HybridPredictedDemoSceneBuilder
    {
        public const string ScenePath = "Assets/DataSakura/CustomNavigation/Scenes/DotRecastHybridPredicted.unity";

        public static void Rebuild()
        {
            Scene scene = EditorSceneManager.OpenScene(
                LocalBotsDemoSceneBuilder.ScenePath,
                OpenSceneMode.Single);
            GameObject previousRuntime = GameObject.Find("LocalOnly Runtime");
            if (previousRuntime != null)
            {
                UnityEngine.Object.DestroyImmediate(previousRuntime);
            }

            NavigationArtifactAsset artifact = AssetDatabase.LoadAssetAtPath<NavigationArtifactAsset>(
                "Assets/DataSakura/CustomNavigation/Generated/Navigation/local_bots_arena.navigation.asset");
            NavigationAgentProfile agent = AssetDatabase.LoadAssetAtPath<NavigationAgentProfile>(
                "Assets/DataSakura/CustomNavigation/Generated/LocalBotsDemo/LocalBots_Agent.asset");
            NavigationPerformanceProfile performance =
                AssetDatabase.LoadAssetAtPath<NavigationPerformanceProfile>(
                    "Assets/DataSakura/CustomNavigation/Generated/LocalBotsDemo/LocalBots_MobilePerformance.asset");
            if (artifact == null || agent == null || performance == null)
            {
                throw new InvalidOperationException(
                    "Rebuild the LocalOnly Bots scene before creating the HybridPredicted scene.");
            }

            var runtimeObject = new GameObject("HybridPredicted Runtime");
            NavigationQuerySchedulerBehaviour scheduler =
                runtimeObject.AddComponent<NavigationQuerySchedulerBehaviour>();
            scheduler.Configure(artifact, performance, agent);
            HybridPredictedNavigationDemo demo =
                runtimeObject.AddComponent<HybridPredictedNavigationDemo>();
            demo.Configure(
                scheduler,
                "http://127.0.0.1:5079",
                new Vector3(-11f, 0f, -7f),
                new Vector3(11f, 0f, 7f));

            EditorSceneManager.MarkSceneDirty(scene);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Selection.activeObject = runtimeObject;
            Debug.Log(
                $"[CustomNavigation] Rebuilt HybridPredicted scene with shared artifact " +
                $"{artifact.ArtifactHash}.",
                runtimeObject);
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(value => string.Equals(value.path, scenePath, StringComparison.Ordinal)))
            {
                scenes.Add(new EditorBuildSettingsScene(scenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }
    }
}
