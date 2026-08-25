using System;
using System.Collections.Generic;
using UnityEditor;

namespace CustomNavigation.Editor
{
    /// <summary>Result of an explicit project-folder migration.</summary>
    public sealed class CustomNavigationLayoutMigrationResult
    {
        /// <summary>Whether the project is already migrated or was migrated successfully.</summary>
        public bool Succeeded { get; }

        /// <summary>Human-readable actions or conflicts, in deterministic path order.</summary>
        public IReadOnlyList<string> Messages { get; }

        internal CustomNavigationLayoutMigrationResult(bool succeeded, IReadOnlyList<string> messages)
        {
            Succeeded = succeeded;
            Messages = messages ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Moves the pre-0.6.6 project root without changing Unity GUIDs. The migration is explicit,
    /// idempotent and refuses to merge two roots because a same-named generated asset can hide
    /// user-authored content or create an ambiguous bake.
    /// </summary>
    public static class CustomNavigationLayoutMigration
    {
        /// <summary>The project root used through package 0.6.5.</summary>
        public const string LegacyRoot = "Assets/CustomNavigation";

        /// <summary>The unified DataSakura product root.</summary>
        public const string CurrentRoot = "Assets/DataSakura/CustomNavigation";

        /// <summary>The builder scene folder used through package 0.6.5.</summary>
        public const string LegacyScenesFolderName = "Scene";

        /// <summary>The unified builder scene folder.</summary>
        public const string CurrentScenesFolderName = "Scenes";

        /// <summary>Preflights and moves the legacy root with <see cref="AssetDatabase.MoveAsset"/>.</summary>
        public static CustomNavigationLayoutMigrationResult Migrate()
        {
            return Migrate(LegacyRoot, CurrentRoot);
        }

        internal static CustomNavigationLayoutMigrationResult Migrate(
            string legacyRoot,
            string currentRoot)
        {
            var messages = new List<string>();
            bool hasLegacyRoot = AssetDatabase.IsValidFolder(legacyRoot);
            bool hasCurrentRoot = AssetDatabase.IsValidFolder(currentRoot);
            if (hasLegacyRoot && hasCurrentRoot)
            {
                messages.Add(
                    $"Conflict: both {legacyRoot} and {currentRoot} exist. Nothing was moved. "
                    + "Classify or archive one root, then run migration again.");
                return new CustomNavigationLayoutMigrationResult(false, messages);
            }

            string rootToInspect = hasLegacyRoot ? legacyRoot : currentRoot;
            string legacyScenes = rootToInspect + "/" + LegacyScenesFolderName;
            string currentScenes = rootToInspect + "/" + CurrentScenesFolderName;
            if (AssetDatabase.IsValidFolder(legacyScenes)
                && AssetDatabase.IsValidFolder(currentScenes))
            {
                messages.Add(
                    $"Conflict: both {legacyScenes} and {currentScenes} exist. Nothing was moved. "
                    + "Classify or archive one scene folder, then run migration again.");
                return new CustomNavigationLayoutMigrationResult(false, messages);
            }

            if (hasLegacyRoot)
            {
                EnsureFolder(ParentPath(currentRoot));
                string rootError = AssetDatabase.MoveAsset(legacyRoot, currentRoot);
                if (!string.IsNullOrEmpty(rootError))
                {
                    messages.Add("Unity refused the GUID-preserving folder move: " + rootError);
                    return new CustomNavigationLayoutMigrationResult(false, messages);
                }

                messages.Add($"Moved {legacyRoot} to {currentRoot}.");
            }

            string migratedLegacyScenes = currentRoot + "/" + LegacyScenesFolderName;
            string migratedCurrentScenes = currentRoot + "/" + CurrentScenesFolderName;
            if (AssetDatabase.IsValidFolder(migratedLegacyScenes))
            {
                string scenesError = AssetDatabase.MoveAsset(
                    migratedLegacyScenes,
                    migratedCurrentScenes);
                if (!string.IsNullOrEmpty(scenesError))
                {
                    messages.Add(
                        "The product root moved, but Unity refused the GUID-preserving scene-folder "
                        + "rename. Resolve the reported conflict and run migration again: "
                        + scenesError);
                    return new CustomNavigationLayoutMigrationResult(false, messages);
                }

                messages.Add($"Renamed {migratedLegacyScenes} to {migratedCurrentScenes}.");
            }

            if (!hasLegacyRoot && messages.Count == 0)
            {
                messages.Add($"No legacy {legacyRoot} layout was found; nothing to migrate.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return new CustomNavigationLayoutMigrationResult(true, messages);
        }

        [MenuItem("Tools/Custom Navigation/Migrate pre-0.6.6 project folders", priority = 190)]
        private static void MigrateFromMenu()
        {
            CustomNavigationLayoutMigrationResult result = Migrate();
            string report = string.Join("\n", result.Messages);
            if (result.Succeeded)
            {
                UnityEngine.Debug.Log("[CustomNavigation] " + report);
            }
            else
            {
                UnityEngine.Debug.LogError("[CustomNavigation] " + report);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = ParentPath(path);
            string name = path.Substring(path.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static string ParentPath(string path)
        {
            int separator = path.LastIndexOf('/');
            if (separator <= 0)
            {
                throw new ArgumentException("An AssetDatabase path must have a parent.", nameof(path));
            }

            return path.Substring(0, separator);
        }
    }
}
