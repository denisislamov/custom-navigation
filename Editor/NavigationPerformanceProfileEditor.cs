using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Presents the local scheduler profile without implying that reserved compatibility
    /// fields already implement caches, workers, memory enforcement, or telemetry.
    /// </summary>
    [CustomEditor(typeof(NavigationPerformanceProfile))]
    public sealed class NavigationPerformanceProfileEditor : UnityEditor.Editor
    {
        internal static readonly string[] ActiveSchedulerFields =
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

        internal static readonly string[] ActiveConsumerFields =
        {
            "combatBotMinimumReplanSeconds",
            "visibleBotMinimumReplanSeconds",
            "backgroundBotMinimumReplanSeconds",
            "budgetWarningMultiplier"
        };

        internal static readonly string[] ReservedFields =
        {
            "routeCacheEntries",
            "memoryBudgetMegabytes",
            "backgroundWorkerCount",
            "collectProductionMetrics"
        };

        private static readonly string[] PresetDrivenFields =
        {
            "frameBudgetMilliseconds",
            "maximumIterationsPerFrame",
            "maximumIterationsPerQueryStep",
            "maximumNewQueriesPerFrame",
            "maximumConcurrentSlicedQueries",
            "maximumQueuedQueries"
        };

        private static readonly string[] TierLabels =
        {
            "Mobile Low", "Mobile Medium", "Mobile High", "Custom"
        };

        private static readonly NavigationDeviceTier[] Tiers =
        {
            NavigationDeviceTier.MobileLow,
            NavigationDeviceTier.MobileMedium,
            NavigationDeviceTier.MobileHigh,
            NavigationDeviceTier.Custom
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var profile = (NavigationPerformanceProfile)target;

            DrawScopeNotice();
            DrawPresetSelector(profile);
            EditorGUILayout.Space(8f);
            DrawWorkingSummary(profile);
            EditorGUILayout.Space(8f);
            DrawAdvanced(profile);
            EditorGUILayout.Space(6f);
            DrawReservedFields();

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawScopeNotice()
        {
            EditorGUILayout.HelpBox(
                "Scope: this profile limits the local client scheduler. It does not change " +
                "navmesh geometry, is not written into the baked artifact, and is not a profile " +
                "for the dedicated navigation server.",
                MessageType.Info);
        }

        private void DrawPresetSelector(NavigationPerformanceProfile profile)
        {
            EditorGUILayout.LabelField("Local scheduler preset", EditorStyles.boldLabel);
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
                    "Custom keeps the current serialized values and unlocks working limits in Advanced.",
                    MessageType.None);
                return;
            }

            int differences = CountPresetDifferences(profile);
            if (differences == 0)
            {
                EditorGUILayout.LabelField(
                    $"Matches {TierLabels[currentIndex]}. Choose Custom for manual tuning.",
                    EditorStyles.miniLabel);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(
                    $"{differences} working value(s) differ from {TierLabels[currentIndex]}.",
                    MessageType.Warning);
                if (GUILayout.Button("Reset preset", GUILayout.Width(105f), GUILayout.Height(38f)))
                {
                    Undo.RecordObject(profile, "Reset Navigation Budget Preset");
                    profile.ApplyStartingPreset(profile.DeviceTier);
                    EditorUtility.SetDirty(profile);
                    serializedObject.Update();
                }
            }
        }

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
                    if (current != null && expected != null
                        && !SerializedProperty.DataEquals(current, expected))
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

        private static void DrawWorkingSummary(NavigationPerformanceProfile profile)
        {
            EditorGUILayout.LabelField("Working limits", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                Row("Frame work", $"{profile.FrameBudgetMilliseconds:0.##} ms, " +
                    $"{profile.MaximumIterationsPerFrame} iterations");
                Row("Admission", $"{profile.MaximumNewQueriesPerFrame}/frame, " +
                    $"{profile.MaximumConcurrentSlicedQueries} active");
                Row("Backlog", $"{profile.MaximumQueuedQueries} waiting requests");
                Row("Queue expiration", $"{profile.QueryDeadlineSeconds:0.##} s waiting only");
            }

            EditorGUILayout.HelpBox(
                "Queue expiration measures time before a request becomes active. It does not abort " +
                "an active sliced search. Queue-full rejection, priority eviction, cancellation, " +
                "and result-size limits are explained in Advanced.",
                MessageType.None);
        }

        private void DrawAdvanced(NavigationPerformanceProfile profile)
        {
            if (!NavigationInspectorGUI.AdvancedFoldout(
                    "PerformanceProfileWorking",
                    "Advanced · working details"))
            {
                return;
            }

            bool locked = profile.DeviceTier != NavigationDeviceTier.Custom;
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.LabelField("Scheduler limits (runtime reads)", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(locked))
                {
                    DrawFields(ActiveSchedulerFields);
                }

                if (locked)
                {
                    EditorGUILayout.LabelField(
                        "Preset values are read-only. Switch to Custom to edit without resetting them.",
                        EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(6f);
                DrawQueueSemantics();
                EditorGUILayout.Space(6f);

                EditorGUILayout.LabelField("Consumer pacing and warnings", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(
                    "Replan intervals are not scheduler limits: the bundled Local Bots sample reads " +
                    "them when deciding when to submit another request. Budget Warning Multiplier is " +
                    "read by NavigationQuerySchedulerBehaviour when warning logs are enabled.",
                    MessageType.None);
                DrawFields(ActiveConsumerFields);

                DrawBufferEstimate(profile);
            }
        }

        private static void DrawQueueSemantics()
        {
            EditorGUILayout.LabelField("Queue behavior", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Backlog counts waiting requests; active queries have a separate Concurrent limit. " +
                "When backlog is full, an incoming request is rejected unless its priority can evict " +
                "the worst queued request. Cancellation is observed on the next scheduler Tick. " +
                "Path Polygons and Straight Path Points cap result buffers; reaching a corridor cap " +
                "may produce a partial result rather than unlimited output.",
                MessageType.None);
        }

        private static void DrawBufferEstimate(NavigationPerformanceProfile profile)
        {
            long corridorBytes = (long)profile.MaximumConcurrentSlicedQueries
                                 * profile.MaximumPathPolygons * sizeof(long);
            long pointBytes = (long)profile.MaximumConcurrentSlicedQueries
                              * profile.MaximumStraightPathPoints * 32L;
            EditorGUILayout.LabelField(
                $"Allocated result buffers: about {(corridorBytes + pointBytes) / 1024f:0.#} KB " +
                "(excludes DotRecast query internals).",
                EditorStyles.miniLabel);
        }

        private void DrawReservedFields()
        {
            if (!NavigationInspectorGUI.AdvancedFoldout(
                    "PerformanceProfileReserved",
                    "Legacy / Diagnostics · reserved"))
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "Compatibility-only serialized values. Current package runtime, reference server, " +
                "bundled samples, and the audited EFT consumer do not enforce a route cache size, " +
                "memory target, worker count, or production telemetry flag. Values are preserved " +
                "when old profiles load and when presets are reapplied.",
                MessageType.Warning);

            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUI.DisabledScope(true))
            {
                DrawFields(ReservedFields);
            }
        }

        private void DrawFields(string[] fields)
        {
            for (int i = 0; i < fields.Length; i++)
            {
                NavigationFieldLabels.DrawProperty(serializedObject.FindProperty(fields[i]));
            }
        }

        private static void Row(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(135f));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            }
        }
    }
}
