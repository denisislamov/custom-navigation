using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace CustomNavigation.Runtime
{
    /// <summary>
    /// Persistent overlay that takes the player back to the demo hub scene.
    ///
    /// This class must stay in a file named exactly like the type. Unity only creates a
    /// MonoScript for the class whose name matches the file, so a MonoBehaviour declared
    /// in a differently named file cannot be instantiated - AddComponent returns null and
    /// logs "The referenced script (Unknown) on this Behaviour is missing!".
    /// </summary>
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

