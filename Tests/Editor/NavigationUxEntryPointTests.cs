using System;
using System.Linq;
using System.Reflection;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
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
            Assert.That(menu.menuItem, Is.EqualTo("Tools/DataSakura/Custom Navigation"));

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
        public void OpeningAndClosingTheWindowDoesNotCreateAssetsOrDirtyTheScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Selection.activeObject = null;
            string[] assetsBefore = AssetDatabase.GetAllAssetPaths();

            NavigationEditorWindow.Open();
            EditorWindow.GetWindow<NavigationEditorWindow>().Close();

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
    }
}
