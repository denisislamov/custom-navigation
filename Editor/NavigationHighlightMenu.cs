using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    internal static class NavigationHighlightMenu
    {
        private const string MenuPath = "Tools/Custom Navigation/Navigation Highlight";

        [MenuItem(MenuPath, priority = 101)]
        private static void ToggleHighlight()
        {
            NavigationHighlightSettings.Enabled = !NavigationHighlightSettings.Enabled;
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, validate = true)]
        private static bool ValidateToggleHighlight()
        {
            Menu.SetChecked(MenuPath, NavigationHighlightSettings.Enabled);
            return true;
        }
    }
}
