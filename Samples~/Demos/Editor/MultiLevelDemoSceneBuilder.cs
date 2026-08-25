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
    public static class MultiLevelDemoSceneBuilder
    {
        public const string ScenePath = "Assets/DataSakura/CustomNavigation/Scenes/DotRecastMultiLevel.unity";

        private const string DemoFolder = "Assets/DataSakura/CustomNavigation/Generated/MultiLevelDemo";
        private const string AgentPath = DemoFolder + "/MultiLevel_Agent.asset";
        private const string AreasPath = DemoFolder + "/MultiLevel_Areas.asset";
        private const string PerformancePath = DemoFolder + "/MultiLevel_MobilePerformance.asset";
        private const string PlatformMeshPath = DemoFolder + "/MultiLevel_Platform.asset";
        private const string RampMeshPath = DemoFolder + "/MultiLevel_Ramp.asset";
        private const string BoxMeshPath = DemoFolder + "/MultiLevel_Box.asset";
        private const string LowerMaterialPath = DemoFolder + "/MultiLevel_Lower.mat";
        private const string MiddleMaterialPath = DemoFolder + "/MultiLevel_Middle.mat";
        private const string UpperMaterialPath = DemoFolder + "/MultiLevel_Upper.mat";
        private const string RampMaterialPath = DemoFolder + "/MultiLevel_Ramp.mat";
        private const string SupportMaterialPath = DemoFolder + "/MultiLevel_Support.mat";

        private static readonly Vector3 PlayerStart = new Vector3(-11f, 0f, -3f);
        private static readonly Vector3 InitialDestination = new Vector3(19f, 5f, 3f);

        public static void Rebuild()
        {
            EnsureAssetFolder(DemoFolder);
            NavigationAgentProfile agent = LoadOrCreate<NavigationAgentProfile>(AgentPath);
            ConfigureAgent(agent);
            NavigationAreaCatalog areas = LoadOrCreate<NavigationAreaCatalog>(AreasPath);
            if (areas.Areas.Count == 0)
            {
                areas.ResetToDefaults();
                EditorUtility.SetDirty(areas);
            }

            NavigationPerformanceProfile performance = LoadOrCreate<NavigationPerformanceProfile>(PerformancePath);
            performance.ApplyStartingPreset(NavigationDeviceTier.MobileMedium);
            EditorUtility.SetDirty(performance);

            Mesh platformMesh = CreateOrUpdateMesh(PlatformMeshPath, CreatePlatformMesh());
            Mesh rampMesh = CreateOrUpdateMesh(RampMeshPath, CreateRampMesh());
            Mesh boxMesh = CreateOrUpdateMesh(BoxMeshPath, CreateBoxMesh());
            Material lowerMaterial = LoadOrCreateMaterial(
                LowerMaterialPath,
                new Color(0.05f, 0.48f, 0.42f, 1f));
            Material middleMaterial = LoadOrCreateMaterial(
                MiddleMaterialPath,
                new Color(0.08f, 0.42f, 0.72f, 1f));
            Material upperMaterial = LoadOrCreateMaterial(
                UpperMaterialPath,
                new Color(0.43f, 0.24f, 0.76f, 1f));
            Material rampMaterial = LoadOrCreateMaterial(
                RampMaterialPath,
                new Color(0.2f, 0.72f, 0.62f, 1f));
            Material supportMaterial = LoadOrCreateMaterial(
                SupportMaterialPath,
                new Color(0.025f, 0.055f, 0.08f, 1f));

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var levelObject = new GameObject("Multi-Level Navigation Level");
            NavigationLevel level = levelObject.AddComponent<NavigationLevel>();
            ConfigureLevel(level, agent, areas, performance);

            var walkableMeshes = new List<MeshFilter>();
            walkableMeshes.Add(CreateMeshObject(
                "Lower platform (Y 0)",
                levelObject.transform,
                platformMesh,
                lowerMaterial,
                new Vector3(-10f, 0f, 0f),
                Vector3.one,
                NavigationGeometryMode.Include,
                1));
            walkableMeshes.Add(CreateMeshObject(
                "Ramp Y 0 to 2.5",
                levelObject.transform,
                rampMesh,
                rampMaterial,
                new Vector3(-6f, 0f, 0f),
                Vector3.one,
                NavigationGeometryMode.Include,
                2));
            walkableMeshes.Add(CreateMeshObject(
                "Middle platform (Y 2.5)",
                levelObject.transform,
                platformMesh,
                middleMaterial,
                new Vector3(4f, 2.5f, 0f),
                Vector3.one,
                NavigationGeometryMode.Include,
                1));
            walkableMeshes.Add(CreateMeshObject(
                "Ramp Y 2.5 to 5",
                levelObject.transform,
                rampMesh,
                rampMaterial,
                new Vector3(8f, 2.5f, 0f),
                Vector3.one,
                NavigationGeometryMode.Include,
                2));
            walkableMeshes.Add(CreateMeshObject(
                "Upper platform (Y 5)",
                levelObject.transform,
                platformMesh,
                upperMaterial,
                new Vector3(18f, 5f, 0f),
                Vector3.one,
                NavigationGeometryMode.Include,
                1));

            CreateMeshObject(
                "Lower platform support",
                levelObject.transform,
                boxMesh,
                supportMaterial,
                new Vector3(-10f, -0.3f, 0f),
                new Vector3(8f, 0.6f, 12f),
                NavigationGeometryMode.Ignore,
                1);
            CreateMeshObject(
                "Middle platform support",
                levelObject.transform,
                boxMesh,
                supportMaterial,
                new Vector3(4f, 1f, 0f),
                new Vector3(8f, 3f, 12f),
                NavigationGeometryMode.Ignore,
                1);
            CreateMeshObject(
                "Upper platform support",
                levelObject.transform,
                boxMesh,
                supportMaterial,
                new Vector3(18f, 2.25f, 0f),
                new Vector3(8f, 5.5f, 12f),
                NavigationGeometryMode.Ignore,
                1);

            CreateTestPoint(levelObject.transform, "spawn_lower", PlayerStart);
            CreateTestPoint(levelObject.transform, "checkpoint_middle", new Vector3(4f, 2.5f, 3f));
            CreateTestPoint(levelObject.transform, "objective_upper", InitialDestination);

            BuildSelectionGeometry(
                walkableMeshes,
                out Vector3[] selectionVertices,
                out int[] selectionTriangles);

            var runtimeObject = new GameObject("Multi-Level Local Runtime");
            NavigationQuerySchedulerBehaviour scheduler =
                runtimeObject.AddComponent<NavigationQuerySchedulerBehaviour>();
            MultiLevelNavigationDemo demo = runtimeObject.AddComponent<MultiLevelNavigationDemo>();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            // LocalOnly demo: client artifact only, no server export (see LocalBotsDemoSceneBuilder).
            NavigationBakeResult artifact = NavigationBakeCommand.Execute(level);
            scheduler.Configure(artifact.Asset, performance, agent);
            demo.Configure(
                scheduler,
                PlayerStart,
                InitialDestination,
                selectionVertices,
                selectionTriangles);
            EditorUtility.SetDirty(scheduler);
            EditorUtility.SetDirty(demo);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = runtimeObject;
            Debug.Log(
                $"[CustomNavigation] Rebuilt multi-level scene: 3 height levels, 2 ramps, " +
                $"artifact={artifact.Hash}, polygons={artifact.PolygonCount}.",
                runtimeObject);
        }

        private static void ConfigureAgent(NavigationAgentProfile agent)
        {
            var serialized = new SerializedObject(agent);
            serialized.Update();
            serialized.FindProperty("profileId").stringValue = "human_multilevel";
            serialized.FindProperty("height").floatValue = 1.8f;
            serialized.FindProperty("radius").floatValue = 0.38f;
            serialized.FindProperty("maximumClimb").floatValue = 0.4f;
            serialized.FindProperty("maximumSlope").floatValue = 40f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(agent);
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
            serialized.FindProperty("levelId").stringValue = "multilevel_ramp_demo";
            serialized.FindProperty("description").stringValue =
                "Three elevated platforms at Y=0/2.5/5 m connected by two walkable " +
                "ramps. DotRecast derives vertical connectivity directly from the geometry.";
            serialized.FindProperty("geometryRoot").objectReferenceValue = level.transform;

            SerializedProperty buildSettings = serialized.FindProperty("buildSettings");
            buildSettings.FindPropertyRelative("cellSize").floatValue = 0.2f;
            buildSettings.FindPropertyRelative("cellHeight").floatValue = 0.1f;
            buildSettings.FindPropertyRelative("minimumRegionArea").floatValue = 1f;
            buildSettings.FindPropertyRelative("mergedRegionArea").floatValue = 4f;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(level);
        }

        private static MeshFilter CreateMeshObject(
            string objectName,
            Transform parent,
            Mesh mesh,
            Material material,
            Vector3 position,
            Vector3 scale,
            NavigationGeometryMode mode,
            int areaId)
        {
            var value = new GameObject(objectName);
            value.transform.SetParent(parent, false);
            value.transform.position = position;
            value.transform.localScale = scale;
            MeshFilter filter = value.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            value.AddComponent<MeshRenderer>().sharedMaterial = material;
            NavigationGeometrySource source = value.AddComponent<NavigationGeometrySource>();
            var serialized = new SerializedObject(source);
            serialized.Update();
            serialized.FindProperty("mode").enumValueIndex = (int)mode;
            serialized.FindProperty("area").intValue = areaId;
            serialized.FindProperty("includeChildren").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return filter;
        }

        private static void CreateTestPoint(
            Transform parent,
            string pointId,
            Vector3 position)
        {
            var value = new GameObject($"Test Point — {pointId}");
            value.transform.SetParent(parent, false);
            value.transform.position = position;
            NavigationTestPoint point = value.AddComponent<NavigationTestPoint>();
            var serialized = new SerializedObject(point);
            serialized.Update();
            serialized.FindProperty("pointId").stringValue = pointId;
            serialized.FindProperty("pointType").enumValueIndex = pointId == "spawn_lower"
                ? (int)NavigationTestPointType.TeamSpawn
                : pointId == "objective_upper"
                    ? (int)NavigationTestPointType.Objective
                    : (int)NavigationTestPointType.Patrol;
            serialized.FindProperty("group").stringValue = "vertical_route";
            serialized.FindProperty("required").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildSelectionGeometry(
            IReadOnlyList<MeshFilter> meshes,
            out Vector3[] vertices,
            out int[] triangles)
        {
            var combinedVertices = new List<Vector3>();
            var combinedTriangles = new List<int>();
            for (int meshIndex = 0; meshIndex < meshes.Count; meshIndex++)
            {
                MeshFilter filter = meshes[meshIndex];
                Mesh mesh = filter.sharedMesh;
                int vertexOffset = combinedVertices.Count;
                Vector3[] localVertices = mesh.vertices;
                for (int i = 0; i < localVertices.Length; i++)
                {
                    combinedVertices.Add(filter.transform.TransformPoint(localVertices[i]));
                }

                int[] localTriangles = mesh.triangles;
                for (int i = 0; i < localTriangles.Length; i++)
                {
                    combinedTriangles.Add(vertexOffset + localTriangles[i]);
                }
            }

            vertices = combinedVertices.ToArray();
            triangles = combinedTriangles.ToArray();
        }

        private static Mesh CreatePlatformMesh()
        {
            var mesh = new Mesh { name = "Multi-level platform 8x12" };
            mesh.vertices = new[]
            {
                new Vector3(-4f, 0f, -6f),
                new Vector3(4f, 0f, -6f),
                new Vector3(4f, 0f, 6f),
                new Vector3(-4f, 0f, 6f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateRampMesh()
        {
            var mesh = new Mesh { name = "Multi-level ramp 6x4 rise 2.5" };
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, -2f),
                new Vector3(6f, 2.5f, -2f),
                new Vector3(6f, 2.5f, 2f),
                new Vector3(0f, 0f, 2f)
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateBoxMesh()
        {
            Vector3 half = Vector3.one * 0.5f;
            var mesh = new Mesh { name = "Multi-level visual support" };
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
            if (!scenes.Exists(value => string.Equals(value.path, scenePath, StringComparison.Ordinal)))
            {
                scenes.Add(new EditorBuildSettingsScene(scenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }
    }
}
