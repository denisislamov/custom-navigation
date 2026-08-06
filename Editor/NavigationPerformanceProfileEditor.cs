using System.Collections.Generic;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Inspector for the runtime budgets.
    ///
    /// Honestly separates the fields that really constrain the scheduler from
    /// the ones that are still reserved (route cache, workers, metrics, replan
    /// intervals) - their values are read by nobody right now.
    /// </summary>
    [CustomEditor(typeof(NavigationPerformanceProfile))]
    public sealed class NavigationPerformanceProfileEditor : UnityEditor.Editor
    {
        /// <summary>Fields that NavigationQueryScheduler actually reads.</summary>
        private static readonly string[] ActiveBudgetFields =
        {
            "frameBudgetMilliseconds",
            "maximumIterationsPerFrame",
            "maximumIterationsPerQueryStep",
            "maximumNewQueriesPerFrame",
            "maximumConcurrentSlicedQueries",
            "maximumQueuedQueries",
            "maximumPathPolygons",
            "maximumStraightPathPoints",
            "queryDeadlineSeconds"
        };

        /// <summary>Fields declared and covered by presets, but read nowhere.</summary>
        private static readonly string[] ReservedFields =
        {
            "routeCacheEntries",
            "memoryBudgetMegabytes",
            "backgroundWorkerCount",
            "combatBotMinimumReplanSeconds",
            "visibleBotMinimumReplanSeconds",
            "backgroundBotMinimumReplanSeconds",
            "collectProductionMetrics"
        };

        /// <summary>Fields the preset sets explicitly - deviations are measured against them.</summary>
        private static readonly string[] PresetDrivenFields =
        {
            "frameBudgetMilliseconds",
            "maximumIterationsPerFrame",
            "maximumIterationsPerQueryStep",
            "maximumNewQueriesPerFrame",
            "maximumConcurrentSlicedQueries",
            "maximumQueuedQueries"
        };

        private static readonly string[] TierLabels = { "Mobile Low", "Mobile Medium", "Mobile High", "Custom" };

        private static readonly NavigationDeviceTier[] Tiers =
        {
            NavigationDeviceTier.MobileLow,
            NavigationDeviceTier.MobileMedium,
            NavigationDeviceTier.MobileHigh,
            NavigationDeviceTier.Custom
        };

        [SerializeField] private int estimateAgentCount = 24;
        [SerializeField] private int estimateTargetFps = 60;
        private string estimateResult = string.Empty;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var profile = (NavigationPerformanceProfile)target;

            DrawScopeNotice();
            DrawPresetSelector(profile);
            EditorGUILayout.Space(8f);
            DrawActiveBudgets(profile);
            EditorGUILayout.Space(8f);
            DrawEstimate(profile);
            EditorGUILayout.Space(8f);
            DrawReservedFields();

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawScopeNotice()
        {
            EditorGUILayout.HelpBox(
                "What it affects: only the client path request scheduler.\\n" +
                "What it does NOT affect: the navmesh shape (that is Build Settings) and the navigation server " +
                "(these values never reach the manifest).",
                MessageType.None);
        }

        // -- Presets -----------------------------------------------------------
        private void DrawPresetSelector(NavigationPerformanceProfile profile)
        {
            EditorGUILayout.LabelField("Device class", EditorStyles.boldLabel);

            int currentIndex = System.Array.IndexOf(Tiers, profile.DeviceTier);
            if (currentIndex < 0)
            {
                currentIndex = 1;
            }

            int nextIndex = GUILayout.Toolbar(currentIndex, TierLabels);
            if (nextIndex != currentIndex)
            {
                Undo.RecordObject(profile, "Change Navigation Device Tier");
                profile.ApplyStartingPreset(Tiers[nextIndex]);
                EditorUtility.SetDirty(profile);
                serializedObject.Update();
            }

            if (profile.DeviceTier == NavigationDeviceTier.Custom)
            {
                EditorGUILayout.HelpBox(
                    "Custom: every field is edited by hand. The values must be verified " +
                    "on the slowest supported device.",
                    MessageType.Info);
                return;
            }

            int differences = CountPresetDifferences(profile);
            if (differences == 0)
            {
                EditorGUILayout.HelpBox(
                    $"The values match the {TierLabels[nextIndex]} preset. " +
                    "The fields are locked - switch to Custom to edit them by hand.",
                    MessageType.None);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(
                    $"{TierLabels[nextIndex]} (changed fields: {differences}). " +
                    "The values differ from the preset.",
                    MessageType.Warning);
                if (GUILayout.Button("Reset to preset", GUILayout.Width(120f), GUILayout.Height(38f)))
                {
                    Undo.RecordObject(profile, "Reset Navigation Budget Preset");
                    profile.ApplyStartingPreset(profile.DeviceTier);
                    EditorUtility.SetDirty(profile);
                    serializedObject.Update();
                }
            }
        }

        /// <summary>
        /// Compares the current values against a reference preset. Uses a temporary
        /// object so the value table is not duplicated in the editor.
        /// </summary>
        private int CountPresetDifferences(NavigationPerformanceProfile profile)
        {
            var reference = CreateInstance<NavigationPerformanceProfile>();
            try
            {
                reference.ApplyStartingPreset(profile.DeviceTier);
                var referenceObject = new SerializedObject(reference);
                int differences = 0;
                for (int i = 0; i < PresetDrivenFields.Length; i++)
                {
                    SerializedProperty current = serializedObject.FindProperty(PresetDrivenFields[i]);
                    SerializedProperty expected = referenceObject.FindProperty(PresetDrivenFields[i]);
                    if (current == null || expected == null)
                    {
                        continue;
                    }

                    if (!SerializedProperty.DataEquals(current, expected))
                    {
                        differences++;
                    }
                }

                return differences;
            }
            finally
            {
                DestroyImmediate(reference);
            }
        }

        // -- Active budgets ----------------------------------------------------
        private void DrawActiveBudgets(NavigationPerformanceProfile profile)
        {
            EditorGUILayout.LabelField("Active budgets", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "These nine fields are read by NavigationQueryScheduler every frame.",
                EditorStyles.miniLabel);

            bool locked = profile.DeviceTier != NavigationDeviceTier.Custom;
            using (new EditorGUI.DisabledScope(locked))
            {
                for (int i = 0; i < ActiveBudgetFields.Length; i++)
                {
                    NavigationFieldLabels.DrawProperty(serializedObject.FindProperty(ActiveBudgetFields[i]));
                }
            }

            DrawBudgetExplanation(profile);

            if (NavigationInspectorGUI.AdvancedFoldout("PerformanceProfile", "Advanced"))
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    NavigationFieldLabels.DrawProperties(serializedObject, "budgetWarningMultiplier");
                }
            }
        }

        private void DrawBudgetExplanation(NavigationPerformanceProfile profile)
        {
            float frameMs = 1000f / Mathf.Max(1, estimateTargetFps);
            float share = profile.FrameBudgetMilliseconds / frameMs * 100f;

            // Rough pool memory estimate: the long corridor plus the path points.
            long corridorBytes = (long)profile.MaximumConcurrentSlicedQueries * profile.MaximumPathPolygons * 8;
            long pointBytes = (long)profile.MaximumConcurrentSlicedQueries * profile.MaximumStraightPathPoints * 32;
            float poolKilobytes = (corridorBytes + pointBytes) / 1024f;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("What this means in practice", EditorStyles.miniBoldLabel);
                Row(
                    "Share of the frame budget",
                    $"{profile.FrameBudgetMilliseconds:0.##} ms out of {frameMs:0.#} ms, about {share:0.#} % at {estimateTargetFps} FPS");
                Row(
                    "Request pool memory",
                    $"{profile.MaximumConcurrentSlicedQueries} x ({profile.MaximumPathPolygons} polygons + " +
                    $"{profile.MaximumStraightPathPoints} points), about {poolKilobytes:0.#} KB");
                Row(
                    "Throughput",
                    $"{profile.MaximumNewQueriesPerFrame} new requests per frame");
            }
        }

        // -- Load estimate -----------------------------------------------------
        private void DrawEstimate(NavigationPerformanceProfile profile)
        {
            EditorGUILayout.LabelField("Scenario estimate", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                estimateAgentCount = EditorGUILayout.IntSlider(
                    new GUIContent("Simultaneous agents", "How many agents change their destination at once."),
                    Mathf.Max(1, estimateAgentCount),
                    1,
                    256);
                estimateTargetFps = EditorGUILayout.IntPopup(
                    "Target FPS",
                    estimateTargetFps <= 0 ? 60 : estimateTargetFps,
                    new[] { "30", "60", "120" },
                    new[] { 30, 60, 120 });

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Estimate", GUILayout.Height(24f)))
                    {
                        estimateResult = BuildEstimate(profile);
                    }

                    if (GUILayout.Button("Take from scene", EditorStyles.miniButton, GUILayout.Width(120f)))
                    {
                        estimateAgentCount = Mathf.Max(1, CountSceneAgents());
                        estimateResult = BuildEstimate(profile);
                    }
                }

                if (!string.IsNullOrEmpty(estimateResult))
                {
                    EditorGUILayout.HelpBox(estimateResult, MessageType.None);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "Press Estimate - it runs once against the current values.",
                        EditorStyles.miniLabel);
                }
            }
        }

        private string BuildEstimate(NavigationPerformanceProfile profile)
        {
            int frames = Mathf.CeilToInt(
                (float)estimateAgentCount / Mathf.Max(1, profile.MaximumNewQueriesPerFrame));
            float seconds = frames / (float)Mathf.Max(1, estimateTargetFps);
            int overflow = Mathf.Max(0, estimateAgentCount - profile.MaximumQueuedQueries);

            var builder = new List<string>
            {
                $"{estimateAgentCount} agents / {profile.MaximumNewQueriesPerFrame} starts per frame " +
                $"= {frames} frames, about {seconds * 1000f:0} ms for a full replanning cycle.",
                $"Queue: {profile.MaximumQueuedQueries} slots."
            };

            if (overflow > 0)
            {
                builder.Add(
                    $"Warning: {overflow} requests will not fit the queue and will be evicted. " +
                    "Increase the queue length or spread replanning over time.");
            }

            if (seconds > profile.QueryDeadlineSeconds)
            {
                builder.Add(
                    $"Warning: the full cycle ({seconds:0.##} s) is longer than the request lifetime " +
                    $"({profile.QueryDeadlineSeconds:0.##} s) - some requests will go stale in the queue.");
            }

            return string.Join("\n", builder);
        }

        /// <summary>
        /// Counts scene bot agents WITHOUT a hard reference to the client assembly.
        /// The package must not depend on gameplay code, so the bot type is resolved
        /// by name via reflection - returns 0 when the client assembly is absent.
        /// </summary>
        private static int CountSceneAgents()
        {
            var botType = System.Type.GetType(
                "CustomNavigation.Runtime.NavigationBotAgent, CustomNavigation.Client");
            if (botType == null)
            {
                return 0;
            }

            UnityEngine.Object[] agents = FindObjectsByType(
                botType,
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            return agents.Length;
        }

        // -- Reserved fields ---------------------------------------------------
        private void DrawReservedFields()
        {
            if (!NavigationInspectorGUI.AdvancedFoldout(
                    "PerformanceProfileReserved",
                    "Reserved (no effect yet)"))
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "These fields are stored in the asset and covered by presets, but nobody " +
                "reads them: route cache and background workers are not implemented, and neither " +
                "are the replan intervals or the telemetry. Editing them is safe but pointless.",
                MessageType.Warning);

            using (new EditorGUI.IndentLevelScope())
            {
                for (int i = 0; i < ReservedFields.Length; i++)
                {
                    NavigationFieldLabels.DrawProperty(serializedObject.FindProperty(ReservedFields[i]));
                }
            }
        }

        private static void Row(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.Width(170f));
                EditorGUILayout.LabelField(value, EditorStyles.miniBoldLabel);
            }
        }
    }
}

