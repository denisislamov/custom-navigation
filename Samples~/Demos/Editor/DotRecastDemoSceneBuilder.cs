using System;
using System.Collections.Generic;
using System.IO;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomNavigation.Editor
{
    public static class DotRecastDemoSceneBuilder
    {
        public const string ScenePath = "Assets/CustomNavigation/Scene/DotRecastTopDown.unity";
        public const string ServerClientScenePath = "Assets/CustomNavigation/Scene/DotRecastServerClient.unity";

        private const string ArtifactPath =
            "Assets/CustomNavigation/Generated/Navigation/local_bots_arena.artifact.asset";
        private const string AgentProfilePath =
            "Assets/CustomNavigation/Generated/LocalBotsDemo/LocalBots_Agent.asset";
        private const string PerformanceProfilePath =
            "Assets/CustomNavigation/Generated/LocalBotsDemo/LocalBots_MobilePerformance.asset";
        private const string AgentMeshPath =
            "Assets/CustomNavigation/Generated/LocalBotsDemo/LocalBots_Box.asset";
        private const string AgentMaterialPath =
            "Assets/CustomNavigation/Generated/LocalBotsDemo/LocalBots_Obstacle.mat";
        private const string PathMaterialPath =
            "Assets/CustomNavigation/Generated/LocalBotsDemo/LocalBots_Floor.mat";

        private static readonly Vector2 ArenaSize = new Vector2(28f, 20f);
        private static readonly Vector3 AgentStart = new Vector3(-11f, 0f, -7f);
        private static readonly Vector3 InitialDestination = new Vector3(11f, 0f, 7f);

        [InitializeOnLoadMethod]
        private static void CreateMissingSceneAfterImport()
        {
            if (File.Exists(ScenePath) && File.Exists(ServerClientScenePath))
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    if (!File.Exists(ScenePath))
                    {
                        RebuildScene();
                    }
                    if (!File.Exists(ServerClientScenePath))
                    {
                        RebuildServerClientScene();
                    }
                }
            };
        }

        public static void RebuildScene()
        {
            Scene scene = OpenStaticArenaSource();
            NavigationArtifactAsset artifact = LoadRequired<NavigationArtifactAsset>(ArtifactPath);
            NavigationAgentProfile agentProfile = LoadRequired<NavigationAgentProfile>(AgentProfilePath);
            NavigationPerformanceProfile performance =
                LoadRequired<NavigationPerformanceProfile>(PerformanceProfilePath);
            Mesh agentMesh = LoadRequired<Mesh>(AgentMeshPath);
            Material agentMaterial = LoadRequired<Material>(AgentMaterialPath);
            Material pathMaterial = LoadRequired<Material>(PathMaterialPath);

            var runtimeObject = new GameObject("Top-down Local Artifact Runtime");
            NavigationQuerySchedulerBehaviour scheduler =
                runtimeObject.AddComponent<NavigationQuerySchedulerBehaviour>();
            scheduler.Configure(artifact, performance, agentProfile);
            DotRecastTopDownDemo demo = runtimeObject.AddComponent<DotRecastTopDownDemo>();
            demo.Configure(
                scheduler,
                ArenaSize,
                AgentStart,
                InitialDestination,
                agentMesh,
                agentMaterial,
                pathMaterial);

            EditorUtility.SetDirty(scheduler);
            EditorUtility.SetDirty(demo);
            EditorSceneManager.MarkSceneDirty(scene);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = runtimeObject;
            Debug.Log(
                $"[CustomNavigation] Rebuilt static TopDown scene with artifact " +
                $"{artifact.ArtifactHash} and no runtime geometry generation.",
                runtimeObject);
        }

        public static void RebuildServerClientScene()
        {
            Scene scene = OpenStaticArenaSource();
            NavigationArtifactAsset artifact = LoadRequired<NavigationArtifactAsset>(ArtifactPath);
            Mesh agentMesh = LoadRequired<Mesh>(AgentMeshPath);
            Material agentMaterial = LoadRequired<Material>(AgentMaterialPath);
            Material pathMaterial = LoadRequired<Material>(PathMaterialPath);

            var runtimeObject = new GameObject("Static Server Client Runtime");
            ServerNavigationTopDownDemo demo = runtimeObject.AddComponent<ServerNavigationTopDownDemo>();
            demo.Configure(
                artifact.LevelId,
                artifact.ArtifactHash,
                ArenaSize,
                AgentStart,
                InitialDestination,
                agentMesh,
                agentMaterial,
                pathMaterial);

            EditorUtility.SetDirty(demo);
            EditorSceneManager.MarkSceneDirty(scene);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ServerClientScenePath));
            EditorSceneManager.SaveScene(scene, ServerClientScenePath);
            AddSceneToBuildSettings(ServerClientScenePath);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = runtimeObject;
            Debug.Log(
                $"[CustomNavigation] Rebuilt static ServerClient scene for " +
                $"{artifact.LevelId}/{artifact.ArtifactHash}.",
                runtimeObject);
        }

        public static void RebuildSceneFromCommandLine()
        {
            RebuildScene();
            RebuildServerClientScene();
            AssetDatabase.SaveAssets();
        }

        private static Scene OpenStaticArenaSource()
        {
            EnsureStaticArenaSource();
            Scene scene = EditorSceneManager.OpenScene(
                LocalBotsDemoSceneBuilder.ScenePath,
                OpenSceneMode.Single);
            GameObject previousRuntime = GameObject.Find("LocalOnly Runtime");
            if (previousRuntime != null)
            {
                UnityEngine.Object.DestroyImmediate(previousRuntime);
            }
            return scene;
        }

        private static void EnsureStaticArenaSource()
        {
            if (File.Exists(LocalBotsDemoSceneBuilder.ScenePath)
                && AssetDatabase.LoadAssetAtPath<NavigationArtifactAsset>(ArtifactPath) != null
                && AssetDatabase.LoadAssetAtPath<Mesh>(AgentMeshPath) != null)
            {
                return;
            }

            LocalBotsDemoSceneBuilder.Rebuild();
        }

        private static T LoadRequired<T>(string assetPath) where T : UnityEngine.Object
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (value == null)
            {
                throw new InvalidOperationException(
                    $"Required static navigation asset is missing: {assetPath}. " +
                    "Rebuild the LocalOnly Bots scene first.");
            }
            return value;
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
