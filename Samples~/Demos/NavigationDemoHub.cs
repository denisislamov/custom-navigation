using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace CustomNavigation.Runtime
{
    [Serializable]
    public sealed class NavigationDemoSceneEntry
    {
        [SerializeField, Tooltip("Name of the level card in the start catalog.")]
        private string title;
        [SerializeField, Tooltip("Name of the Unity scene from Build Settings to load.")]
        private string sceneName;
        [SerializeField, TextArea(2, 5), Tooltip("Short description of the purpose and the navigation scenario under test.")]
        private string description;
        [SerializeField, Tooltip("Short mode label: LocalOnly, ServerOnly or HybridPredicted.")]
        private string mode;
        [SerializeField, Tooltip("Show a warning that a standalone server is required before launching.")]
        private bool serverRequired;

        public string Title => title;
        public string SceneName => sceneName;
        public string Description => description;
        public string Mode => mode;
        public bool ServerRequired => serverRequired;

        public NavigationDemoSceneEntry(
            string entryTitle,
            string entrySceneName,
            string entryDescription,
            string entryMode,
            bool requiresServer)
        {
            title = entryTitle;
            sceneName = entrySceneName;
            description = entryDescription;
            mode = entryMode;
            serverRequired = requiresServer;
        }
    }

    [DisallowMultipleComponent]
    public sealed class NavigationDemoHub : MonoBehaviour
    {
        [SerializeField, Tooltip("Title of the start level.")]
        private string title = "Custom Navigation / DotRecast";
        [SerializeField, TextArea(3, 7), Tooltip("Catalog description shown to the user before picking a level.")]
        private string description;
        [SerializeField, Tooltip("Scenes that can be launched from the start level.")]
        private NavigationDemoSceneEntry[] scenes = Array.Empty<NavigationDemoSceneEntry>();

        private Vector2 scrollPosition;
        private string status;
        private string serverAddressInput;
        private string serverConnectionStatus;
        private bool serverCheckPending;
        private GUIStyle titleStyle;
        private GUIStyle descriptionStyle;
        private GUIStyle cardTitleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle modeStyle;
        private GUIStyle serverStyle;
        private GUIStyle serverAddressStyle;
        private GUIStyle actionButtonStyle;

        public string Description => description;
        public NavigationDemoSceneEntry[] Scenes => scenes;

        public void Configure(
            string hubTitle,
            string hubDescription,
            NavigationDemoSceneEntry[] entries)
        {
            title = hubTitle;
            description = hubDescription;
            scenes = entries ?? Array.Empty<NavigationDemoSceneEntry>();
        }

        private void Awake()
        {
            scenes ??= Array.Empty<NavigationDemoSceneEntry>();
            serverAddressInput = NavigationServerRuntimeSettings.CurrentUrl;
            serverConnectionStatus = "The address persists between client launches.";
            CreateCamera();
        }

        private void OnGUI()
        {
            EnsureStyles();
            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.025f, 0.04f, 1f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;

            using (NavigationDemoGuiScope gui = NavigationDemoPresentation.BeginSafeAreaGui())
            {
                float contentWidth = Mathf.Min(920f, gui.Width);
                float left = (gui.Width - contentWidth) * 0.5f;
                var viewport = new Rect(left, 0f, contentWidth, gui.Height);
                var content = new Rect(
                    0f,
                    0f,
                    contentWidth - 16f,
                    CalculateContentHeight(gui.IsNarrow));
                scrollPosition = GUI.BeginScrollView(viewport, scrollPosition, content);

                float y = 0f;
                GUI.Label(new Rect(0f, y, content.width, 42f), title, titleStyle);
                y += 48f;
                float descriptionHeight = gui.IsNarrow ? 110f : 68f;
                GUI.Label(new Rect(0f, y, content.width, descriptionHeight), description, descriptionStyle);
                y += descriptionHeight + 12f;
                DrawServerAddressPanel(content.width, gui.IsNarrow, ref y);

                for (int i = 0; i < scenes.Length; i++)
                {
                    NavigationDemoSceneEntry entry = scenes[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    DrawCard(entry, content.width, gui.IsNarrow, ref y);
                }

                if (!string.IsNullOrWhiteSpace(status))
                {
                    GUI.Label(new Rect(0f, y + 4f, content.width, 42f), status, serverStyle);
                }

                GUI.EndScrollView();
            }
        }

        private void DrawServerAddressPanel(float width, bool narrow, ref float y)
        {
            float panelHeight = narrow ? 178f : 112f;
            GUI.Box(new Rect(0f, y, width, panelHeight), GUIContent.none);
            GUI.Label(new Rect(16f, y + 10f, width - 32f, 28f), "Navigation server", cardTitleStyle);

            if (narrow)
            {
                GUI.Label(
                    new Rect(16f, y + 39f, width - 32f, 22f),
                    "On a phone, enter the Wi-Fi IP of the computer and the port.",
                    bodyStyle);
                serverAddressInput = GUI.TextField(
                    new Rect(16f, y + 65f, width - 32f, 34f),
                    serverAddressInput,
                    serverAddressStyle);
                float buttonWidth = (width - 40f) * 0.5f;
                if (GUI.Button(
                        new Rect(16f, y + 106f, buttonWidth, 34f),
                        "Save",
                        actionButtonStyle))
                {
                    SaveServerAddress(false);
                }

                GUI.enabled = !serverCheckPending;
                if (GUI.Button(
                        new Rect(24f + buttonWidth, y + 106f, buttonWidth, 34f),
                        serverCheckPending ? "Checking..." : "Check /health",
                        actionButtonStyle))
                {
                    SaveServerAddress(true);
                }
                GUI.enabled = true;
                GUI.Label(
                    new Rect(16f, y + 145f, width - 32f, 24f),
                    serverConnectionStatus,
                    serverStyle);
                y += panelHeight + 12f;
                return;
            }

            GUI.Label(
                new Rect(16f, y + 38f, 250f, 22f),
                "Computer IP on the Wi-Fi network:",
                bodyStyle);
            serverAddressInput = GUI.TextField(
                new Rect(266f, y + 34f, width - 536f, 34f),
                serverAddressInput,
                serverAddressStyle);
            if (GUI.Button(
                    new Rect(width - 254f, y + 34f, 106f, 34f),
                    "Save",
                    actionButtonStyle))
            {
                SaveServerAddress(false);
            }

            GUI.enabled = !serverCheckPending;
            if (GUI.Button(
                    new Rect(width - 140f, y + 34f, 124f, 34f),
                    serverCheckPending ? "Checking..." : "Check",
                    actionButtonStyle))
            {
                SaveServerAddress(true);
            }
            GUI.enabled = true;
            GUI.Label(
                new Rect(16f, y + 76f, width - 32f, 24f),
                serverConnectionStatus,
                serverStyle);
            y += panelHeight + 12f;
        }

        private void SaveServerAddress(bool checkConnection)
        {
            if (!NavigationServerRuntimeSettings.TrySave(
                    serverAddressInput,
                    out string normalizedUrl,
                    out string error))
            {
                serverConnectionStatus = "ERROR: " + error;
                return;
            }

            serverAddressInput = normalizedUrl;
            serverConnectionStatus = "Saved: " + normalizedUrl;
            if (checkConnection)
            {
                StartCoroutine(CheckServerConnection(normalizedUrl));
            }
        }

        private IEnumerator CheckServerConnection(string baseUrl)
        {
            serverCheckPending = true;
            serverConnectionStatus = "Connecting to " + baseUrl + "...";
            using (UnityWebRequest request = UnityWebRequest.Get(baseUrl + "/health"))
            {
                request.timeout = 4;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    serverConnectionStatus = "SERVER ONLINE: " + baseUrl;
                    Debug.Log(
                        $"[CustomNavigation] Navigation server health check passed: {baseUrl}.",
                        this);
                }
                else
                {
                    serverConnectionStatus = "SERVER UNREACHABLE: " + request.error;
                    Debug.LogWarning(
                        $"[CustomNavigation] Navigation server health check failed for " +
                        $"{baseUrl}: {request.error}.",
                        this);
                }
            }

            serverCheckPending = false;
        }

        private void DrawCard(NavigationDemoSceneEntry entry, float width, bool narrow, ref float y)
        {
            float cardHeight = narrow ? 218f : 132f;
            GUI.Box(new Rect(0f, y, width, cardHeight), GUIContent.none);
            if (narrow)
            {
                GUI.Label(new Rect(16f, y + 12f, width - 32f, 30f), entry.Title, cardTitleStyle);
                GUI.Label(new Rect(16f, y + 46f, Mathf.Min(220f, width - 32f), 27f), entry.Mode, modeStyle);
                GUI.Label(new Rect(16f, y + 80f, width - 32f, 68f), entry.Description, bodyStyle);
                if (entry.ServerRequired)
                {
                    GUI.Label(new Rect(16f, y + 148f, width - 32f, 26f), "SERVER REQUIRED", serverStyle);
                }

                if (GUI.Button(new Rect(16f, y + 174f, width - 32f, 36f), "Launch level"))
                {
                    OpenScene(entry);
                }

                y += cardHeight + 12f;
                return;
            }

            GUI.Label(new Rect(18f, y + 13f, width - 230f, 30f), entry.Title, cardTitleStyle);
            GUI.Label(new Rect(width - 205f, y + 13f, 185f, 27f), entry.Mode, modeStyle);
            GUI.Label(new Rect(18f, y + 48f, width - 238f, 66f), entry.Description, bodyStyle);

            if (entry.ServerRequired)
            {
                GUI.Label(new Rect(width - 212f, y + 50f, 196f, 28f), "SERVER REQUIRED", serverStyle);
            }

            if (GUI.Button(new Rect(width - 212f, y + 84f, 196f, 34f), "Launch level"))
            {
                OpenScene(entry);
            }

            y += cardHeight + 12f;
        }

        private void OpenScene(NavigationDemoSceneEntry entry)
        {
            if (!Application.CanStreamedLevelBeLoaded(entry.SceneName))
            {
                status = $"Scene '{entry.SceneName}' is missing or disabled in Build Settings.";
                Debug.LogError("[CustomNavigation] " + status, this);
                return;
            }

            string hubSceneName = gameObject.scene.name;
            NavigationDemoHubReturn.Install(hubSceneName);
            SceneManager.LoadScene(entry.SceneName, LoadSceneMode.Single);
        }

        private float CalculateContentHeight(bool narrow)
        {
            float headerHeight = narrow ? 360f : 252f;
            float cardHeight = narrow ? 230f : 144f;
            return headerHeight + scenes.Length * cardHeight + 52f;
        }

        private void CreateCamera()
        {
            var cameraObject = new GameObject("Demo Hub Camera");
            cameraObject.transform.SetParent(transform, false);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.025f, 0.04f, 1f);
            camera.cullingMask = 0;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            descriptionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = true,
                normal = { textColor = new Color(0.73f, 0.82f, 0.9f) }
            };
            cardTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.92f, 0.97f, 1f) }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = new Color(0.73f, 0.8f, 0.87f) }
            };
            modeStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.25f, 1f, 0.72f) }
            };
            serverStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.68f, 0.2f) }
            };
            serverAddressStyle = new GUIStyle(GUI.skin.textField)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 14,
                padding = new RectOffset(10, 10, 4, 4)
            };
            actionButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
        }
    }

    [DisallowMultipleComponent]
    public sealed class NavigationDemoHubReturn : MonoBehaviour
    {
        private string hubSceneName;
        private GUIStyle buttonStyle;

        public static void Install(string sceneName)
        {
            var overlay = new GameObject("Return To Navigation Demo Hub");
            NavigationDemoHubReturn value = overlay.AddComponent<NavigationDemoHubReturn>();
            value.hubSceneName = sceneName;
            DontDestroyOnLoad(overlay);
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                ReturnToHub();
            }
        }

        private void OnGUI()
        {
            buttonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            using (NavigationDemoGuiScope gui = NavigationDemoPresentation.BeginSafeAreaGui())
            {
                if (GUI.Button(
                        new Rect(0f, gui.Height - 44f, Mathf.Min(260f, gui.Width), 44f),
                        "< Back to the level catalog",
                        buttonStyle))
                {
                    ReturnToHub();
                }
            }
        }

        private void ReturnToHub()
        {
            string target = hubSceneName;
            Destroy(gameObject);
            SceneManager.LoadScene(target, LoadSceneMode.Single);
        }
    }
}
