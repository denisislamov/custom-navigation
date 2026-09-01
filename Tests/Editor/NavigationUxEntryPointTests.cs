using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomNavigation.Editor.Tests
{
    public sealed class NavigationUxEntryPointTests
    {
        private const string AssetMenuPrefix = "DataSakura/Custom Navigation/";
        private const string ComponentMenuPrefix = "DataSakura/Custom Navigation/";

        [Test]
        public void MainWindowUsesTheAgreedSectionsAndToolsEntryPoint()
        {
            Assert.That(
                NavigationEditorWindow.Tabs,
                Is.EqualTo(new[] { "Overview", "Geometry", "Bake", "Settings", "Diagnostics" }));

            MethodInfo open = typeof(NavigationEditorWindow).GetMethod(
                nameof(NavigationEditorWindow.Open),
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(open, Is.Not.Null);
            MenuItem menu = open.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>().Single();
            Assert.That(menu.menuItem, Is.EqualTo("Tools/DataSakura/Custom Navigation Window"));
            Assert.That(NavigationEditorWindow.RequiresSelectedLevel(3), Is.False,
                "Project defaults must be reachable from Settings without creating a level.");
            Assert.That(NavigationEditorWindow.RequiresSelectedLevel(4), Is.False);
            Assert.That(NavigationEditorWindow.RequiresSelectedLevel(2), Is.True);

            Assert.That(
                typeof(NavigationEditorWindow).GetMethod(
                    nameof(NavigationEditorWindow.OpenServerTab),
                    BindingFlags.Public | BindingFlags.Static),
                Is.Not.Null,
                "The existing automation entrypoint must remain public.");
            Assert.That(
                typeof(NavigationEditorWindow).GetMethod(
                    nameof(NavigationEditorWindow.OpenArtifactsTab),
                    BindingFlags.Public | BindingFlags.Static),
                Is.Not.Null,
                "The existing automation entrypoint must remain public.");
        }

        [Test]
        public void PackageRegistersOnlyTheCustomNavigationWindowInTheToolsMenu()
        {
            string[] toolsItems = typeof(NavigationEditorWindow).Assembly
                .GetTypes()
                .SelectMany(type => type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false))
                .Cast<MenuItem>()
                .Select(attribute => attribute.menuItem)
                .Where(path => path.StartsWith("Tools/", StringComparison.Ordinal))
                .Distinct()
                .ToArray();

            Assert.That(
                toolsItems,
                Is.EqualTo(new[] { "Tools/DataSakura/Custom Navigation Window" }));
        }

        [Test]
        public void ScenePreviewUsesANativeSceneViewOverlay()
        {
            OverlayAttribute overlay = typeof(NavigationSceneViewOverlay)
                .GetCustomAttribute<OverlayAttribute>();
            Assert.That(overlay, Is.Not.Null);
            Assert.That(overlay.editorWindowType, Is.EqualTo(typeof(SceneView)));
            Assert.That(overlay.id, Is.EqualTo(NavigationSceneViewOverlay.OverlayId));
            Assert.That(overlay.displayName, Is.EqualTo("Custom Navigation"));
        }

        [Test]
        public void OpeningAndClosingTheWindowDoesNotCreateAssetsOrDirtyTheScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Selection.activeObject = null;
            string[] assetsBefore = AssetDatabase.GetAllAssetPaths();

            NavigationEditorWindow.Open();
            NavigationEditorWindow window = EditorWindow.GetWindow<NavigationEditorWindow>();
            Assert.That(window.titleContent.text, Is.EqualTo(NavigationEditorWindow.WindowTitle));
            Assert.That(NavigationEditorWindow.WindowTitle, Is.EqualTo("DS Navigation"));
            window.Close();

            Assert.That(scene.isDirty, Is.False);
            Assert.That(AssetDatabase.GetAllAssetPaths(), Is.EqualTo(assetsBefore));
        }

        [TestCase(typeof(NavigationLevel))]
        [TestCase(typeof(NavigationGeometrySource))]
        [TestCase(typeof(NavigationModifierVolume))]
        [TestCase(typeof(NavigationTestPoint))]
        [TestCase(typeof(NavigationLink))]
        [TestCase(typeof(NavigationPortal))]
        [TestCase(typeof(NavigationQuerySchedulerBehaviour))]
        public void ComponentsUseTheDataSakuraGroup(Type type)
        {
            AddComponentMenu attribute = type.GetCustomAttribute<AddComponentMenu>();
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.componentMenu, Does.StartWith(ComponentMenuPrefix));
        }

        [TestCase(typeof(NavigationAgentProfile))]
        [TestCase(typeof(NavigationAreaCatalog))]
        [TestCase(typeof(NavigationPerformanceProfile))]
        [TestCase(typeof(NavigationServerSettings))]
        public void AuthoringAssetsUseTheDataSakuraGroup(Type type)
        {
            CreateAssetMenuAttribute attribute = type.GetCustomAttribute<CreateAssetMenuAttribute>();
            Assert.That(attribute, Is.Not.Null);
            Assert.That(attribute.menuName, Does.StartWith(AssetMenuPrefix));
        }

        [Test]
        public void GeneratedArtifactIsNotOfferedAsAHandCreatedConfig()
        {
            Assert.That(
                typeof(NavigationArtifactAsset).GetCustomAttribute<CreateAssetMenuAttribute>(),
                Is.Null);
        }

        [Test]
        public void LayoutMigrationIsAnExplicitWindowActionRatherThanAToolsItem()
        {
            MethodInfo[] methods = typeof(CustomNavigationLayoutMigration).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(
                methods.SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false)),
                Is.Empty);
        }

        [Test]
        public void DeleteClientArtifactRemovesOnlyItsGeneratedProjectFiles()
        {
            const string levelId = "ux-delete-test";
            const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            string folder = NavigationArtifactBuilder.GeneratedClientFolder;
            string payloadPath = $"{folder}/{levelId}.navigation.bytes";
            string manifestPath = $"{folder}/{levelId}.navigation.manifest.json";
            string artifactPath = $"{folder}/{levelId}.navigation.asset";

            try
            {
                EnsureAssetFolder(folder);
                File.WriteAllBytes(payloadPath, new byte[] { 1, 2, 3 });
                File.WriteAllText(manifestPath, "{}");
                AssetDatabase.ImportAsset(payloadPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceSynchronousImport);

                TextAsset payload = AssetDatabase.LoadAssetAtPath<TextAsset>(payloadPath);
                var artifact = ScriptableObject.CreateInstance<NavigationArtifactAsset>();
                artifact.Configure(levelId, hash, "1", "test",
                    NavigationCompatibilityContract.Precision,
                    NavigationCompatibilityContract.CanonicalJitterAssemblySha256,
                    NavigationCompatibilityContract.DeterministicMathCompatibilityId,
                    NavigationCompatibilityContract.FingerprintAlgorithmVersion,
                    NavigationCompatibilityContract.FingerprintAlgorithmId,
                    "agent", 1, 1, payload, "{}");
                AssetDatabase.CreateAsset(artifact, artifactPath);

                Assert.That(
                    NavigationArtifactBuilder.GetClientArtifactPaths(artifact),
                    Is.EquivalentTo(new[] { artifactPath, payloadPath, manifestPath }));

                NavigationArtifactBuilder.DeleteClientArtifact(artifact);

                Assert.That(AssetDatabase.LoadMainAssetAtPath(artifactPath), Is.Null);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(payloadPath), Is.Null);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(manifestPath), Is.Null);
            }
            finally
            {
                AssetDatabase.DeleteAsset(artifactPath);
                AssetDatabase.DeleteAsset(payloadPath);
                AssetDatabase.DeleteAsset(manifestPath);
            }
        }

        private static void EnsureAssetFolder(string folder)
        {
            string[] segments = folder.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }

                current = next;
            }
        }
    }
}
