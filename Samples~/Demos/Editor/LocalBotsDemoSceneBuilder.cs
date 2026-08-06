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
    public static class LocalBotsDemoSceneBuilder
    {
        public const string ScenePath = "Assets/CustomNavigation/Scene/DotRecastLocalBots.unity";

        private const string DemoFolder = "Assets/CustomNavigation/Generated/LocalBotsDemo";
        private const string AgentPath = DemoFolder + "/LocalBots_Agent.asset";
        private const string AreasPath = DemoFolder + "/LocalBots_Areas.asset";
        private const string PerformancePath = DemoFolder + "/LocalBots_MobilePerformance.asset";
        private const string FloorMeshPath = DemoFolder + "/LocalBots_Floor.asset";
        private const string BoxMeshPath = DemoFolder + "/LocalBots_Box.asset";
        private const string FloorMaterialPath = DemoFolder + "/LocalBots_Floor.mat";
        private const string ObstacleMaterialPath = DemoFolder + "/LocalBots_Obstacle.mat";
        private const string BorderMaterialPath = DemoFolder + "/LocalBots_Border.mat";

        private static readonly Vector2 FloorSize = new Vector2(28f, 20f);
        private static readonly Vector3 PlayerStart = new Vector3(-11f, 0f, -7f);

        [MenuItem("Tools/Custom Navigation/Rebuild LocalOnly Bots Scene", priority = 130)]
        public static void Rebuild()
        {
            EnsureAssetFolder(DemoFolder);
            NavigationAgentProfile agent = LoadOrCreate<NavigationAgentProfile>(AgentPath);
            NavigationAreaCatalog areas = LoadOrCreate<NavigationAreaCatalog>(AreasPath);
            if (areas.Areas.Count == 0)
            {
                areas.ResetToDefaults();
                EditorUtility.SetDirty(areas);
            }

            NavigationPerformanceProfile performance = LoadOrCreate<NavigationPerformanceProfile>(PerformancePath);
            if (performance.DeviceTier == NavigationDeviceTier.Custom)
            {
                performance.ApplyStartingPreset(NavigationDeviceTier.MobileMedium);
                EditorUtility.SetDirty(performance);
            }

            Mesh floorMesh = CreateOrUpdateMesh(FloorMeshPath, CreateQuad(FloorSize));
            Mesh boxMesh = CreateOrUpdateMesh(BoxMeshPath, CreateBox(Vector3.one));
            Material floorMaterial = LoadOrCreateMaterial(
                FloorMaterialPath,
                new Color(0.07f, 0.43f, 0.4f, 1f));
            Material obstacleMaterial = LoadOrCreateMaterial(
                ObstacleMaterialPath,
                new Color(0.78f, 0.22f, 0.18f, 1f));
            Material borderMaterial = LoadOrCreateMaterial(
                BorderMaterialPath,
                new Color(0.025f, 0.045f, 0.06f, 1f));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var levelObject = new GameObject("Navigation Level");
            NavigationLevel level = levelObject.AddComponent<NavigationLevel>();
            ConfigureLevel(level, agent, areas, performance);

            CreateMeshObject(
                "Arena border",
                levelObject.transform,
                floorMesh,
                borderMaterial,
                new Vector3(0f, -0.04f, 0f),
                new Vector3(1.035f, 1f, 1.05f),
                NavigationGeometryMode.Ignore);
            CreateMeshObject(
                "Walkable floor",
                levelObject.transform,
                floorMesh,
                floorMaterial,
                Vector3.zero,
                Vector3.one,
                NavigationGeometryMode.Include);

            var obstacles = new[]
            {
                new Obstacle(-3f, -4.4f, 2f, 6.4f),
                new Obstacle(-3f, 4.4f, 2f, 6.4f),
                new Obstacle(4f, -2f, 5f, 2f),
                new Obstacle(7f, 4f, 2f, 6f),
                new Obstacle(-9f, 4f, 4f, 2f),
                new Obstacle(10f, -6f, 3f, 2f)
            };
            const float obstacleHeight = 1.6f;
            for (int i = 0; i < obstacles.Length; i++)
            {
                Obstacle obstacle = obstacles[i];
                GameObject visual = CreateMeshObject(
                    $"Obstacle {i + 1}",
                    levelObject.transform,
                    boxMesh,
                    obstacleMaterial,
                    new Vector3(obstacle.CenterX, obstacleHeight * 0.5f, obstacle.CenterZ),
                    new Vector3(obstacle.SizeX, obstacleHeight, obstacle.SizeZ),
                    NavigationGeometryMode.Ignore);
                visual.AddComponent<NavigationModifierVolume>();
            }

            var runtimeObject = new GameObject("LocalOnly Runtime");
            NavigationQuerySchedulerBehaviour scheduler =
                runtimeObject.AddComponent<NavigationQuerySchedulerBehaviour>();
            LocalOnlyBotsNavigationDemo demo = runtimeObject.AddComponent<LocalOnlyBotsNavigationDemo>();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            NavigationArtifactBuildResult artifact = NavigationArtifactBuilder.BuildAndExport(level);
            scheduler.Configure(artifact.Asset, performance, agent);
            demo.Configure(scheduler, FloorSize, 24, PlayerStart);
            EditorUtility.SetDirty(scheduler);
            EditorUtility.SetDirty(demo);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = runtimeObject;
            Debug.Log(
                $"[CustomNavigation] Rebuilt LocalOnly mobile bots scene with artifact " +
                $"{artifact.Hash}, {artifact.PolygonCount} polygons and 24 budgeted bots.",
                runtimeObject);
        }

        private static void ConfigureLevel(
            NavigationLevel level,
            NavigationAgentProfile agent,
            NavigationAreaCatalog areas,
            NavigationPerformanceProfile performance)
        {
            level.ConfigureDefaults(agent, areas, performance);
            var serialized = new SerializedObject(level);
            serialized.Update();
            serialized.FindProperty("levelId").stringValue = "local_bots_arena";
            serialized.FindProperty("description").stringValue =
                "Mobile arena for LocalOnly pathfinding: the player and 24 bots " +
                "share a single sliced query budget with no HTTP server.";
            serialized.FindProperty("geometryRoot").objectReferenceValue = level.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(level);
        }

        private static GameObject CreateMeshObject(
            string objectName,
            Transform parent,
            Mesh mesh,
            Material material,
            Vector3 position,
            Vector3 scale,
            NavigationGeometryMode mode)
        {
            var value = new GameObject(objectName);
            value.transform.SetParent(parent, false);
            value.transform.position = position;
            value.transform.localScale = scale;
            value.AddComponent<MeshFilter>().sharedMesh = mesh;
            value.AddComponent<MeshRenderer>().sharedMaterial = material;
            NavigationGeometrySource source = value.AddComponent<NavigationGeometrySource>();
            var serialized = new SerializedObject(source);
            serialized.Update();
            serialized.FindProperty("mode").enumValueIndex = (int)mode;
            serialized.FindProperty("area").intValue = (int)NavigationArea.Ground;
            serialized.FindProperty("includeChildren").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return value;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value != null)
            {
                return value;
            }

            value = ScriptableObject.CreateInstance<T>();
            value.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(value, path);
            return value;
        }

        private static Mesh CreateOrUpdateMesh(string path, Mesh generated)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Material LoadOrCreateMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader == null)
                {
                    throw new InvalidOperationException("No compatible unlit shader is available.");
                }

                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh CreateQuad(Vector2 size)
        {
            float halfX = size.x * 0.5f;
            float halfZ = size.y * 0.5f;
            var mesh = new Mesh { name = "LocalOnly floor" };
            mesh.vertices = new[]
            {
                new Vector3(-halfX, 0f, -halfZ),
                new Vector3(halfX, 0f, -halfZ),
                new Vector3(halfX, 0f, halfZ),
                new Vector3(-halfX, 0f, halfZ)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateBox(Vector3 size)
        {
            Vector3 half = size * 0.5f;
            var mesh = new Mesh { name = "LocalOnly obstacle" };
            mesh.vertices = new[]
            {
                new Vector3(-half.x, -half.y, -half.z),
                new Vector3(half.x, -half.y, -half.z),
                new Vector3(half.x, -half.y, half.z),
                new Vector3(-half.x, -half.y, half.z),
                new Vector3(-half.x, half.y, -half.z),
                new Vector3(half.x, half.y, -half.z),
                new Vector3(half.x, half.y, half.z),
                new Vector3(-half.x, half.y, half.z)
            };
            mesh.triangles = new[]
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6,
                0, 4, 5, 0, 5, 1,
                1, 5, 6, 1, 6, 2,
                2, 6, 7, 2, 7, 3,
                3, 7, 4, 3, 4, 0
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(value => string.Equals(value.path, scenePath, StringComparison.Ordinal)))
            {
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private readonly struct Obstacle
        {
            public readonly float CenterX;
            public readonly float CenterZ;
            public readonly float SizeX;
            public readonly float SizeZ;

            public Obstacle(float centerX, float centerZ, float sizeX, float sizeZ)
            {
                CenterX = centerX;
                CenterZ = centerZ;
                SizeX = sizeX;
                SizeZ = sizeZ;
            }
        }
    }
}
