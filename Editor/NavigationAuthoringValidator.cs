using System;
using System.Collections.Generic;
using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CustomNavigation.Editor
{
    internal enum NavigationValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>Issue groups so that a long list reads well instead of being scary.</summary>
    internal enum NavigationValidationCategory
    {
        Setup,
        Geometry,
        Identifiers,
        Budgets
    }

    internal readonly struct NavigationValidationIssue
    {
        public readonly NavigationValidationSeverity Severity;
        public readonly NavigationValidationCategory Category;
        public readonly string Message;
        public readonly Object Context;

        /// <summary>Auto-fix. Null when the issue cannot be fixed safely and unambiguously.</summary>
        public readonly Action Fix;

        public readonly string FixLabel;

        public NavigationValidationIssue(
            NavigationValidationSeverity severity,
            string message,
            Object context = null,
            NavigationValidationCategory category = NavigationValidationCategory.Setup,
            Action fix = null,
            string fixLabel = "Fix")
        {
            Severity = severity;
            Category = category;
            Message = message;
            Context = context;
            Fix = fix;
            FixLabel = fixLabel;
        }

        public bool CanFix => Fix != null;
    }

    /// <summary>
    /// Result of a single validation run together with its timestamp.
    /// Validation runs only on a button press or before Build/Export,
    /// so the window always shows a cached snapshot.
    /// </summary>
    internal sealed class NavigationValidationReport
    {
        public static readonly NavigationValidationReport NotEvaluated = new NavigationValidationReport();

        public readonly List<NavigationValidationIssue> Issues = new List<NavigationValidationIssue>();
        public bool Evaluated { get; private set; }
        public DateTime EvaluatedAt { get; private set; }
        public string LevelId { get; private set; } = string.Empty;

        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }
        public int InfoCount { get; private set; }

        public bool HasErrors => ErrorCount > 0;

        public static NavigationValidationReport Create(NavigationLevel level)
        {
            var report = new NavigationValidationReport
            {
                Evaluated = true,
                EvaluatedAt = DateTime.Now,
                LevelId = level != null ? level.LevelId : string.Empty
            };

            report.Issues.AddRange(NavigationAuthoringValidator.Validate(level));
            for (int i = 0; i < report.Issues.Count; i++)
            {
                switch (report.Issues[i].Severity)
                {
                    case NavigationValidationSeverity.Error:
                        report.ErrorCount++;
                        break;
                    case NavigationValidationSeverity.Warning:
                        report.WarningCount++;
                        break;
                    default:
                        report.InfoCount++;
                        break;
                }
            }

            return report;
        }

        public string DescribeFirstError()
        {
            for (int i = 0; i < Issues.Count; i++)
            {
                if (Issues[i].Severity == NavigationValidationSeverity.Error)
                {
                    return Issues[i].Message;
                }
            }

            return string.Empty;
        }
    }

    internal static class NavigationAuthoringValidator
    {
        public static List<NavigationValidationIssue> Validate(NavigationLevel level)
        {
            var issues = new List<NavigationValidationIssue>();
            if (level == null)
            {
                issues.Add(new NavigationValidationIssue(
                    NavigationValidationSeverity.Error,
                    "Navigation Level is not selected."));
                return issues;
            }

            ValidateRequiredAssets(level, issues);
            if (string.IsNullOrWhiteSpace(level.Description))
            {
                issues.Add(new NavigationValidationIssue(
                    NavigationValidationSeverity.Warning,
                    "Add a short level description so designers can identify its purpose in the catalog.",
                    level));
            }

            ValidateSources(level, issues);
            ValidateStableIds(level, issues);
            ValidatePerformance(level, issues);
            ValidateAreasAgainstCatalog(level, issues);
            return issues;
        }

        /// <summary>
        /// Catches a common mistake: the scene uses a surface type that is missing
        /// from the Area Catalog, so it falls back to the default color and flags.
        /// </summary>
        private static void ValidateAreasAgainstCatalog(
            NavigationLevel level,
            List<NavigationValidationIssue> issues)
        {
            NavigationAreaCatalog catalog = level.AreaCatalog;
            if (catalog == null)
            {
                return;
            }

            var reported = new HashSet<NavigationArea>();

            NavigationGeometrySource[] sources = level.GeometryRoot
                .GetComponentsInChildren<NavigationGeometrySource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i].Mode == NavigationGeometryMode.Include)
                {
                    ReportMissingArea(catalog, sources[i].Area, sources[i], reported, issues);
                }
            }

            NavigationModifierVolume[] volumes = level.GetComponentsInChildren<NavigationModifierVolume>(true);
            for (int i = 0; i < volumes.Length; i++)
            {
                if (volumes[i].Mode == NavigationGeometryMode.Include)
                {
                    ReportMissingArea(catalog, volumes[i].Area, volumes[i], reported, issues);
                }
            }

            NavigationLink[] links = level.GetComponentsInChildren<NavigationLink>(true);
            for (int i = 0; i < links.Length; i++)
            {
                ReportMissingArea(catalog, links[i].Area, links[i], reported, issues);
            }
        }

        private static void ReportMissingArea(
            NavigationAreaCatalog catalog,
            NavigationArea area,
            Object context,
            HashSet<NavigationArea> reported,
            List<NavigationValidationIssue> issues)
        {
            if (area == NavigationArea.NotWalkable
                || catalog.Find(area) != null
                || !reported.Add(area))
            {
                return;
            }

            issues.Add(new NavigationValidationIssue(
                NavigationValidationSeverity.Warning,
                $"Surface type '{area}' is used in the scene but is missing from the Area Catalog. " +
                "It will fall back to the default color and cost. Add it to the catalog.",
                context,
                NavigationValidationCategory.Setup,
                () => NavigationValidationFixes.AddAreaToCatalog(catalog, area),
                "Add to catalog"));
        }

        private static void ValidateRequiredAssets(
            NavigationLevel level,
            List<NavigationValidationIssue> issues)
        {
            if (level.DefaultAgentProfile == null)
            {
                issues.Add(new NavigationValidationIssue(
                    NavigationValidationSeverity.Error,
                    "Default Agent Profile is not assigned.",
                    level,
                    NavigationValidationCategory.Setup,
                    () => NavigationValidationFixes.CreateAndAssignAgentProfile(level),
                    "Create profile"));
            }

            if (level.AreaCatalog == null)
            {
                issues.Add(new NavigationValidationIssue(
                    NavigationValidationSeverity.Error,
                    "Area Catalog is not assigned.",
                    level,
                    NavigationValidationCategory.Setup,
                    () => NavigationValidationFixes.CreateAndAssignAreaCatalog(level),
                    "Create catalog"));
            }

            if (level.PerformanceProfile == null)
            {
                issues.Add(new NavigationValidationIssue(
                    NavigationValidationSeverity.Info,
                    "Mobile Performance Profile is not assigned - safe Mobile Medium values will be " +
                    "used instead. This is a programmer setting: it affects only the client " +
                    "request scheduler and affects neither the navmesh nor the server.",
                    level,
                    NavigationValidationCategory.Budgets,
                    () => NavigationValidationFixes.CreateAndAssignPerformanceProfile(level),
                    "Create profile"));
            }
        }

        private static void ValidateSources(
            NavigationLevel level,
            List<NavigationValidationIssue> issues)
        {
            NavigationGeometrySource[] sources = level.GeometryRoot
                .GetComponentsInChildren<NavigationGeometrySource>(true);
            if (sources.Length == 0)
            {
                issues.Add(new NavigationValidationIssue(
                    NavigationValidationSeverity.Error,
                    "No explicit geometry sources. Open Sources and scan MeshFilters.",
                    level,
                    NavigationValidationCategory.Geometry,
                    () => NavigationValidationFixes.AddMissingSources(level),
                    "Tag meshes"));
                return;
            }

            for (int i = 0; i < sources.Length; i++)
            {
                NavigationGeometrySource source = sources[i];
                MeshFilter[] meshes = source.IncludeChildren
                    ? source.GetComponentsInChildren<MeshFilter>(source.IncludeInactiveChildren)
                    : source.TryGetComponent(out MeshFilter meshFilter)
                        ? new[] { meshFilter }
                        : System.Array.Empty<MeshFilter>();

                bool hasMesh = false;
                for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
                {
                    MeshFilter mesh = meshes[meshIndex];
                    hasMesh |= mesh != null && mesh.sharedMesh != null;
                    if (mesh != null && mesh.sharedMesh != null && !mesh.sharedMesh.isReadable)
                    {
                        Mesh sharedMesh = mesh.sharedMesh;
                        issues.Add(new NavigationValidationIssue(
                            NavigationValidationSeverity.Error,
                            $"Mesh '{sharedMesh.name}' is not readable. Enable Read/Write " +
                            "for editor export; the generated navigation artifact does not need the source mesh at runtime.",
                            sharedMesh,
                            NavigationValidationCategory.Geometry,
                            NavigationValidationFixes.CanEnableReadWrite(sharedMesh)
                                ? () => NavigationValidationFixes.EnableReadWrite(sharedMesh)
                                : null,
                            "Enable Read/Write"));
                    }
                }

                if (!hasMesh && source.Mode != NavigationGeometryMode.Ignore)
                {
                    issues.Add(new NavigationValidationIssue(
                        NavigationValidationSeverity.Error,
                        "Geometry Source does not contain a readable MeshFilter.",
                        source,
                        NavigationValidationCategory.Geometry));
                }

            }
        }

        private static void ValidateStableIds(
            NavigationLevel level,
            List<NavigationValidationIssue> issues)
        {
            NavigationLevel[] loadedLevels = Object.FindObjectsByType<NavigationLevel>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < loadedLevels.Length; i++)
            {
                if (loadedLevels[i] != level
                    && string.Equals(loadedLevels[i].LevelId, level.LevelId, StringComparison.Ordinal))
                {
                    issues.Add(new NavigationValidationIssue(
                        NavigationValidationSeverity.Error,
                        $"Duplicate Navigation Level ID '{level.LevelId}'. Stable artifact filenames " +
                        "must not let two loaded levels overwrite each other.",
                        level,
                        NavigationValidationCategory.Identifiers));
                    break;
                }
            }

            ValidateIds(
                level.GetComponentsInChildren<NavigationLink>(true),
                value => value.LinkId,
                "link",
                "linkId",
                issues);
            ValidateIds(
                level.GetComponentsInChildren<NavigationPortal>(true),
                value => value.PortalId,
                "portal",
                "portalId",
                issues);
            ValidateIds(
                level.GetComponentsInChildren<NavigationTestPoint>(true),
                value => value.PointId,
                "test point",
                "pointId",
                issues);
        }

        private static void ValidateIds<T>(
            IReadOnlyList<T> values,
            System.Func<T, string> idSelector,
            string label,
            string idPropertyName,
            List<NavigationValidationIssue> issues)
            where T : Object
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < values.Count; i++)
            {
                T value = values[i];
                string id = idSelector(value);
                if (string.IsNullOrWhiteSpace(id))
                {
                    issues.Add(new NavigationValidationIssue(
                        NavigationValidationSeverity.Error,
                        $"A {label} has no stable ID.",
                        value,
                        NavigationValidationCategory.Identifiers,
                        () => NavigationValidationFixes.GenerateStableId(value, idPropertyName, label),
                        "Generate id"));
                }
                else if (!ids.Add(id))
                {
                    issues.Add(new NavigationValidationIssue(
                        NavigationValidationSeverity.Error,
                        $"Duplicate {label} ID '{id}'.",
                        value,
                        NavigationValidationCategory.Identifiers,
                        () => NavigationValidationFixes.GenerateStableId(value, idPropertyName, label),
                        "Regenerate id"));
                }
            }
        }

        private static void ValidatePerformance(
            NavigationLevel level,
            List<NavigationValidationIssue> issues)
        {
            NavigationPerformanceProfile profile = level.PerformanceProfile;
            if (profile == null)
            {
                return;
            }

            if (profile.FrameBudgetMilliseconds > 2f)
            {
                issues.Add(new NavigationValidationIssue(
                    NavigationValidationSeverity.Warning,
                    $"Navigation frame budget is {profile.FrameBudgetMilliseconds:0.##} ms. " +
                    "Measure this profile on the slowest supported mobile device.",
                    profile,
                    NavigationValidationCategory.Budgets));
            }

            if (profile.MaximumIterationsPerQueryStep > profile.MaximumIterationsPerFrame)
            {
                issues.Add(new NavigationValidationIssue(
                    NavigationValidationSeverity.Error,
                    "Query step iterations exceed the whole-frame iteration budget.",
                    profile,
                    NavigationValidationCategory.Budgets,
                    () => NavigationValidationFixes.ClampQueryStepIterations(profile),
                    "Clamp the step"));
            }
        }

        private static void AddError(
            List<NavigationValidationIssue> issues,
            string message,
            Object context)
        {
            issues.Add(new NavigationValidationIssue(
                NavigationValidationSeverity.Error,
                message,
                context));
        }
    }

    /// <summary>
    /// Safe auto-fixes for common validation errors. Every edit goes
    /// through Undo so the designer can revert it with a normal Ctrl+Z.
    /// </summary>
    internal static class NavigationValidationFixes
    {
        private const string GeneratedSettingsFolder = "Assets/DataSakura/CustomNavigation/Generated/Settings";

        public static void CreateAndAssignAgentProfile(NavigationLevel level)
        {
            var profile = CreateAsset<NavigationAgentProfile>(level, "Agent");
            AssignLevelReference(level, "defaultAgentProfile", profile);
        }

        public static void CreateAndAssignAreaCatalog(NavigationLevel level)
        {
            var catalog = CreateAsset<NavigationAreaCatalog>(level, "Areas");
            catalog.ResetToDefaults();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssignLevelReference(level, "areaCatalog", catalog);
        }

        public static void CreateAndAssignPerformanceProfile(NavigationLevel level)
        {
            var profile = CreateAsset<NavigationPerformanceProfile>(level, "MobilePerformance");
            profile.ApplyStartingPreset(NavigationDeviceTier.MobileMedium);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssignLevelReference(level, "performanceProfile", profile);
        }

        public static void AddMissingSources(NavigationLevel level)
        {
            MeshFilter[] meshFilters = level.GeometryRoot.GetComponentsInChildren<MeshFilter>(true);
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Navigation Geometry Sources");
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter mesh = meshFilters[i];
                if (mesh.sharedMesh == null || mesh.TryGetComponent(out NavigationGeometrySource _))
                {
                    continue;
                }

                NavigationGeometrySource source = Undo.AddComponent<NavigationGeometrySource>(mesh.gameObject);
                var sourceObject = new SerializedObject(source);
                sourceObject.Update();
                sourceObject.FindProperty("area").intValue = (int)NavigationArea.Ground;
                sourceObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(source);
            }

            Undo.CollapseUndoOperations(group);
        }

        public static bool CanEnableReadWrite(Mesh mesh)
        {
            string path = AssetDatabase.GetAssetPath(mesh);
            return !string.IsNullOrEmpty(path)
                   && AssetImporter.GetAtPath(path) is ModelImporter;
        }

        public static void EnableReadWrite(Mesh mesh)
        {
            string path = AssetDatabase.GetAssetPath(mesh);
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            {
                return;
            }

            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        public static void AddAreaToCatalog(NavigationAreaCatalog catalog, NavigationArea area)
        {
            if (catalog == null)
            {
                return;
            }

            var catalogObject = new SerializedObject(catalog);
            catalogObject.Update();
            SerializedProperty areas = catalogObject.FindProperty("areas");
            areas.arraySize++;
            SerializedProperty added = areas.GetArrayElementAtIndex(areas.arraySize - 1);
            added.FindPropertyRelative("area").intValue = (int)area;
            added.FindPropertyRelative("name").stringValue = area.ToString();
            added.FindPropertyRelative("color").colorValue = Color.HSVToRGB(
                Mathf.Repeat((int)area * 0.13f, 1f),
                0.65f,
                0.9f);
            added.FindPropertyRelative("defaultCost").floatValue = 1f;
            catalogObject.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        public static void GenerateStableId(Object target, string propertyName, string label)
        {
            var targetObject = new SerializedObject(target);
            targetObject.Update();
            SerializedProperty property = targetObject.FindProperty(propertyName);
            if (property == null)
            {
                return;
            }

            string seed = target.name + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            property.stringValue = NavigationIdUtility.Sanitize(seed, label);
            targetObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        public static void ClampQueryStepIterations(NavigationPerformanceProfile profile)
        {
            var profileObject = new SerializedObject(profile);
            profileObject.Update();
            SerializedProperty perFrame = profileObject.FindProperty("maximumIterationsPerFrame");
            SerializedProperty perStep = profileObject.FindProperty("maximumIterationsPerQueryStep");
            if (perFrame == null || perStep == null)
            {
                return;
            }

            perStep.intValue = Mathf.Max(1, perFrame.intValue);
            profileObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private static T CreateAsset<T>(NavigationLevel level, string suffix) where T : ScriptableObject
        {
            EnsureFolder(GeneratedSettingsFolder);
            string key = NavigationIdUtility.Sanitize(
                level != null ? level.LevelId : "level",
                "level");
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{GeneratedSettingsFolder}/{key}_{suffix}.asset");
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static void AssignLevelReference(NavigationLevel level, string propertyName, Object value)
        {
            var levelObject = new SerializedObject(level);
            levelObject.Update();
            levelObject.FindProperty(propertyName).objectReferenceValue = value;
            levelObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(level);
        }

        private static void EnsureFolder(string folderPath)
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
    }
}
