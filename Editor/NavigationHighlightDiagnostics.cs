using System.Collections.Generic;
using System.Text;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Prints the full navigation highlight state for the current scene to the Console:
    /// the menu flag, the Scene View settings, the discovered authoring components and artifacts.
    /// </summary>
    internal static class NavigationHighlightDiagnostics
    {
        private static void Report()
        {
            var report = new StringBuilder();
            report.AppendLine("[CustomNavigation] Navigation highlight report");

            AppendToggleState(report);
            AppendSceneViewState(report);
            AppendSceneComponents(report);
            AppendArtifacts(report);

            Debug.Log(report.ToString());
        }

        private static void AppendToggleState(StringBuilder report)
        {
            bool storedValue = EditorPrefs.GetBool(
                NavigationHighlightSettings.EnabledPreferenceKey,
                NavigationHighlightSettings.DefaultEnabled);
            report.AppendLine(
                $"- Tools/Custom Navigation/Navigation Highlight: " +
                $"{(NavigationHighlightSettings.Enabled ? "ON" : "OFF")} " +
                $"(EditorPrefs '{NavigationHighlightSettings.EnabledPreferenceKey}'={storedValue})");
            if (!NavigationHighlightSettings.Enabled)
            {
                report.AppendLine(
                    "  FIX: enable Tools > Custom Navigation > Navigation Highlight.");
            }
        }

        private static void AppendSceneViewState(StringBuilder report)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
            {
                report.AppendLine("- Scene View: no active Scene View found.");
                return;
            }

            report.AppendLine(
                $"- Scene View gizmos: {(sceneView.drawGizmos ? "ON" : "OFF")}, " +
                $"2D mode: {sceneView.in2DMode}, camera: " +
                $"{(sceneView.camera != null ? sceneView.camera.transform.position.ToString("0.##") : "none")}");
            if (!sceneView.drawGizmos)
            {
                report.AppendLine(
                    "  FIX: turn on the Gizmos toggle in the Scene View toolbar - " +
                    "without it Unity never calls OnDrawGizmos and the navigation gizmos are not drawn.");
            }
        }

        private static void AppendSceneComponents(StringBuilder report)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            report.AppendLine($"- Active scene: '{activeScene.path}' (loaded={activeScene.isLoaded})");
            report.AppendLine($"  NavigationLevel: {Count<NavigationLevel>()}");
            report.AppendLine($"  NavigationGeometrySource: {Count<NavigationGeometrySource>()}");
            report.AppendLine($"  NavigationModifierVolume: {Count<NavigationModifierVolume>()}");
            report.AppendLine($"  NavigationLink: {Count<NavigationLink>()}");
            report.AppendLine($"  NavigationPortal: {Count<NavigationPortal>()}");
            report.AppendLine($"  NavigationTestPoint: {Count<NavigationTestPoint>()}");
            report.AppendLine(
                $"  NavigationQuerySchedulerBehaviour: {Count<NavigationQuerySchedulerBehaviour>()}");
        }

        private static void AppendArtifacts(StringBuilder report)
        {
            NavigationLevel[] levels = Object.FindObjectsByType<NavigationLevel>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < levels.Length; i++)
            {
                string levelId = levels[i].LevelId;
                string path = $"{NavigationArtifactBuilder.GeneratedClientFolder}/{levelId}.artifact.asset";
                bool exists = AssetDatabase.LoadAssetAtPath<NavigationArtifactAsset>(path) != null;
                report.AppendLine(
                    $"- Level '{levelId}' -> '{path}' {(exists ? "found" : "MISSING")}");
                if (!exists)
                {
                    report.AppendLine(
                        "  FIX: bake the navmesh via Navigation Editor > Build & Budgets.");
                }
            }

            IReadOnlyList<NavigationArtifactAsset> artifacts =
                NavigationHighlightOverlay.RefreshAndGetArtifacts();
            report.AppendLine($"- Overlay artifacts: {artifacts.Count}");
            if (artifacts.Count == 0)
            {
                report.AppendLine(
                    "  FIX: the overlay found no artifacts - check the Level ID " +
                    "or assign an artifact in NavigationQuerySchedulerBehaviour.");
                return;
            }

            for (int i = 0; i < artifacts.Count; i++)
            {
                NavigationArtifactAsset artifact = artifacts[i];
                bool ready = NavigationHighlightOverlay.TryDescribeOverlay(
                    artifact,
                    out int triangles,
                    out int edges,
                    out string error);
                report.AppendLine(
                    $"  '{artifact.name}' level={artifact.LevelId} polys={artifact.PolygonCount} " +
                    $"hash={artifact.ArtifactHash} -> triangles={triangles}, edges={edges}, " +
                    $"{(ready ? "ready to draw" : $"ERROR: {error ?? "the mesh is empty"}")}");
            }
        }

        private static int Count<T>() where T : Component
        {
            return Object.FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None).Length;
        }
    }
}
