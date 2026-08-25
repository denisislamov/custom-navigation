using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DotRecast.Detour;
using DotRecast.Recast;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CustomNavigation.Editor
{
    public static class PhysicsFreeVerification
    {
        private static readonly string[] ForbiddenRuntimeCalls =
        {
            "Physics.Raycast",
            "Physics.Linecast",
            "Physics.SphereCast",
            "Physics.BoxCast",
            "Physics.CapsuleCast",
            "Physics.Overlap",
            "Physics2D.",
            "CreatePrimitive(",
            "Collider",
            "Rigidbody",
            "CharacterController",
            "NavMeshAgent",
            "NavMeshObstacle",
            "NavMeshSurface"
        };

        private static readonly HashSet<string> ForbiddenSceneComponents = new HashSet<string>
        {
            "UnityEngine.Rigidbody",
            "UnityEngine.Rigidbody2D",
            "UnityEngine.CharacterController",
            "UnityEngine.ArticulationBody",
            "UnityEngine.AI.NavMeshAgent",
            "UnityEngine.AI.NavMeshObstacle"
        };

        public static void Verify()
        {
            VerifyProjectSources();
            VerifyServerClientIsThin();
            VerifyDotRecastAssemblies();
            VerifySerializedScene();
            Debug.Log(
                "Custom Navigation verification passed: runtime, authoring, editor pipeline, " +
                "standalone server and all demo scenes are physics-free; the HTTP client " +
                "contains no local pathfinding calls.");
        }

        private static void VerifyServerClientIsThin()
        {
            string clientPath = Path.Combine(
                ResolveClientRoot(),
                "ServerNavigationTopDownDemo.cs");
            string source = File.ReadAllText(clientPath);
            string[] forbiddenClientApis =
            {
                "using DotRecast",
                "RcBuilder",
                "RcConfig",
                "DtNavMesh",
                "DtNavMeshQuery",
                "TryFindPath("
            };

            foreach (string forbiddenApi in forbiddenClientApis)
            {
                if (source.Contains(forbiddenApi, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Local navigation API '{forbiddenApi}' found in the server-client scene runtime.");
                }
            }
        }

        public static void VerifyFromCommandLine()
        {
            Verify();
        }

        /// <summary>
        /// Locates the folder with the client demo sources relative to this script, so the
        /// check works both in the source repository and after importing the package sample
        /// (Assets/Samples/DataSakura Custom Navigation/...).
        /// </summary>
        private static string ResolveClientRoot()
        {
            string[] guids = AssetDatabase.FindAssets("PhysicsFreeVerification t:MonoScript");
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith("PhysicsFreeVerification.cs", StringComparison.Ordinal))
                {
                    // .../<ClientRoot>/Editor/PhysicsFreeVerification.cs -> <ClientRoot>
                    string editorFolder = Path.GetDirectoryName(Path.GetFullPath(assetPath));
                    return Path.GetDirectoryName(editorFolder)
                        ?? throw new InvalidOperationException(
                            "Cannot resolve the client root above the Editor folder.");
                }
            }

            throw new InvalidOperationException(
                "Cannot locate PhysicsFreeVerification.cs through the AssetDatabase.");
        }

        private static void VerifyProjectSources()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Cannot resolve the Unity project root.");

            // Package sources are resolved through Unity's virtual Packages/ path, so the
            // check keeps working whether the package is embedded, local or from a git URL.
            string packageRoot = Path.GetFullPath("Packages/com.datasakura.custom-navigation");
            var sourceRoots = new List<string>
            {
                ResolveClientRoot(),
                Path.Combine(packageRoot, "Authoring"),
                Path.Combine(packageRoot, "Runtime"),
                Path.Combine(packageRoot, "Editor")
            };

            // The standalone navigation server only exists in the source repository;
            // consumer projects install just the Unity package.
            string serverRoot = Path.Combine(projectRoot, "NavigationServer");
            if (Directory.Exists(serverRoot))
            {
                sourceRoots.Add(serverRoot);
            }

            foreach (string sourceRoot in sourceRoots)
            {
                if (!Directory.Exists(sourceRoot))
                {
                    throw new DirectoryNotFoundException(
                        $"Required navigation source root does not exist: {sourceRoot}");
                }

                foreach (string sourcePath in Directory.GetFiles(
                             sourceRoot,
                             "*.cs",
                             SearchOption.AllDirectories))
                {
                    // This file legitimately spells out the forbidden tokens in its own lists.
                    if (Path.GetFileName(sourcePath) == "PhysicsFreeVerification.cs")
                    {
                        continue;
                    }

                    string source = File.ReadAllText(sourcePath);
                    foreach (string forbiddenCall in ForbiddenRuntimeCalls)
                    {
                        if (source.Contains(forbiddenCall, StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                $"Forbidden navigation dependency '{forbiddenCall}' found in {sourcePath}.");
                        }
                    }
                }
            }
        }

        private static void VerifyDotRecastAssemblies()
        {
            VerifyAssemblyReferences(typeof(RcBuilder).Assembly);
            VerifyAssemblyReferences(typeof(DtNavMesh).Assembly);

            Assembly coreAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .First(assembly => assembly.GetName().Name == "DotRecast.Core");
            VerifyAssemblyReferences(coreAssembly);
        }

        private static void VerifyAssemblyReferences(Assembly assembly)
        {
            string unityReference = assembly.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .FirstOrDefault(name => name != null && name.StartsWith("UnityEngine", StringComparison.Ordinal));
            if (unityReference != null)
            {
                throw new InvalidOperationException(
                    $"{assembly.GetName().Name} unexpectedly references {unityReference}.");
            }
        }

        private static void VerifySerializedScene()
        {
            string[] scenePaths =
            {
                NavigationDemoHubSceneBuilder.ScenePath,
                DotRecastDemoSceneBuilder.ScenePath,
                DotRecastDemoSceneBuilder.ServerClientScenePath,
                "Assets/DataSakura/CustomNavigation/Scenes/DotRecastLocalBots.unity",
                "Assets/DataSakura/CustomNavigation/Scenes/DotRecastHybridPredicted.unity",
                MultiLevelDemoSceneBuilder.ScenePath
            };

            foreach (string scenePath in scenePaths)
            {
                if (!File.Exists(scenePath))
                {
                    throw new FileNotFoundException("A Custom Navigation scene has not been generated.", scenePath);
                }

                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null)
                        {
                            continue;
                        }

                        Type componentType = component.GetType();
                        string typeName = componentType.FullName ?? componentType.Name;
                        if (ForbiddenSceneComponents.Contains(typeName) || IsCollisionComponent(typeName))
                        {
                            throw new InvalidOperationException(
                                $"Forbidden scene component '{typeName}' found on {component.gameObject.name} in {scenePath}.");
                        }
                    }
                }
            }
        }

        private static bool IsCollisionComponent(string typeName)
        {
            return typeName.StartsWith("UnityEngine.", StringComparison.Ordinal)
                   && (typeName.EndsWith("Collider", StringComparison.Ordinal)
                       || typeName.EndsWith("Collider2D", StringComparison.Ordinal));
        }
    }
}
