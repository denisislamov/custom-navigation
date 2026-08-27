using System;
using System.Collections.Generic;
using System.IO;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CustomNavigation.Editor
{
    public sealed class NavigationEditorWindow : EditorWindow
    {
        internal static readonly string[] Tabs =
        {
            "Overview",
            "Geometry",
            "Bake",
            "Settings",
            "Diagnostics"
        };

        private const int OverviewTab = 0;
        private const int GeometryTab = 1;
        private const int BakeTab = 2;
        private const int SettingsTab = 3;
        private const int DiagnosticsTab = 4;

        private const string GeneratedSettingsFolder = "Assets/DataSakura/CustomNavigation/Generated/Settings";

        [SerializeField] private NavigationLevel selectedLevel;
        [SerializeField] private int selectedTab;
        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private bool showSourceDetails = true;
        [SerializeField] private string lastBuildMessage;
        [SerializeField] private string lastExportMessage;
        [SerializeField] private string serverAddressInput = string.Empty;
        [SerializeField] private string serverStatusMessage = string.Empty;
        [SerializeField] private MessageType serverStatusType = MessageType.None;
        [SerializeField] private string artifactsStatusMessage = string.Empty;
        [SerializeField] private MessageType artifactsStatusType = MessageType.None;
        [SerializeField] private float clearanceThreshold;
        [SerializeField] private int probeSourceMode;
        [SerializeField] private bool probeRequestPending;
        [SerializeField] private string sourceSearch = string.Empty;
        [SerializeField] private int sourceModeFilter;
        [SerializeField] private NavigationGeometryMode batchMode = NavigationGeometryMode.Include;
        [SerializeField] private NavigationArea batchArea = NavigationArea.Ground;
        [SerializeField] private string exportedManifestPath = string.Empty;
        [SerializeField] private string exportedHash = string.Empty;
        [SerializeField] private string serverVerdictMessage = string.Empty;
        [SerializeField] private MessageType serverVerdictType = MessageType.None;

        // Not serialized: a domain reload kills the in-flight request together with its callback,
        // so a persisted true would block the buttons forever.
        private bool serverRequestPending;

        /// <summary>
        /// Snapshot of the last validation. It is never recomputed on its own:
        /// only from the Validate button and inside Build/Export.
        /// </summary>
        private NavigationValidationReport validationReport = NavigationValidationReport.NotEvaluated;

        /// <summary>The level data changed after the last check. Nothing is recomputed.</summary>
        private bool validationStale;

        /// <summary>
        /// Heavy actions (Build, Export, analysis, Fix) run AFTER the GUI is drawn.
        /// Otherwise the modal progress bar and the changing issue list break the layout groups.
        /// </summary>
        private Action pendingAction;

        private List<NavigationArtifactComparison> artifactComparisons;

        internal const string MainMenuPath = "Tools/DataSakura/Custom Navigation Window";
        internal const string WindowTitle = "DS Navigation";

        [MenuItem(MainMenuPath, priority = 100)]
        public static void Open()
        {
            var window = GetWindow<NavigationEditorWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(460f, 420f);
            window.Show();
        }

        public static void OpenServerTab()
        {
            Open();
            GetWindow<NavigationEditorWindow>().selectedTab = SettingsTab;
        }

        public static void OpenArtifactsTab()
        {
            Open();
            var window = GetWindow<NavigationEditorWindow>();
            window.selectedTab = DiagnosticsTab;
            window.RefreshArtifacts();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            Selection.selectionChanged += OnSelectionChanged;
            serverRequestPending = false;
            probeRequestPending = false;
            selectedTab = Mathf.Clamp(selectedTab, 0, Tabs.Length - 1);
            TrySelectLevelFromContext();
            // Validation does NOT run automatically: an honest "not evaluated"
            // state is shown until the user presses Validate.
            validationReport = NavigationValidationReport.NotEvaluated;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawStatusBar();

            selectedTab = GUILayout.Toolbar(selectedTab, Tabs);
            EditorGUILayout.Space(6f);

            bool levelRequired = RequiresSelectedLevel(selectedTab);
            if (levelRequired)
            {
                DrawLevelSelector();
                if (selectedLevel == null)
                {
                    DrawEmptyState();
                    return;
                }
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            switch (selectedTab)
            {
                case GeometryTab:
                    DrawSources();
                    break;
                case BakeTab:
                    DrawBuildStatus();
                    break;
                case SettingsTab:
                    DrawSettings();
                    break;
                case DiagnosticsTab:
                    DrawDiagnostics();
                    break;
                default:
                    DrawOverview();
                    break;
            }

            EditorGUILayout.EndScrollView();
            RunPendingAction();
        }

        internal static bool RequiresSelectedLevel(int tabIndex)
        {
            return tabIndex != DiagnosticsTab && tabIndex != SettingsTab;
        }

        private void DrawSettings()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Project Defaults", EditorStyles.miniButton))
                {
                    SettingsService.OpenProjectSettings(NavigationProjectSettings.ProjectProviderPath);
                }

                if (GUILayout.Button("Open Scene Preview Preferences", EditorStyles.miniButton))
                {
                    SettingsService.OpenUserPreferences(NavigationProjectSettings.PreferencesProviderPath);
                }
            }

            EditorGUILayout.Space(8f);
            DrawLevelSelector();
            if (selectedLevel == null)
            {
                EditorGUILayout.HelpBox(
                    "Project Defaults and Scene Preview Preferences are available without a " +
                    "Navigation Level. Select a level to edit its profiles and local Bake Quality.",
                    MessageType.Info);
                return;
            }

            DrawLevelSettings();
            EditorGUILayout.Space(8f);
            DrawAssetReferences();
            EditorGUILayout.Space(10f);
            DrawBuildAndBudgets();
            EditorGUILayout.Space(12f);
            DrawServerSettings();
        }

        private void DrawDiagnostics()
        {
            DrawLevelSelector();
            if (selectedLevel != null)
            {
                DrawTools();
                EditorGUILayout.Space(12f);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Select a Navigation Level to use path probes and navmesh analysis.",
                    MessageType.None);
            }

            DrawArtifacts();
            EditorGUILayout.Space(12f);
            DrawLayoutMigration();
        }

        private static void DrawLayoutMigration()
        {
            EditorGUILayout.LabelField("Project layout migration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Preview the pre-0.6.6 folder migration before running it. The command is explicit, " +
                "idempotent, and uses AssetDatabase.MoveAsset to preserve GUIDs and references.",
                MessageType.None);

            if (!GUILayout.Button("Preview / Run pre-0.6.6 Migration", GUILayout.Height(26f)))
            {
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog(
                "Custom Navigation layout migration",
                "This will look for the legacy Assets/CustomNavigation layout and move it to " +
                "Assets/DataSakura/CustomNavigation when there is no conflict. Nothing is changed " +
                "when the legacy layout is absent. Continue?",
                "Run Migration",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            CustomNavigationLayoutMigrationResult result = CustomNavigationLayoutMigration.Migrate();
            string report = string.Join("\n", result.Messages);
            if (result.Succeeded)
            {
                Debug.Log("[CustomNavigation] " + report);
            }
            else
            {
                Debug.LogError("[CustomNavigation] " + report);
            }
        }

        /// <summary>
        /// Runs the deferred action outside the layout groups: the modal progress bar
        /// and rebuilding lists inside OnGUI would break IMGUI.
        /// </summary>
        private void RunPendingAction()
        {
            if (pendingAction == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Action action = pendingAction;
            pendingAction = null;
            action();
            Repaint();
        }

        private static void DrawHeader()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Custom Navigation Authoring", EditorStyles.largeLabel);
            EditorGUILayout.LabelField(
                "Physics-free geometry authoring for local and server DotRecast navigation.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
        }

        /// <summary>
        /// The level readiness status light. It shows only the cached
        /// validation snapshot: nothing is recomputed while drawing the window.
        /// </summary>
        private void DrawStatusBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUIContent status = DescribeValidationStatus();
                var style = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
                EditorGUILayout.LabelField(status, style);

                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(selectedLevel == null))
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                "Validate",
                                "A one-shot level check. Nothing runs in the background."),
                            GUILayout.Width(80f),
                            GUILayout.Height(22f)))
                    {
                        RunValidation();
                    }
                }
            }
        }

        private GUIContent DescribeValidationStatus()
        {
            if (selectedLevel == null)
            {
                return new GUIContent("[ ]  No level selected");
            }

            if (!validationReport.Evaluated)
            {
                return new GUIContent(
                    "[ ]  Not validated - press Validate",
                    "Validation runs manually, and automatically before Build/Export.");
            }

            string time = validationReport.EvaluatedAt.ToString("HH:mm:ss");
            string stale = validationStale ? "   (data changed)" : string.Empty;
            if (validationReport.ErrorCount > 0)
            {
                return new GUIContent(
                    $"[X]  {validationReport.ErrorCount} errors - export blocked   ·   checked at {time}{stale}",
                    validationReport.DescribeFirstError());
            }

            if (validationReport.WarningCount > 0)
            {
                return new GUIContent(
                    $"[!]  {validationReport.WarningCount} warnings   ·   checked at {time}{stale}");
            }

            return new GUIContent($"[v]  Ready to export   ·   checked at {time}{stale}");
        }

        private void RunValidation()
        {
            validationReport = NavigationValidationReport.Create(selectedLevel);
            validationStale = false;
            Repaint();
        }

        private void DrawLevelSelector()
        {
            EditorGUI.BeginChangeCheck();
            NavigationLevel nextLevel = (NavigationLevel)EditorGUILayout.ObjectField(
                new GUIContent(
                    "Navigation Level",
                    "The NavigationLevel of the current scene edited in this window."),
                selectedLevel,
                typeof(NavigationLevel),
                true);
            if (EditorGUI.EndChangeCheck())
            {
                selectedLevel = nextLevel;
                MarkValidationStale();
                Repaint();
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.HelpBox(
                "This scene has no selected Navigation Level. Create one to define explicit " +
                "mesh sources, agent settings and mobile query budgets.",
                MessageType.Info);

            if (GUILayout.Button("Create Navigation Level Setup", GUILayout.Height(34f)))
            {
                CreateLevelSetup();
            }

            NavigationLevel[] sceneLevels = FindSceneLevels();
            if (sceneLevels.Length > 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Existing levels", EditorStyles.boldLabel);
                for (int i = 0; i < sceneLevels.Length; i++)
                {
                    if (GUILayout.Button(sceneLevels[i].name, EditorStyles.miniButton))
                    {
                        selectedLevel = sceneLevels[i];
                        Selection.activeObject = selectedLevel.gameObject;
                        MarkValidationStale();
                    }
                }
            }
        }

        private void DrawOverview()
        {
            NavigationGeometrySource[] sources = selectedLevel.GeometryRoot
                .GetComponentsInChildren<NavigationGeometrySource>(true);
            NavigationLink[] links = selectedLevel.GetComponentsInChildren<NavigationLink>(true);
            NavigationPortal[] portals = selectedLevel.GetComponentsInChildren<NavigationPortal>(true);
            NavigationTestPoint[] tests = selectedLevel.GetComponentsInChildren<NavigationTestPoint>(true);

            EditorGUILayout.LabelField("Level summary", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("Level ID", selectedLevel.LevelId);
                DrawSummaryRow(
                    "Description",
                    string.IsNullOrWhiteSpace(selectedLevel.Description)
                        ? "Not set"
                        : selectedLevel.Description);
                DrawSummaryRow("Geometry sources", sources.Length.ToString());
                DrawSummaryRow("Links / portals", $"{links.Length} / {portals.Length}");
                DrawSummaryRow("Test points", tests.Length.ToString());
                DrawSummaryRow("Device tier", selectedLevel.PerformanceProfile != null
                    ? selectedLevel.PerformanceProfile.DeviceTier.ToString()
                    : "Not assigned");
            }

            EditorGUILayout.Space(8f);
            DrawValidation();
        }

        private void DrawLevelSettings()
        {
            EditorGUILayout.LabelField("Level setup", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "DotRecast derives stacked surfaces and vertical connectivity directly from " +
                "world-space geometry, agent Maximum Slope and Maximum Climb. Manual floor " +
                "definitions are not required.",
                MessageType.Info);

            var levelObject = new SerializedObject(selectedLevel);
            levelObject.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(levelObject.FindProperty("levelId"));
            EditorGUILayout.PropertyField(levelObject.FindProperty("description"));
            EditorGUILayout.PropertyField(levelObject.FindProperty("geometryRoot"));
            if (EditorGUI.EndChangeCheck())
            {
                levelObject.ApplyModifiedProperties();
                MarkSceneChanged();
                MarkValidationStale();
            }
        }

        private static void DrawSummaryRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(150f));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            }
        }

        private void DrawAssetReferences()
        {
            EditorGUILayout.LabelField("Shared profiles", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Agent, Areas, and Runtime Query Budget can be shared by several levels. " +
                "Edit shows every known dependent scene/prefab first; Make Local Copy keeps " +
                "the current values but isolates this level.",
                MessageType.None);
            var levelObject = new SerializedObject(selectedLevel);
            levelObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawProfileReference<NavigationAgentProfile>(
                levelObject,
                "defaultAgentProfile",
                "Agent",
                "Agent",
                null);
            DrawProfileReference<NavigationAreaCatalog>(
                levelObject,
                "areaCatalog",
                "Areas",
                "Areas",
                catalog => catalog.ResetToDefaults());
            DrawProfileReference<NavigationPerformanceProfile>(
                levelObject,
                "performanceProfile",
                "Runtime Query Budget",
                "RuntimeQueryBudget",
                profile => profile.ApplyStartingPreset(NavigationDeviceTier.MobileMedium));
            if (EditorGUI.EndChangeCheck())
            {
                levelObject.ApplyModifiedProperties();
                MarkSceneChanged();
                MarkValidationStale();
            }
        }

        private void DrawProfileReference<T>(
            SerializedObject levelObject,
            string propertyName,
            string label,
            string fileSuffix,
            Action<T> initialize)
            where T : ScriptableObject
        {
            SerializedProperty property = levelObject.FindProperty(propertyName);
            EditorGUILayout.PropertyField(property, new GUIContent(label));
            T current = property.objectReferenceValue as T;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(current == null))
                {
                    if (GUILayout.Button("Edit", EditorStyles.miniButton))
                    {
                        pendingAction = () => ShowProfileUsageAndSelect(current);
                    }
                }

                if (GUILayout.Button("New", EditorStyles.miniButton))
                {
                    pendingAction = () =>
                    {
                        T created = CreateProfile(fileSuffix, initialize);
                        AssignProfile(propertyName, created);
                    };
                }

                using (new EditorGUI.DisabledScope(current == null))
                {
                    if (GUILayout.Button("Make Local Copy", EditorStyles.miniButton))
                    {
                        pendingAction = () =>
                        {
                            T copy = NavigationProfileAssets.MakeLocalCopy(
                                current,
                                $"{GeneratedSettingsFolder}/{GetSceneKey()}_{fileSuffix}.asset");
                            AssignProfile(propertyName, copy);
                        };
                    }
                }
            }

            EditorGUILayout.Space(4f);
        }

        private T CreateProfile<T>(string fileSuffix, Action<T> initialize)
            where T : ScriptableObject
        {
            EnsureAssetFolder(GeneratedSettingsFolder);
            T profile = CreateAsset<T>(
                $"{GeneratedSettingsFolder}/{GetSceneKey()}_{fileSuffix}.asset");
            initialize?.Invoke(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private void AssignProfile(string propertyName, UnityEngine.Object profile)
        {
            var levelObject = new SerializedObject(selectedLevel);
            levelObject.Update();
            levelObject.FindProperty(propertyName).objectReferenceValue = profile;
            levelObject.ApplyModifiedProperties();
            MarkSceneChanged();
            MarkValidationStale();
        }

        private static void ShowProfileUsageAndSelect(UnityEngine.Object profile)
        {
            IReadOnlyList<string> usages = NavigationProfileUsage.Find(profile);
            string message = usages.Count == 0
                ? "No Navigation Level dependencies were found."
                : "This shared profile is used by:\n\n" + string.Join("\n", usages);
            if (!EditorUtility.DisplayDialog(
                    "Edit shared navigation profile",
                    message,
                    "Edit Profile",
                    "Cancel"))
            {
                return;
            }

            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        private void DrawValidation()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Validate", EditorStyles.miniButton, GUILayout.Width(70f)))
                {
                    RunValidation();
                }
            }

            if (!validationReport.Evaluated)
            {
                EditorGUILayout.HelpBox(
                    "The level has not been validated in this session. Validation does not run " +
                    "on scene changes - press Validate, or just Build (it validates the level).",
                    MessageType.None);
                return;
            }

            if (validationReport.Issues.Count == 0)
            {
                EditorGUILayout.HelpBox("Authoring setup is valid for export.", MessageType.Info);
                return;
            }

            DrawIssueCategory(NavigationValidationCategory.Setup, "Level setup");
            DrawIssueCategory(NavigationValidationCategory.Geometry, "Geometry");
            DrawIssueCategory(NavigationValidationCategory.Identifiers, "Stable ids");
            DrawIssueCategory(NavigationValidationCategory.Budgets, "Runtime budgets");
        }

        private void DrawIssueCategory(NavigationValidationCategory category, string title)
        {
            int count = 0;
            for (int i = 0; i < validationReport.Issues.Count; i++)
            {
                if (validationReport.Issues[i].Category == category)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField($"{title} ({count})", EditorStyles.miniBoldLabel);
            for (int i = 0; i < validationReport.Issues.Count; i++)
            {
                NavigationValidationIssue issue = validationReport.Issues[i];
                if (issue.Category != category)
                {
                    continue;
                }

                DrawIssue(issue);
            }
        }

        private void DrawIssue(NavigationValidationIssue issue)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.HelpBox(issue.Message, ToMessageType(issue.Severity));
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(110f)))
                {
                    if (issue.Context != null
                        && GUILayout.Button("Select", EditorStyles.miniButton))
                    {
                        Selection.activeObject = issue.Context;
                        EditorGUIUtility.PingObject(issue.Context);
                    }

                    if (issue.CanFix
                        && GUILayout.Button(
                            new GUIContent(issue.FixLabel, "A safe auto-fix, revertible with Ctrl+Z."),
                            EditorStyles.miniButton))
                    {
                        NavigationValidationIssue captured = issue;
                        pendingAction = () =>
                        {
                            captured.Fix();
                            MarkSceneChanged();
                            RunValidation();
                        };
                    }
                }
            }
        }

        private static MessageType ToMessageType(NavigationValidationSeverity severity)
        {
            return severity switch
            {
                NavigationValidationSeverity.Error => MessageType.Error,
                NavigationValidationSeverity.Warning => MessageType.Warning,
                _ => MessageType.Info
            };
        }

        private void DrawBuildStatus()
        {
            EditorGUILayout.LabelField("Build pipeline", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Build for Client produces a deterministic DotRecast binary and puts it into " +
                "Generated/Navigation - that asset is exactly what ships with the app build.\n" +
                "Upload to Server sends that artifact to the running server over HTTP, which is " +
                "the only way that works when the server is not on this machine.\n" +
                "Export to Folder writes the same files into the server artifact folder instead.",
                MessageType.Info);

            NavigationArtifactAsset builtArtifact = selectedLevel != null
                ? NavigationArtifactBuilder.LoadClientArtifact(selectedLevel.LevelId)
                : null;

            using (new EditorGUILayout.HorizontalScope())
            {
                // The button is not blindly disabled: it runs validation itself
                // and explains exactly what prevents the level from building.
                if (GUILayout.Button("Build for Client", GUILayout.Height(30f)))
                {
                    pendingAction = BuildForClient;
                }

                using (new EditorGUI.DisabledScope(builtArtifact == null || serverRequestPending))
                {
                    if (GUILayout.Button(
                            serverRequestPending ? "Uploading..." : "Upload to Server",
                            GUILayout.Height(30f)))
                    {
                        UploadToServer(builtArtifact);
                    }

                    if (GUILayout.Button("Export to Folder", GUILayout.Height(30f)))
                    {
                        pendingAction = ExportForServer;
                    }
                }
            }

            if (validationReport.Evaluated && validationReport.HasErrors)
            {
                EditorGUILayout.HelpBox(
                    "There are validation errors - Build will stop and show the list. " +
                    "Open the Validation section above.",
                    MessageType.Error);
            }

            if (builtArtifact == null)
            {
                EditorGUILayout.HelpBox(
                    "The client artifact for this level is not built yet - run Build for Client first.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "Client artifact",
                    $"{builtArtifact.LevelId} · {NavigationArtifactIndex.Short(builtArtifact.ArtifactHash)} · " +
                    $"{builtArtifact.PolygonCount} polygons");

                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("Danger zone", EditorStyles.boldLabel);
                if (GUILayout.Button("Remove baked navigation"))
                {
                    RemoveBakedNavigation(builtArtifact);
                }
            }

            if (!string.IsNullOrWhiteSpace(lastBuildMessage))
            {
                EditorGUILayout.HelpBox(lastBuildMessage, MessageType.Info);
            }

            if (!string.IsNullOrWhiteSpace(lastExportMessage))
            {
                EditorGUILayout.HelpBox(lastExportMessage, MessageType.Info);
                DrawPostExportActions();
            }
        }

        private void RemoveBakedNavigation(NavigationArtifactAsset artifact)
        {
            IReadOnlyList<string> files;
            try
            {
                files = NavigationArtifactBuilder.GetClientArtifactPaths(artifact);
            }
            catch (Exception exception)
            {
                lastBuildMessage = "Cannot remove baked navigation: " + exception.Message;
                Debug.LogException(exception);
                return;
            }

            string message =
                "Delete the generated navigation files for this level?\n\n" +
                string.Join("\n", files) +
                "\n\nServer copies are not deleted.";
            if (!EditorUtility.DisplayDialog(
                    "Remove baked navigation",
                    message,
                    "Delete files",
                    "Cancel"))
            {
                return;
            }

            try
            {
                NavigationArtifactBuilder.DeleteClientArtifact(artifact);
                lastBuildMessage = "Baked navigation files were deleted. Server copies were left unchanged.";
                lastExportMessage = string.Empty;
                exportedManifestPath = string.Empty;
                exportedHash = string.Empty;
                artifactComparisons = null;
            }
            catch (Exception exception)
            {
                lastBuildMessage = "Could not delete baked navigation: " + exception.Message;
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// Post-export actions. The server is checked ONLY on a button press -
        /// no automatic requests after the files are written.
        /// </summary>
        private void DrawPostExportActions()
        {
            if (string.IsNullOrEmpty(exportedManifestPath))
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(serverRequestPending))
                {
                    if (GUILayout.Button(
                            serverRequestPending ? "Checking..." : "Check the server",
                            EditorStyles.miniButton))
                    {
                        pendingAction = () => VerifyServerAfterExport(exportedHash);
                    }
                }

                if (GUILayout.Button("Copy the launch command", EditorStyles.miniButton))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildServerCommand(exportedManifestPath);
                    lastExportMessage += "\n\nThe launch command was copied to the clipboard.";
                }
            }

            if (!string.IsNullOrWhiteSpace(serverVerdictMessage))
            {
                EditorGUILayout.HelpBox(serverVerdictMessage, serverVerdictType);
            }
        }

        private static string BuildServerCommand(string manifestPath)
        {
            NavigationServerSettings settings = NavigationServerSettings.LoadOrNull();
            string prefix = settings != null ? settings.ListenPrefix : "http://127.0.0.1:5079/";
            return $"./{NavigationServerInstaller.InstallFolderName}/run-server.sh " +
                   $"--listen '{prefix}' --manifest '{manifestPath}'";
        }

        private void VerifyServerAfterExport(string expectedHash)
        {
            serverRequestPending = true;
            serverVerdictMessage = "Requesting /health...";
            serverVerdictType = MessageType.None;
            NavigationServerEditorClient.Get("/health", (success, payload) =>
            {
                if (this == null)
                {
                    return;
                }

                serverRequestPending = false;
                if (!success)
                {
                    serverVerdictMessage =
                        "Server unreachable: " + payload +
                        "\nStart it with the copied command.";
                    serverVerdictType = MessageType.Warning;
                    Repaint();
                    return;
                }

                if (!NavigationServerEditorClient.TryParse(
                        payload,
                        out NavigationServerEditorClient.HealthResponse health))
                {
                    serverVerdictMessage = "Unrecognized server response: " + payload;
                    serverVerdictType = MessageType.Warning;
                    Repaint();
                    return;
                }

                bool matches = string.Equals(
                    health.artifactHash,
                    expectedHash,
                    StringComparison.OrdinalIgnoreCase);
                serverVerdictMessage = matches
                    ? $"OK: the server picked up the new artifact: {health.levelId} · " +
                      $"{NavigationArtifactIndex.Short(health.artifactHash)}."
                    : $"Warning: the server is still on the old artifact " +
                      $"({NavigationArtifactIndex.Short(health.artifactHash)} instead of " +
                      $"{NavigationArtifactIndex.Short(expectedHash)}). Restart it.";
                serverVerdictType = matches ? MessageType.Info : MessageType.Warning;
                Repaint();
            });
        }

        private void BuildForClient()
        {
            // 1. Validation is mandatory and is run by Build itself, so the user
            //    never has to remember about it or face a greyed out button.
            RunValidation();
            if (validationReport.HasErrors)
            {
                lastBuildMessage = string.Empty;
                EditorUtility.DisplayDialog(
                    "Build stopped",
                    "The level is not ready to build.\n\nFirst error:\n" +
                    validationReport.DescribeFirstError() +
                    $"\n\nTotal errors: {validationReport.ErrorCount}. " +
                    "The list with Select and Fix buttons is in Overview -> Validation.",
                    "Got it");
                selectedTab = OverviewTab;
                return;
            }

            using var progress = new NavigationBuildProgress("Build navigation for client", 7);
            try
            {
                NavigationArtifactBuildResult result =
                    NavigationArtifactBuilder.BuildForClient(selectedLevel, progress);
                lastBuildMessage = FormatBuildCard(result);
                lastExportMessage = string.Empty;
                NavigationPathProbe.InvalidateCache();
                NavigationSceneTools.ClearProbeResults();
                NavigationSceneTools.ClearAnalysis();
                Selection.activeObject = result.Asset;
                EditorGUIUtility.PingObject(result.Asset);
                Debug.Log(
                    $"[CustomNavigation] Built client artifact {result.Hash} for level " +
                    $"'{selectedLevel.LevelId}': {result.ClientDataPath}.",
                    result.Asset);
            }
            catch (NavigationBuildCanceledException canceled)
            {
                lastBuildMessage = "Build canceled: " + canceled.Stage;
            }
            catch (Exception exception)
            {
                lastBuildMessage = "Build for Client failed: " + exception.Message;
                Debug.LogException(exception, selectedLevel);
            }
        }

        private static string FormatBuildCard(NavigationArtifactBuildResult result)
        {
            return
                $"Build finished in {result.ElapsedSeconds:0.0} s\n" +
                $"Polygons: {result.PolygonCount}   ·   Sources: {result.SourceMeshCount}   ·   " +
                $"Size: {result.ByteSize / 1024f:0.#} KB\n" +
                $"Hash: {NavigationArtifactIndex.Short(result.Hash)}\n" +
                result.ClientDataPath;
        }

        private void ExportForServer()
        {
            using var progress = new NavigationBuildProgress("Export navigation for server", 2, false);
            try
            {
                progress.Stage("Checking the artifact and the server folder");
                NavigationServerExportResult export = NavigationArtifactBuilder.ExportForServer(selectedLevel);
                progress.Stage("Writing the server files");
                lastExportMessage =
                    $"Exported to the server folder in {progress.ElapsedSeconds:0.0} s\n" +
                    $"{export.LevelId} · {NavigationArtifactIndex.Short(export.Hash)}\n" +
                    export.ServerDataPath +
                    (export.SetAsActive ? "\nMarked as active.manifest.json." : string.Empty) +
                    "\nThis only reaches the server if it reads that very folder - " +
                    "use Upload to Server for a remote one.";
                Debug.Log(
                    $"[CustomNavigation] Exported artifact {export.Hash} to {export.ServerDataPath}.",
                    selectedLevel);
                artifactComparisons = null;
                // The server check does NOT run automatically: a button is shown instead.
                exportedManifestPath = export.ServerManifestPath;
                exportedHash = export.Hash;
            }
            catch (NavigationBuildCanceledException canceled)
            {
                lastExportMessage = "Export canceled: " + canceled.Stage;
            }
            catch (Exception exception)
            {
                lastExportMessage = "Export to Folder failed: " + exception.Message;
                Debug.LogException(exception, selectedLevel);
            }
        }

        /// <summary>
        /// Sends the artifact to the running server over HTTP. Unlike the folder export
        /// this works for a server on another machine, and it lands where the server
        /// actually reads - no restart, no path guessing.
        /// </summary>
        private void UploadToServer(NavigationArtifactAsset artifact)
        {
            serverRequestPending = true;
            lastExportMessage = "Uploading to " + NavigationServerEditorClient.BaseUrl + "...";
            NavigationServerUploader.Upload(artifact, true, (success, message) =>
            {
                if (this == null)
                {
                    return;
                }

                serverRequestPending = false;
                lastExportMessage = success ? message : "Upload failed: " + message;
                if (success)
                {
                    Debug.Log("[CustomNavigation] " + message, artifact);
                }
                else
                {
                    Debug.LogWarning("[CustomNavigation] Upload failed: " + message, artifact);
                }

                artifactComparisons = null;
                Repaint();
            });
        }

        private void DrawSources()
        {
            MeshFilter[] meshFilters = selectedLevel.GeometryRoot.GetComponentsInChildren<MeshFilter>(true);
            int readableMeshCount = 0;
            int missingSourceCount = 0;
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter mesh = meshFilters[i];
                if (mesh.sharedMesh == null)
                {
                    continue;
                }

                readableMeshCount++;
                if (!mesh.TryGetComponent(out NavigationGeometrySource _))
                {
                    missingSourceCount++;
                }
            }

            EditorGUILayout.LabelField("Explicit geometry sources", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The scan reads MeshFilter.sharedMesh directly. It does not query Unity Physics " +
                "and it never needs runtime scene baking on a mobile device.",
                MessageType.Info);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("Readable MeshFilters", readableMeshCount.ToString());
                DrawSummaryRow("Missing source tag", missingSourceCount.ToString());
                DrawSummaryRow("Geometry root", selectedLevel.GeometryRoot.name);
            }

            using (new EditorGUI.DisabledScope(missingSourceCount == 0))
            {
                if (GUILayout.Button($"Add {missingSourceCount} Missing Sources", GUILayout.Height(30f)))
                {
                    AddMissingSources(meshFilters);
                }
            }

            if (Selection.activeGameObject != null
                && Selection.activeGameObject.scene.IsValid()
                && Selection.activeGameObject.GetComponent<NavigationGeometrySource>() == null)
            {
                if (GUILayout.Button("Add Source To Selected GameObject"))
                {
                    AddSource(Selection.activeGameObject, false);
                    MarkValidationStale();
                }
            }

            EditorGUILayout.Space(8f);
            NavigationGeometrySource[] sources = selectedLevel.GeometryRoot
                .GetComponentsInChildren<NavigationGeometrySource>(true);

            DrawSourceFilters(sources);

            List<NavigationGeometrySource> visible = FilterSources(sources);
            showSourceDetails = EditorGUILayout.Foldout(
                showSourceDetails,
                $"Sources ({visible.Count} of {sources.Length})",
                true);
            if (!showSourceDetails)
            {
                return;
            }

            DrawBatchEditBar(visible);

            for (int i = 0; i < visible.Count; i++)
            {
                DrawSourceEditor(visible[i]);
            }
        }

        // -- Source filters and bulk editing -----------------------------------
        private void DrawSourceFilters(IReadOnlyList<NavigationGeometrySource> sources)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                sourceSearch = EditorGUILayout.TextField(
                    new GUIContent("Search", "Filter by object name."),
                    sourceSearch ?? string.Empty);
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(24f)))
                {
                    sourceSearch = string.Empty;
                    GUI.FocusControl(null);
                }
            }

            sourceModeFilter = EditorGUILayout.Popup(
                new GUIContent("Mode", "Show only the sources of the selected mode."),
                sourceModeFilter,
                new[] { "All", "Include", "Block", "Ignore" });

            int include = 0;
            int block = 0;
            int ignore = 0;
            for (int i = 0; i < sources.Count; i++)
            {
                switch (sources[i].Mode)
                {
                    case NavigationGeometryMode.Block:
                        block++;
                        break;
                    case NavigationGeometryMode.Ignore:
                        ignore++;
                        break;
                    default:
                        include++;
                        break;
                }
            }

            EditorGUILayout.LabelField(
                $"Include: {include}   ·   Block: {block}   ·   Ignore: {ignore}",
                EditorStyles.miniLabel);
        }

        private List<NavigationGeometrySource> FilterSources(IReadOnlyList<NavigationGeometrySource> sources)
        {
            var result = new List<NavigationGeometrySource>();
            for (int i = 0; i < sources.Count; i++)
            {
                NavigationGeometrySource source = sources[i];
                if (source == null)
                {
                    continue;
                }

                if (sourceModeFilter > 0 && (int)source.Mode != sourceModeFilter - 1)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(sourceSearch)
                    && source.name.IndexOf(sourceSearch, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                result.Add(source);
            }

            return result;
        }

        /// <summary>Bulk edit of mode and area for every filtered source.</summary>
        private void DrawBatchEditBar(List<NavigationGeometrySource> visible)
        {
            if (visible.Count == 0)
            {
                EditorGUILayout.HelpBox("No source matches the filter.", MessageType.None);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    $"Apply to all shown ({visible.Count})",
                    EditorStyles.miniBoldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    batchMode = (NavigationGeometryMode)EditorGUILayout.EnumPopup(batchMode);
                    if (GUILayout.Button("Set mode", EditorStyles.miniButton, GUILayout.Width(110f)))
                    {
                        ApplyToSources(visible, "mode", (int)batchMode, "Batch Set Navigation Mode");
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    batchArea = (NavigationArea)EditorGUILayout.EnumPopup(batchArea);
                    if (GUILayout.Button("Set area", EditorStyles.miniButton, GUILayout.Width(110f)))
                    {
                        ApplyToSources(visible, "area", (int)batchArea, "Batch Set Navigation Area");
                    }
                }

                if (GUILayout.Button("Select all shown in the scene", EditorStyles.miniButton))
                {
                    var objects = new UnityEngine.Object[visible.Count];
                    for (int i = 0; i < visible.Count; i++)
                    {
                        objects[i] = visible[i].gameObject;
                    }

                    Selection.objects = objects;
                }
            }
        }

        private void ApplyToSources(
            List<NavigationGeometrySource> sources,
            string propertyName,
            int value,
            string undoName)
        {
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            for (int i = 0; i < sources.Count; i++)
            {
                NavigationGeometrySource source = sources[i];
                Undo.RecordObject(source, undoName);
                var sourceObject = new SerializedObject(source);
                sourceObject.Update();
                SerializedProperty property = sourceObject.FindProperty(propertyName);
                if (property != null)
                {
                    property.intValue = value;
                    sourceObject.ApplyModifiedProperties();
                }

                EditorUtility.SetDirty(source);
            }

            Undo.CollapseUndoOperations(group);
            MarkSceneChanged();
            MarkValidationStale();
        }

        private void DrawSourceEditor(NavigationGeometrySource source)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField(source, typeof(NavigationGeometrySource), true);
                    if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(54f)))
                    {
                        Selection.activeObject = source.gameObject;
                        EditorGUIUtility.PingObject(source.gameObject);
                    }

                    if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        NavigationGeometrySource removed = source;
                        pendingAction = () =>
                        {
                            Undo.DestroyObjectImmediate(removed);
                            MarkSceneChanged();
                            MarkValidationStale();
                        };
                        return;
                    }
                }

                // SerializedObject records an Undo entry itself on ApplyModifiedProperties.
                var sourceObject = new SerializedObject(source);
                sourceObject.Update();
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(sourceObject.FindProperty("mode"));
                EditorGUILayout.PropertyField(sourceObject.FindProperty("area"));
                EditorGUILayout.PropertyField(sourceObject.FindProperty("includeChildren"));
                EditorGUILayout.PropertyField(sourceObject.FindProperty("includeInactiveChildren"));
                if (EditorGUI.EndChangeCheck())
                {
                    sourceObject.ApplyModifiedProperties();
                    MarkSceneChanged();
                    MarkValidationStale();
                }
            }
        }

        private void AddMissingSources(IReadOnlyList<MeshFilter> meshFilters)
        {
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Navigation Geometry Sources");
            for (int i = 0; i < meshFilters.Count; i++)
            {
                MeshFilter mesh = meshFilters[i];
                if (mesh.sharedMesh == null || mesh.TryGetComponent(out NavigationGeometrySource _))
                {
                    continue;
                }

                AddSource(mesh.gameObject, false);
            }

            Undo.CollapseUndoOperations(group);
            MarkSceneChanged();
            MarkValidationStale();
        }

        private void AddSource(GameObject target, bool includeChildren)
        {
            NavigationGeometrySource source = Undo.AddComponent<NavigationGeometrySource>(target);
            var sourceObject = new SerializedObject(source);
            sourceObject.Update();
            sourceObject.FindProperty("area").intValue = (int)NavigationArea.Ground;
            sourceObject.FindProperty("includeChildren").boolValue = includeChildren;
            sourceObject.FindProperty("includeInactiveChildren").boolValue = false;
            sourceObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(source);
        }

        private void DrawBuildAndBudgets()
        {
            // Agent diagram: the parameters that really define the navmesh shape.
            NavigationAgentProfile agentProfile = selectedLevel.DefaultAgentProfile;
            if (agentProfile != null)
            {
                EditorGUILayout.LabelField("Agent the navmesh is built for", EditorStyles.boldLabel);
                NavigationAgentDiagram.DrawFoldout(agentProfile, selectedLevel);
                if (GUILayout.Button("Edit the agent profile", EditorStyles.miniButton))
                {
                    pendingAction = () => ShowProfileUsageAndSelect(agentProfile);
                }

                EditorGUILayout.Space(10f);
            }

            EditorGUILayout.LabelField("Bake quality", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These parameters affect the navmesh itself, both on the client and on the server, " +
                "because the artifact is the same. Bake Quality is stored locally on this " +
                "Navigation Level; it is not a shared profile.",
                MessageType.None);
            var levelObject = new SerializedObject(selectedLevel);
            levelObject.Update();
            EditorGUI.BeginChangeCheck();
            NavigationInspectorGUI.DrawBuildSettings(
                levelObject.FindProperty("buildSettings"),
                "NavigationWindow.BuildSettings",
                agentProfile);
            if (EditorGUI.EndChangeCheck())
            {
                levelObject.ApplyModifiedProperties();
                MarkSceneChanged();
            }

            if (GUILayout.Button("Use Project Bake Default", EditorStyles.miniButton))
            {
                pendingAction = ApplyProjectBakeDefault;
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Mobile query budget", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These settings affect neither the navmesh nor the server. They only constrain " +
                "the client request scheduler and start to matter with dozens of agents.",
                MessageType.None);
            NavigationPerformanceProfile profile = selectedLevel.PerformanceProfile;
            if (profile == null)
            {
                EditorGUILayout.HelpBox(
                    "No profile assigned - Mobile Medium values will be used. " +
                    "This does not block the navmesh build.",
                    MessageType.Info);
                if (GUILayout.Button("Create Mobile Performance Profile"))
                {
                    profile = CreatePerformanceProfile(GetSceneKey());
                    AssignPerformanceProfile(profile);
                }

                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("Preset", profile.DeviceTier.ToString());
                DrawSummaryRow("Frame budget", profile.FrameBudgetMilliseconds.ToString("0.##") + " ms");
                DrawSummaryRow("Backlog / active", $"{profile.MaximumQueuedQueries} / {profile.MaximumConcurrentSlicedQueries}");
                if (GUILayout.Button("Edit Runtime Query Budget", EditorStyles.miniButton))
                {
                    pendingAction = () => ShowProfileUsageAndSelect(profile);
                }
            }

        }

        private void ApplyProjectBakeDefault()
        {
            NavigationProjectSettings settings = NavigationProjectSettings.instance;
            Undo.RecordObject(selectedLevel, "Apply Navigation Bake Default");
            JsonUtility.FromJsonOverwrite(
                JsonUtility.ToJson(settings.DefaultBuildSettings),
                selectedLevel.BuildSettings);
            EditorUtility.SetDirty(selectedLevel);
            MarkSceneChanged();
            MarkValidationStale();
        }

        // ── Tools tab ─────────────────────────────────────────────────────────
        private void DrawTools()
        {
            EditorGUILayout.HelpBox(
                "Every check here runs from a button and caches its result. " +
                "Nothing is computed in the background, while dragging handles, or on scene changes.",
                MessageType.None);

            if (!NavigationHighlightSettings.Enabled)
            {
                EditorGUILayout.HelpBox(
                    "Custom Navigation preview layers are off. Configure them in the Scene View " +
                    "overlay or personal Scene Preview preferences.",
                    MessageType.Warning);
                if (GUILayout.Button("Open Scene Preview Preferences"))
                {
                    SettingsService.OpenUserPreferences(
                        NavigationProjectSettings.PreferencesProviderPath);
                }
            }

            NavigationArtifactAsset artifact = NavigationArtifactBuilder.LoadClientArtifact(selectedLevel.LevelId);
            if (artifact == null)
            {
                EditorGUILayout.HelpBox(
                    "There is no built artifact for this level. Run Build for Client " +
                    "in the Bake section.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(8f);
            DrawAgentPreviewTool();
            EditorGUILayout.Space(10f);
            DrawPathProbeTool(artifact);
            EditorGUILayout.Space(10f);
            DrawAnalysisTool(artifact);
        }

        private void DrawAgentPreviewTool()
        {
            EditorGUILayout.LabelField("Agent reference in the scene", EditorStyles.boldLabel);
            NavigationAgentProfile agent = selectedLevel.DefaultAgentProfile;
            if (agent != null)
            {
                NavigationSceneTools.PreviewAgent = agent;
            }

            if (agent == null)
            {
                EditorGUILayout.HelpBox("The level has no Agent Profile assigned.", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool enabled = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Show the agent reference",
                        "A static marker: drawn with cheap Handles and never touches the navmesh."),
                    NavigationSceneTools.AgentPreviewEnabled);
                if (enabled != NavigationSceneTools.AgentPreviewEnabled)
                {
                    NavigationSceneTools.AgentPreviewEnabled = enabled;
                }

                DrawSummaryRow("Height / radius", $"{agent.Height:0.##} m / {agent.Radius:0.##} m");
                DrawSummaryRow("Minimum passage", $"{agent.Radius * 2f:0.##} m");
                DrawSummaryRow("Maximum step", $"{agent.MaximumClimb:0.##} m");

                using (new EditorGUI.DisabledScope(!NavigationSceneTools.AgentPreviewEnabled))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Place at the view center", EditorStyles.miniButton))
                    {
                        SceneView view = SceneView.lastActiveSceneView;
                        if (view != null)
                        {
                            NavigationSceneTools.AgentPreviewPosition = view.pivot;
                            SceneView.RepaintAll();
                        }
                    }

                    if (GUILayout.Button("Snap to the navmesh", EditorStyles.miniButton))
                    {
                        pendingAction = () => SnapAgentPreviewToNavmesh(agent);
                    }
                }
            }
        }

        private void SnapAgentPreviewToNavmesh(NavigationAgentProfile agent)
        {
            NavigationArtifactAsset artifact = NavigationArtifactBuilder.LoadClientArtifact(selectedLevel.LevelId);
            NavigationProbeResult result = NavigationPathProbe.FindPath(
                artifact,
                agent,
                NavigationSceneTools.AgentPreviewPosition,
                NavigationSceneTools.AgentPreviewPosition);
            if (result.HasProjectedStart)
            {
                NavigationSceneTools.AgentPreviewPosition = result.ProjectedStart;
                SceneView.RepaintAll();
            }
            else
            {
                EditorUtility.DisplayDialog(
                    "Could not snap to the navmesh",
                    result.DescribeFailure(),
                    "Got it");
            }
        }

        private void DrawPathProbeTool(NavigationArtifactAsset artifact)
        {
            EditorGUILayout.LabelField("Path Probe", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool enabled = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Show the Start / Destination points",
                        "Dragging the points computes nothing - only the Find Path button does."),
                    NavigationSceneTools.ProbeEnabled);
                if (enabled != NavigationSceneTools.ProbeEnabled)
                {
                    NavigationSceneTools.ProbeEnabled = enabled;
                }

                EditorGUI.BeginChangeCheck();
                Vector3 start = EditorGUILayout.Vector3Field("Start", NavigationSceneTools.ProbeStart);
                Vector3 destination = EditorGUILayout.Vector3Field(
                    "Destination",
                    NavigationSceneTools.ProbeDestination);
                if (EditorGUI.EndChangeCheck())
                {
                    NavigationSceneTools.ProbeStart = start;
                    NavigationSceneTools.ProbeDestination = destination;
                    SceneView.RepaintAll();
                }

                probeSourceMode = GUILayout.Toolbar(
                    Mathf.Clamp(probeSourceMode, 0, 2),
                    new[] { "Local", "Server", "Both" });

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Swap", EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        Vector3 previousStart = NavigationSceneTools.ProbeStart;
                        NavigationSceneTools.ProbeStart = NavigationSceneTools.ProbeDestination;
                        NavigationSceneTools.ProbeDestination = previousStart;
                        SceneView.RepaintAll();
                    }

                    if (GUILayout.Button("From Test Points", EditorStyles.miniButton))
                    {
                        pendingAction = PickProbePointsFromTestPoints;
                    }

                    if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        NavigationSceneTools.ClearProbeResults();
                    }
                }

                using (new EditorGUI.DisabledScope(artifact == null || probeRequestPending))
                {
                    if (GUILayout.Button(
                            probeRequestPending ? "Requesting the server..." : "Find Path",
                            GUILayout.Height(28f)))
                    {
                        pendingAction = () => RunPathProbe(artifact);
                    }
                }

                DrawProbeResults();
            }
        }

        private void PickProbePointsFromTestPoints()
        {
            NavigationTestPoint[] points = selectedLevel.GetComponentsInChildren<NavigationTestPoint>(true);
            if (points.Length < 2)
            {
                EditorUtility.DisplayDialog(
                    "Not enough Test Points",
                    "At least two NavigationTestPoint objects are required in the level.",
                    "Got it");
                return;
            }

            NavigationSceneTools.ProbeStart = points[0].transform.position;
            NavigationSceneTools.ProbeDestination = points[points.Length - 1].transform.position;
            SceneView.RepaintAll();
        }

        private void RunPathProbe(NavigationArtifactAsset artifact)
        {
            Vector3 start = NavigationSceneTools.ProbeStart;
            Vector3 destination = NavigationSceneTools.ProbeDestination;

            if (probeSourceMode != 1)
            {
                NavigationProbeResult local = NavigationPathProbe.FindPath(
                    artifact,
                    selectedLevel.DefaultAgentProfile,
                    start,
                    destination);
                NavigationSceneTools.SetLocalResult(local);
            }
            else
            {
                NavigationSceneTools.SetLocalResult(null);
            }

            if (probeSourceMode == 0)
            {
                NavigationSceneTools.SetServerPath(null, string.Empty);
                return;
            }

            RequestServerPath(artifact, start, destination);
        }

        private void RequestServerPath(NavigationArtifactAsset artifact, Vector3 start, Vector3 destination)
        {
            probeRequestPending = true;
            var payload = new NavigationServerEditorClient.PathRequest
            {
                requestId = "editor-probe-" + DateTime.Now.Ticks,
                start = NavigationServerEditorClient.ServerVector3.From(start),
                destination = NavigationServerEditorClient.ServerVector3.From(destination),
                clientArtifactHash = artifact != null ? artifact.ArtifactHash : string.Empty,
                clientPathFingerprint = string.Empty
            };

            NavigationServerEditorClient.Post("/path", JsonUtility.ToJson(payload), (success, response) =>
            {
                if (this == null)
                {
                    return;
                }

                probeRequestPending = false;
                if (!success)
                {
                    NavigationSceneTools.SetServerPath(null, "Server unreachable: " + response);
                    Repaint();
                    return;
                }

                if (!NavigationServerEditorClient.TryParse(
                        response,
                        out NavigationServerEditorClient.PathResponse parsed)
                    || parsed.points == null)
                {
                    NavigationSceneTools.SetServerPath(null, "Unrecognized server response: " + response);
                    Repaint();
                    return;
                }

                if (!parsed.success || parsed.points.Length == 0)
                {
                    NavigationSceneTools.SetServerPath(
                        null,
                        string.IsNullOrWhiteSpace(parsed.message)
                            ? "The server found no route."
                            : "Server: " + parsed.message);
                    Repaint();
                    return;
                }

                var points = new Vector3[parsed.points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    points[i] = parsed.points[i].ToUnity();
                }

                bool hashMatches = artifact == null
                                   || string.Equals(
                                       parsed.artifactHash,
                                       artifact.ArtifactHash,
                                       StringComparison.OrdinalIgnoreCase);
                NavigationSceneTools.SetServerPath(
                    points,
                    $"The server returned {points.Length} points, artifact " +
                    NavigationArtifactIndex.Short(parsed.artifactHash) +
                    (hashMatches ? " matches the client one." : " DIFFERS from the client one!"));
                Repaint();
            });
        }

        private void DrawProbeResults()
        {
            NavigationProbeResult local = NavigationSceneTools.LocalResult;
            if (local != null)
            {
                EditorGUILayout.Space(4f);
                if (local.Success)
                {
                    EditorGUILayout.HelpBox(
                        $"Local: {local.Length:0.##} m, {local.Points.Length} points, " +
                        $"{local.CorridorPolygonCount} corridor polygons, {local.ElapsedMilliseconds:0.##} ms" +
                        (local.Areas.Count > 0
                            ? "\nAreas: " + string.Join(", ", local.Areas)
                            : string.Empty) +
                        (local.Partial ? "\nWarning: partial path, the destination is not fully reachable." : string.Empty),
                        local.Partial ? MessageType.Warning : MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        local.DescribeFailure() +
                        (string.IsNullOrEmpty(local.Hint) ? string.Empty : "\n→ " + local.Hint),
                        MessageType.Error);
                }
            }

            if (!string.IsNullOrWhiteSpace(NavigationSceneTools.ServerMessage))
            {
                EditorGUILayout.HelpBox(
                    NavigationSceneTools.ServerMessage,
                    NavigationSceneTools.ServerPath.Length > 0 ? MessageType.Info : MessageType.Warning);
            }

            if (probeSourceMode == 2
                && local != null
                && local.Success
                && NavigationSceneTools.ServerPath.Length > 0)
            {
                float delta = ComparePaths(local.Points, NavigationSceneTools.ServerPath);
                EditorGUILayout.HelpBox(
                    delta <= 0.05f
                        ? $"The local and server paths match (max divergence {delta:0.###} m)."
                        : $"Warning: the paths diverge, maximum deviation {delta:0.##} m. " +
                          "Check that the server uses the same artifact and the same agent profile.",
                    delta <= 0.05f ? MessageType.Info : MessageType.Warning);
            }
        }

        private static float ComparePaths(Vector3[] left, Vector3[] right)
        {
            float worst = 0f;
            int count = Mathf.Min(left.Length, right.Length);
            for (int i = 0; i < count; i++)
            {
                worst = Mathf.Max(worst, Vector3.Distance(left[i], right[i]));
            }

            if (left.Length != right.Length)
            {
                worst = Mathf.Max(worst, Vector3.Distance(left[left.Length - 1], right[right.Length - 1]));
            }

            return worst;
        }

        private void DrawAnalysisTool(NavigationArtifactAsset artifact)
        {
            EditorGUILayout.LabelField("Navmesh analysis", EditorStyles.boldLabel);
            NavigationAgentProfile agent = selectedLevel.DefaultAgentProfile;
            if (clearanceThreshold <= 0f)
            {
                clearanceThreshold = agent != null ? Mathf.Max(0.1f, agent.Radius) : 0.45f;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool show = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Show the last analysis",
                        "A ready mesh is drawn from the cache - nothing is recomputed."),
                    NavigationSceneTools.ShowAnalysis);
                if (show != NavigationSceneTools.ShowAnalysis)
                {
                    NavigationSceneTools.ShowAnalysis = show;
                }

                clearanceThreshold = EditorGUILayout.Slider(
                    new GUIContent(
                        "Narrowness threshold (m)",
                        "Triangles closer than this distance to the navmesh edge count as narrow."),
                    clearanceThreshold,
                    0.05f,
                    3f);

                using (new EditorGUI.DisabledScope(artifact == null || agent == null))
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Analyze Clearance", GUILayout.Height(26f)))
                    {
                        pendingAction = () => RunAnalysis(artifact, agent, true);
                    }

                    if (GUILayout.Button("Analyze Slopes", GUILayout.Height(26f)))
                    {
                        pendingAction = () => RunAnalysis(artifact, agent, false);
                    }
                }

                NavigationNavmeshAnalysis analysis = NavigationSceneTools.Analysis;
                if (analysis != null && analysis.HasResult)
                {
                    EditorGUILayout.HelpBox(
                        analysis.Summary + $"\nProduced at {analysis.EvaluatedAt:HH:mm:ss}.",
                        analysis.SummaryType);
                    if (GUILayout.Button("Clear analysis", EditorStyles.miniButton))
                    {
                        NavigationSceneTools.ClearAnalysis();
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "The analysis has not run. It walks the whole navmesh, so it runs " +
                        "from a button only and is cached until the next build.",
                        MessageType.None);
                }
            }
        }

        private void RunAnalysis(
            NavigationArtifactAsset artifact,
            NavigationAgentProfile agent,
            bool clearance)
        {
            NavigationArtifactInstance instance = NavigationPathProbe.TryGetArtifact(artifact, out string error);
            if (instance == null)
            {
                EditorUtility.DisplayDialog("Analysis is not possible", error, "Got it");
                return;
            }

            using var progress = new NavigationBuildProgress(
                clearance ? "Analyze clearance" : "Analyze slopes",
                2);
            try
            {
                NavigationNavmeshAnalysis analysis = NavigationSceneTools.EnsureAnalysis();
                if (clearance)
                {
                    analysis.AnalyzeClearance(instance, clearanceThreshold, progress);
                }
                else
                {
                    analysis.AnalyzeSlopes(instance, agent.MaximumSlope, progress);
                }

                SceneView.RepaintAll();
            }
            catch (NavigationBuildCanceledException)
            {
                NavigationSceneTools.ClearAnalysis();
            }
            catch (Exception exception)
            {
                NavigationSceneTools.ClearAnalysis();
                Debug.LogException(exception, artifact);
            }
        }

        // ── Server tab ────────────────────────────────────────────────────────
        private void DrawServerSettings()
        {
            EditorGUILayout.LabelField("Navigation server", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The only place where the authoritative navigation server address is set. " +
                "The client, the bots and the editor tools all read this very asset.",
                MessageType.Info);

            NavigationServerSettings settings = NavigationServerSettings.LoadOrNull();
            if (settings == null)
            {
                EditorGUILayout.HelpBox(
                    "NavigationServerSettings does not exist yet. It must live in Resources " +
                    "so the address is readable both in the editor and in a build.",
                    MessageType.Warning);
                if (GUILayout.Button("Create Navigation Server Settings", GUILayout.Height(30f)))
                {
                    CreateServerSettings();
                }

                return;
            }

            var settingsObject = new SerializedObject(settings);
            settingsObject.Update();
            EditorGUI.BeginChangeCheck();
            NavigationInspectorGUI.DrawProperties(
                settingsObject,
                "host",
                "port",
                "useHttps",
                "requestTimeoutSeconds",
                "serverArtifactFolder",
                "notes");
            if (EditorGUI.EndChangeCheck())
            {
                settingsObject.ApplyModifiedProperties();
                NavigationServerSettings.InvalidateCache();
                AssetDatabase.SaveAssets();
                artifactComparisons = null;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Where the data is built and stored", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("Base URL", settings.BaseUrl);
                DrawSummaryRow("Listen prefix", settings.ListenPrefix);
                DrawSummaryRow("Client artifacts", NavigationArtifactBuilder.GeneratedClientFolder);
                DrawSummaryRow("Server artifacts", NavigationArtifactBuilder.ResolveServerFolder());
            }

            DrawArtifactFolderTools(settings);

            EditorGUILayout.HelpBox(
                "The navmesh is baked offline in Unity (the Bake section -> Build for Client). " +
                "The server bakes nothing: it only loads the uploaded artifact and answers POST /path.",
                MessageType.None);

            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Show in Project", EditorStyles.miniButton))
                {
                    Selection.activeObject = settings;
                    EditorGUIUtility.PingObject(settings);
                }

                if (GUILayout.Button("Open the server folder", EditorStyles.miniButton))
                {
                    string folder = NavigationArtifactBuilder.ResolveServerFolder();
                    Directory.CreateDirectory(folder);
                    EditorUtility.RevealInFinder(folder);
                }
            }

            EditorGUILayout.Space(8f);
            DrawLocalServer(settings);

            EditorGUILayout.Space(8f);
            DrawUploadSettings();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Connection check", EditorStyles.boldLabel);
            if (string.IsNullOrEmpty(serverAddressInput))
            {
                serverAddressInput = settings.BaseUrl;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                serverAddressInput = EditorGUILayout.TextField("Address", serverAddressInput);
                using (new EditorGUI.DisabledScope(serverRequestPending))
                {
                    if (GUILayout.Button("Apply", EditorStyles.miniButton, GUILayout.Width(90f)))
                    {
                        ApplyServerAddress(settings);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(serverRequestPending))
            {
                if (GUILayout.Button(
                        serverRequestPending ? "Checking..." : "Check /health",
                        GUILayout.Height(26f)))
                {
                    CheckServerHealth();
                }
            }

            if (!string.IsNullOrWhiteSpace(serverStatusMessage))
            {
                EditorGUILayout.HelpBox(serverStatusMessage, serverStatusType);
            }
        }

        /// <summary>
        /// Credentials for <c>POST /artifacts</c>. Stored in EditorPrefs rather than the
        /// settings asset on purpose: the asset lives in Resources and would carry the
        /// secret into every player build.
        /// </summary>
        private void DrawUploadSettings()
        {
            EditorGUILayout.LabelField("Artifact upload", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Upload to Server (Bake section) pushes the baked navmesh over HTTP, so " +
                "the server does not have to share a file system with this machine.\n" +
                "A server bound to 127.0.0.1 accepts uploads from this machine without a token. " +
                "One listening on the network refuses them unless it was started with " +
                "--upload-token <secret> and the same secret is entered here.",
                MessageType.None);

            string token = NavigationServerUploadToken.Value;
            string edited = EditorGUILayout.PasswordField("Upload token", token);
            if (!string.Equals(edited, token, StringComparison.Ordinal))
            {
                NavigationServerUploadToken.Value = edited;
            }

            EditorGUILayout.LabelField(
                " ",
                "Kept on this machine only (EditorPrefs), never shipped in a build.",
                EditorStyles.miniLabel);
        }

        /// <summary>
        /// Keeps <c>Server Artifact Folder</c> honest. The installed server reads
        /// <c>NavigationServer/NavigationData</c>; when the setting points elsewhere the
        /// folder export silently writes where nobody reads, which is impossible to spot
        /// from the paths alone.
        /// </summary>
        private void DrawArtifactFolderTools(NavigationServerSettings settings)
        {
            bool installed = NavigationServerInstaller.IsInstalled;
            bool mismatched = installed && !string.Equals(
                settings.ServerArtifactFolder.TrimEnd('/'),
                NavigationServerInstaller.InstalledArtifactFolder,
                StringComparison.OrdinalIgnoreCase);

            if (mismatched)
            {
                EditorGUILayout.HelpBox(
                    $"The server is installed in '{NavigationServerInstaller.InstallFolderName}/' and " +
                    $"reads '{NavigationServerInstaller.InstalledArtifactFolder}', but Server Artifact " +
                    $"Folder points at '{settings.ServerArtifactFolder}'. Export to Folder would write " +
                    "where the running server never looks.",
                    MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!installed || !mismatched))
                {
                    if (GUILayout.Button("Use the installed server folder", EditorStyles.miniButton))
                    {
                        NavigationServerInstaller.PointArtifactFolderAtInstall();
                        artifactComparisons = null;
                    }
                }

                if (GUILayout.Button("Choose folder...", EditorStyles.miniButton))
                {
                    ChooseServerArtifactFolder(settings);
                }
            }
        }

        private void ChooseServerArtifactFolder(NavigationServerSettings settings)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            string current = NavigationArtifactBuilder.ResolveServerFolder();
            string picked = EditorUtility.OpenFolderPanel(
                "Server artifact folder",
                Directory.Exists(current) ? current : projectRoot,
                string.Empty);
            if (string.IsNullOrEmpty(picked) || projectRoot == null)
            {
                return;
            }

            // The setting is stored relative to the project so the asset stays portable
            // across machines; an outside folder has to keep its absolute path.
            string full = Path.GetFullPath(picked);
            string root = Path.GetFullPath(projectRoot);
            string value = full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                ? full.Substring(root.Length + 1).Replace('\\', '/')
                : full.Replace('\\', '/');

            var serialized = new SerializedObject(settings);
            SerializedProperty folder = serialized.FindProperty("serverArtifactFolder");
            if (folder == null)
            {
                return;
            }

            folder.stringValue = value;
            serialized.ApplyModifiedProperties();
            NavigationServerSettings.InvalidateCache();
            AssetDatabase.SaveAssets();
            artifactComparisons = null;
        }

        /// <summary>
        /// Install / run the bundled standalone server. It ships inside the package as
        /// <c>Server~</c>, which Unity ignores, so it must be copied into the project
        /// (outside Assets) before it can be built and started.
        /// </summary>
        private void DrawLocalServer(NavigationServerSettings settings)
        {
            EditorGUILayout.LabelField("Local server", EditorStyles.boldLabel);

            bool installed = NavigationServerInstaller.IsInstalled;
            bool running = NavigationServerProcess.IsRunning;

            if (!installed)
            {
                EditorGUILayout.HelpBox(
                    "The package ships the reference .NET navigation server, but it lives in a " +
                    "read-only package folder. Install it into the project to build and run it.\n" +
                    $"It will be copied to '{NavigationServerInstaller.InstallFolderName}/' next " +
                    "to Assets, so Unity never compiles its sources.",
                    MessageType.Info);

                if (GUILayout.Button("Install navigation server", GUILayout.Height(30f)))
                {
                    InstallLocalServer();
                }

                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawSummaryRow("Installed at", NavigationServerInstaller.InstallPath);
                DrawSummaryRow("Status", running
                    ? $"Running (pid {NavigationServerProcess.ProcessId})"
                    : "Stopped");
            }

            if (running)
            {
                EditorGUILayout.HelpBox(
                    "The server is running as a child process of the editor and writes to the " +
                    "Unity Console. It is stopped when you quit Unity.",
                    MessageType.None);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(running))
                {
                    if (GUILayout.Button("Start server", GUILayout.Height(26f)))
                    {
                        StartLocalServer();
                    }
                }

                using (new EditorGUI.DisabledScope(!running))
                {
                    if (GUILayout.Button("Stop server", GUILayout.Height(26f)))
                    {
                        NavigationServerProcess.Stop();
                        serverStatusMessage = "Navigation server stopped.";
                        serverStatusType = MessageType.None;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open server folder", EditorStyles.miniButton))
                {
                    EditorUtility.RevealInFinder(NavigationServerInstaller.InstallPath);
                }

                if (GUILayout.Button("Reinstall from package", EditorStyles.miniButton))
                {
                    if (EditorUtility.DisplayDialog(
                            "Reinstall navigation server",
                            "Overwrite the server sources with the copy from the package?\n\n" +
                            "Baked artifacts in NavigationData are kept.",
                            "Reinstall",
                            "Cancel"))
                    {
                        InstallLocalServer(overwrite: true);
                    }
                }
            }
        }

        private void InstallLocalServer(bool overwrite = false)
        {
            if (!NavigationServerInstaller.TryInstall(overwrite, out string path, out string error))
            {
                serverStatusMessage = error;
                serverStatusType = MessageType.Error;
                return;
            }

            NavigationServerInstaller.PointArtifactFolderAtInstall();
            artifactComparisons = null;

            if (!NavigationServerInstaller.IsDotnetAvailable(out string version))
            {
                serverStatusMessage =
                    $"Server installed at {path}, but the .NET SDK was not found on PATH. " +
                    "Install .NET 9 to build and run it.";
                serverStatusType = MessageType.Warning;
                return;
            }

            serverStatusMessage = $"Server installed at {path} (.NET SDK {version}).";
            serverStatusType = MessageType.Info;
        }

        private void StartLocalServer()
        {
            if (!NavigationServerProcess.TryStart(out string error))
            {
                serverStatusMessage = error;
                serverStatusType = MessageType.Error;
                return;
            }

            serverStatusMessage =
                "Server starting - the first launch also restores NuGet packages and compiles it, " +
                "so give it a few seconds before checking /health.";
            serverStatusType = MessageType.Info;
        }

        private void ApplyServerAddress(NavigationServerSettings settings)
        {
            Undo.RecordObject(settings, "Change Navigation Server Address");
            if (!settings.TryApplyUrl(serverAddressInput, out string error))
            {
                serverStatusMessage = error;
                serverStatusType = MessageType.Error;
                return;
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            NavigationServerSettings.InvalidateCache();
            serverAddressInput = settings.BaseUrl;
            serverStatusMessage = "Saved: " + settings.BaseUrl;
            serverStatusType = MessageType.Info;
            artifactComparisons = null;
        }

        private void CheckServerHealth()
        {
            serverRequestPending = true;
            serverStatusMessage = "Connecting to " + NavigationServerEditorClient.BaseUrl + "...";
            serverStatusType = MessageType.None;
            NavigationServerEditorClient.Get("/health", (success, payload) =>
            {
                if (this == null)
                {
                    return;
                }

                serverRequestPending = false;
                if (!success)
                {
                    serverStatusMessage = "Server unreachable: " + payload;
                    serverStatusType = MessageType.Error;
                }
                else if (NavigationServerEditorClient.TryParse(
                             payload,
                             out NavigationServerEditorClient.HealthResponse health))
                {
                    if (!string.Equals(health.status, "ok", StringComparison.OrdinalIgnoreCase))
                    {
                        // The server is up but has nothing to serve - a normal state
                        // before the first export, and its message says what to do.
                        serverStatusMessage =
                            $"Server is running, but no navigation is loaded.\n{health.message}";
                        serverStatusType = MessageType.Warning;
                    }
                    else
                    {
                        string levels = health.availableLevels != null && health.availableLevels.Length > 0
                            ? string.Join(", ", health.availableLevels)
                            : health.levelId;
                        serverStatusMessage =
                            $"OK: level={health.levelId}, artifact={NavigationArtifactIndex.Short(health.artifactHash)}, " +
                            $"polygons={health.navigationPolygons}, DotRecast {health.dotRecastVersion}.\n" +
                            $"Levels ready to serve: {levels}.";
                        serverStatusType = MessageType.Info;
                    }
                }
                else
                {
                    serverStatusMessage = "Unrecognized server response: " + payload;
                    serverStatusType = MessageType.Warning;
                }

                Repaint();
            });
        }

        private static void CreateServerSettings()
        {
            EnsureAssetFolder(NavigationServerSettings.ResourcesFolder);
            var settings = CreateInstance<NavigationServerSettings>();
            settings.name = NavigationServerSettings.ResourceName;
            AssetDatabase.CreateAsset(settings, NavigationServerSettings.AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            NavigationServerSettings.InvalidateCache();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }

        // ── Artifacts tab ─────────────────────────────────────────────────────
        private void DrawArtifacts()
        {
            EditorGUILayout.LabelField("Navigation maps", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "On the left are the maps built for the app (Generated/Navigation). " +
                "On the right is what actually sits on the navigation server. Diverging rows are " +
                "highlighted: re-upload them with the Export button.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(serverRequestPending))
                {
                    if (GUILayout.Button(
                            serverRequestPending ? "Querying the server..." : "Refresh from server",
                            GUILayout.Height(26f)))
                    {
                        RefreshArtifacts();
                    }
                }

                if (GUILayout.Button("Local folder only", GUILayout.Height(26f)))
                {
                    artifactComparisons = NavigationArtifactIndex.Compare(
                        NavigationArtifactIndex.ScanClientArtifacts(),
                        NavigationArtifactIndex.ScanLocalServerFolder(),
                        false);
                    artifactsStatusMessage =
                        "Showing the local server folder: " + NavigationArtifactBuilder.ResolveServerFolder();
                    artifactsStatusType = MessageType.None;
                }
            }

            if (!string.IsNullOrWhiteSpace(artifactsStatusMessage))
            {
                EditorGUILayout.HelpBox(artifactsStatusMessage, artifactsStatusType);
            }

            if (artifactComparisons == null)
            {
                EditorGUILayout.HelpBox(
                    "Press \"Refresh from server\" to compare the map lists.",
                    MessageType.None);
                return;
            }

            if (artifactComparisons.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "There are no built maps. Build the navigation in the Bake section.",
                    MessageType.Warning);
                return;
            }

            int inSync = 0;
            for (int i = 0; i < artifactComparisons.Count; i++)
            {
                if (artifactComparisons[i].State == NavigationArtifactSyncState.InSync)
                {
                    inSync++;
                }
            }

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    $"In sync: {inSync} of {artifactComparisons.Count}",
                    EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(inSync == artifactComparisons.Count))
                {
                    if (GUILayout.Button(
                            new GUIContent(
                                "Sync all",
                                "Upload every diverging map to the server."),
                            EditorStyles.miniButton,
                            GUILayout.Width(80f)))
                    {
                        pendingAction = SyncAllArtifacts;
                    }
                }
            }

            for (int i = 0; i < artifactComparisons.Count; i++)
            {
                DrawArtifactRow(artifactComparisons[i]);
            }
        }

        /// <summary>Uploads every map that is missing on the server or outdated there.</summary>
        private void SyncAllArtifacts()
        {
            var pending = new List<NavigationArtifactAsset>();
            for (int i = 0; i < artifactComparisons.Count; i++)
            {
                NavigationArtifactComparison row = artifactComparisons[i];
                if (row.HasClient && row.State != NavigationArtifactSyncState.InSync)
                {
                    pending.Add(row.ClientAsset);
                }
            }

            if (pending.Count == 0)
            {
                return;
            }

            using var progress = new NavigationBuildProgress("Sync navigation artifacts", pending.Count);
            int exported = 0;
            try
            {
                for (int i = 0; i < pending.Count; i++)
                {
                    progress.Stage($"Exporting {pending[i].LevelId}");
                    // Only the last map is marked active so the active manifest is not
                    // overwritten on every iteration.
                    NavigationArtifactBuilder.ExportForServer(pending[i], i == pending.Count - 1);
                    exported++;
                }

                artifactsStatusMessage =
                    $"Synchronized maps: {exported} in {progress.ElapsedSeconds:0.0} s. " +
                    "Written to the server artifact folder - a server reading another " +
                    "folder needs Upload to Server instead.";
                artifactsStatusType = MessageType.Info;
            }
            catch (NavigationBuildCanceledException)
            {
                artifactsStatusMessage = $"Synchronization canceled. Uploaded so far: {exported}.";
                artifactsStatusType = MessageType.Warning;
            }
            catch (Exception exception)
            {
                artifactsStatusMessage = "Sync all failed: " + exception.Message;
                artifactsStatusType = MessageType.Error;
                Debug.LogException(exception);
            }

            artifactComparisons = null;
        }

        private void DrawArtifactRow(NavigationArtifactComparison row)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(row.LevelId, EditorStyles.boldLabel, GUILayout.Width(200f));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        DescribeState(row.State),
                        EditorStyles.miniBoldLabel,
                        GUILayout.Width(180f));
                }

                DrawSummaryRow(
                    "Client",
                    row.HasClient
                        ? $"{NavigationArtifactIndex.Short(row.ClientHash)} · {row.ClientPolygonCount} polygons"
                        : "not in the build");
                DrawSummaryRow(
                    "Server",
                    row.ServerHasLevel
                        ? $"{NavigationArtifactIndex.Short(row.ServerHash)} · {row.ServerPolygonCount} polygons" +
                          (row.ServerActive ? " · active" : string.Empty) +
                          (row.ServerLoaded ? " · loaded" : string.Empty)
                        : "not on the server");

                if (!string.IsNullOrWhiteSpace(row.Details))
                {
                    EditorGUILayout.HelpBox(row.Details, ToMessageType(row.State));
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!row.HasClient))
                    {
                        using (new EditorGUI.DisabledScope(serverRequestPending))
                        {
                            if (GUILayout.Button("Upload to Server", EditorStyles.miniButton))
                            {
                                UploadArtifact(row.ClientAsset);
                            }
                        }

                        if (GUILayout.Button("Export to Folder", EditorStyles.miniButton))
                        {
                            ExportArtifact(row.ClientAsset);
                        }

                        if (GUILayout.Button("Select client asset", EditorStyles.miniButton))
                        {
                            Selection.activeObject = row.ClientAsset;
                            EditorGUIUtility.PingObject(row.ClientAsset);
                        }
                    }
                }
            }
        }

        /// <summary>Pushes one map to the running server over HTTP.</summary>
        private void UploadArtifact(NavigationArtifactAsset asset)
        {
            serverRequestPending = true;
            artifactsStatusMessage = "Uploading " + asset.LevelId + "...";
            artifactsStatusType = MessageType.None;
            NavigationServerUploader.Upload(asset, true, (success, message) =>
            {
                if (this == null)
                {
                    return;
                }

                serverRequestPending = false;
                artifactsStatusMessage = success ? message : "Upload failed: " + message;
                artifactsStatusType = success ? MessageType.Info : MessageType.Error;
                if (success)
                {
                    RefreshArtifacts();
                }

                Repaint();
            });
        }

        private void ExportArtifact(NavigationArtifactAsset asset)
        {
            try
            {
                NavigationServerExportResult export = NavigationArtifactBuilder.ExportForServer(asset);
                artifactsStatusMessage =
                    $"Written to the server folder: {export.LevelId} {NavigationArtifactIndex.Short(export.Hash)}\n" +
                    export.ServerDataPath +
                    "\nThis only reaches a server that reads that very folder - " +
                    "use Upload to Server for a remote one.";
                artifactsStatusType = MessageType.Info;
                RefreshArtifacts();
            }
            catch (Exception exception)
            {
                artifactsStatusMessage = "Export failed: " + exception.Message;
                artifactsStatusType = MessageType.Error;
                Debug.LogException(exception, asset);
            }
        }

        private void RefreshArtifacts()
        {
            serverRequestPending = true;
            artifactsStatusMessage = "Requesting the map list from " + NavigationServerEditorClient.BaseUrl + "...";
            artifactsStatusType = MessageType.None;
            List<NavigationArtifactAsset> clientArtifacts = NavigationArtifactIndex.ScanClientArtifacts();

            NavigationServerEditorClient.Get("/artifacts", (success, payload) =>
            {
                if (this == null)
                {
                    return;
                }

                serverRequestPending = false;
                if (success
                    && NavigationServerEditorClient.TryParse(
                        payload,
                        out NavigationServerEditorClient.ArtifactsResponse response))
                {
                    artifactComparisons = NavigationArtifactIndex.Compare(clientArtifacts, response, true);
                    artifactsStatusMessage =
                        $"Server {NavigationServerEditorClient.BaseUrl}: loaded " +
                        $"{response.loadedLevelId} · {NavigationArtifactIndex.Short(response.loadedArtifactHash)}. " +
                        $"Folder: {response.dataDirectory}";
                    artifactsStatusType = MessageType.Info;
                }
                else
                {
                    artifactComparisons = NavigationArtifactIndex.Compare(
                        clientArtifacts,
                        NavigationArtifactIndex.ScanLocalServerFolder(),
                        false);
                    artifactsStatusMessage =
                        "The server is unreachable, showing the local folder " +
                        NavigationArtifactBuilder.ResolveServerFolder() +
                        (success ? "." : ".\n" + payload);
                    artifactsStatusType = MessageType.Warning;
                }

                Repaint();
            });
        }

        private static string DescribeState(NavigationArtifactSyncState state)
        {
            return state switch
            {
                NavigationArtifactSyncState.InSync => "OK in sync",
                NavigationArtifactSyncState.ServerOutdated => "Outdated on the server",
                NavigationArtifactSyncState.MissingOnServer => "Missing on the server",
                NavigationArtifactSyncState.MissingInClient => "Server only",
                _ => "Corrupted"
            };
        }

        private static MessageType ToMessageType(NavigationArtifactSyncState state)
        {
            return state switch
            {
                NavigationArtifactSyncState.InSync => MessageType.Info,
                NavigationArtifactSyncState.MissingOnServer => MessageType.Error,
                NavigationArtifactSyncState.Broken => MessageType.Error,
                _ => MessageType.Warning
            };
        }

        /// <summary>
        /// One-Click Setup: creates a level that already works -
        /// with a meaningful Level ID, profiles and groups to organize the scene.
        /// </summary>
        private void CreateLevelSetup()
        {
            EnsureAssetFolder(GeneratedSettingsFolder);
            string sceneKey = GetSceneKey();

            var root = new GameObject("Navigation Level");
            Undo.RegisterCreatedObjectUndo(root, "Create Navigation Level Setup");
            NavigationLevel level = Undo.AddComponent<NavigationLevel>(root);

            NavigationProjectSettings projectSettings = NavigationProjectSettings.instance;
            NavigationAgentProfile agent;
            NavigationAreaCatalog areas;
            NavigationPerformanceProfile performance;
            if (projectSettings.HasAllProfileDefaults)
            {
                agent = projectSettings.DefaultAgentProfile;
                areas = projectSettings.DefaultAreaCatalog;
                performance = projectSettings.DefaultPerformanceProfile;
            }
            else
            {
                agent = CreateAsset<NavigationAgentProfile>(
                    $"{GeneratedSettingsFolder}/{sceneKey}_Agent.asset");
                areas = CreateAsset<NavigationAreaCatalog>(
                    $"{GeneratedSettingsFolder}/{sceneKey}_Areas.asset");
                areas.ResetToDefaults();
                EditorUtility.SetDirty(areas);
                performance = CreatePerformanceProfile(sceneKey);
            }

            Undo.RecordObject(level, "Assign Navigation Profiles");
            level.ConfigureDefaults(agent, areas, performance);
            JsonUtility.FromJsonOverwrite(
                JsonUtility.ToJson(projectSettings.DefaultBuildSettings),
                level.BuildSettings);

            // Level id and description are derived from the scene name so the designer
            // does not have to invent them from scratch.
            Transform geometryRoot = CreateGroup(root.transform, "NavigationGeometry");
            CreateGroup(root.transform, "NavigationModifiers");
            CreateGroup(root.transform, "NavigationLinks");
            CreateGroup(root.transform, "NavigationTestPoints");

            var levelObject = new SerializedObject(level);
            levelObject.Update();
            levelObject.FindProperty("levelId").stringValue = sceneKey;
            levelObject.FindProperty("description").stringValue =
                $"Navigation for the {SceneManager.GetActiveScene().name} scene.";
            levelObject.FindProperty("geometryRoot").objectReferenceValue = geometryRoot;
            levelObject.ApplyModifiedProperties();

            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();

            selectedLevel = level;
            Selection.activeGameObject = root;
            MarkSceneChanged();
            RunValidation();
        }

        private static Transform CreateGroup(Transform parent, string groupName)
        {
            var group = new GameObject(groupName);
            Undo.RegisterCreatedObjectUndo(group, "Create Navigation Group");
            Undo.SetTransformParent(group.transform, parent, "Create Navigation Group");
            group.transform.localPosition = Vector3.zero;
            group.transform.localRotation = Quaternion.identity;
            group.transform.localScale = Vector3.one;
            return group.transform;
        }

        private NavigationPerformanceProfile CreatePerformanceProfile(string sceneKey)
        {
            EnsureAssetFolder(GeneratedSettingsFolder);
            NavigationPerformanceProfile profile = CreateAsset<NavigationPerformanceProfile>(
                $"{GeneratedSettingsFolder}/{sceneKey}_MobilePerformance.asset");
            profile.ApplyStartingPreset(NavigationDeviceTier.MobileMedium);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private void AssignPerformanceProfile(NavigationPerformanceProfile profile)
        {
            var levelObject = new SerializedObject(selectedLevel);
            levelObject.Update();
            levelObject.FindProperty("performanceProfile").objectReferenceValue = profile;
            levelObject.ApplyModifiedProperties();
            MarkSceneChanged();
            MarkValidationStale();
        }

        private static T CreateAsset<T>(string suggestedPath) where T : ScriptableObject
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(suggestedPath);
            T asset = CreateInstance<T>();
            asset.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
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

        private static string GetSceneKey()
        {
            Scene scene = SceneManager.GetActiveScene();
            string sceneName = string.IsNullOrWhiteSpace(scene.name) ? "UnsavedScene" : scene.name;
            return NavigationIdUtility.Sanitize(sceneName, "level");
        }

        private void TrySelectLevelFromContext()
        {
            if (Selection.activeGameObject != null)
            {
                NavigationLevel selected = Selection.activeGameObject.GetComponentInParent<NavigationLevel>();
                if (selected != null)
                {
                    selectedLevel = selected;
                    return;
                }
            }

            NavigationLevel[] levels = FindSceneLevels();
            if (selectedLevel == null && levels.Length == 1)
            {
                selectedLevel = levels[0];
            }
        }

        private static NavigationLevel[] FindSceneLevels()
        {
            NavigationLevel[] allLevels = Resources.FindObjectsOfTypeAll<NavigationLevel>();
            var sceneLevels = new List<NavigationLevel>();
            for (int i = 0; i < allLevels.Length; i++)
            {
                NavigationLevel level = allLevels[i];
                if (level != null
                    && !EditorUtility.IsPersistent(level)
                    && level.gameObject.scene.IsValid()
                    && level.gameObject.scene.isLoaded)
                {
                    sceneLevels.Add(level);
                }
            }

            return sceneLevels.ToArray();
        }

        private void OnSelectionChanged()
        {
            // Changing the selection only switches the active level.
            // Validation does NOT run here - it is a manual operation.
            NavigationLevel previous = selectedLevel;
            TrySelectLevelFromContext();
            if (previous != selectedLevel)
            {
                validationReport = NavigationValidationReport.NotEvaluated;
                validationStale = false;
            }

            Repaint();
        }

        /// <summary>
        /// A cheap "snapshot is stale" flag. It recomputes nothing:
        /// the user decides when to press Validate.
        /// </summary>
        private void MarkValidationStale()
        {
            validationStale = validationReport.Evaluated;
        }

        private void MarkSceneChanged()
        {
            if (selectedLevel != null && selectedLevel.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(selectedLevel.gameObject.scene);
            }
        }
    }
}
