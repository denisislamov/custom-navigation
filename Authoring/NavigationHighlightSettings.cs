using System;
using UnityEngine;

namespace CustomNavigation.Authoring
{
    /// <summary>
    /// Single switch for the navigation highlight: the navmesh overlay in the Scene View
    /// and every authoring gizmo. The value lives in EditorPrefs, so it is shared
    /// by every scene in the project and survives an editor restart.
    /// </summary>
    public static class NavigationHighlightSettings
    {
        public const string EnabledPreferenceKey = "CustomNavigation.NavigationHighlight.Enabled";
        public const bool DefaultEnabled = true;

        private static bool enabledValue = DefaultEnabled;
        private static bool loaded;

        public static event Action Changed;

        public static bool Enabled
        {
            get
            {
                EnsureLoaded();
                return enabledValue;
            }
            set
            {
                EnsureLoaded();
                if (enabledValue == value)
                {
                    return;
                }

                enabledValue = value;
#if UNITY_EDITOR
                UnityEditor.EditorPrefs.SetBool(EnabledPreferenceKey, value);
#endif
                Changed?.Invoke();
            }
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            loaded = true;
#if UNITY_EDITOR
            enabledValue = UnityEditor.EditorPrefs.GetBool(EnabledPreferenceKey, DefaultEnabled);
#endif
        }
    }

    /// <summary>
    /// Shared highlight palette so that the navmesh overlay and the authoring gizmos
    /// use consistent colors.
    /// </summary>
    public static class NavigationHighlightPalette
    {
        public static readonly Color Include = new Color(0.2f, 0.8f, 1f, 0.75f);
        public static readonly Color Block = new Color(1f, 0.2f, 0.12f, 0.75f);
        public static readonly Color Ignore = new Color(0.55f, 0.55f, 0.6f, 0.4f);
        public static readonly Color Link = new Color(1f, 0.75f, 0.1f, 0.9f);
        public static readonly Color PortalOpen = new Color(0.35f, 1f, 0.55f, 0.9f);
        public static readonly Color PortalClosed = new Color(1f, 0.4f, 0.3f, 0.9f);
        public static readonly Color TestPointRequired = new Color(1f, 0.85f, 0.15f, 0.9f);
        public static readonly Color TestPointOptional = new Color(0.5f, 0.8f, 1f, 0.7f);
        public static readonly Color LevelBounds = new Color(0.4f, 0.85f, 1f, 0.5f);
        public static readonly Color NavigationMeshFallback = new Color(0.1f, 0.75f, 0.5f, 1f);
        public static readonly Color NavigationMeshEdge = new Color(0.03f, 0.16f, 0.12f, 0.85f);
        public static readonly Color NavigationMeshBoundary = new Color(0.05f, 0.05f, 0.08f, 1f);

        public const float NavigationMeshFillAlpha = 0.32f;

        public static Color ForGeometryMode(NavigationGeometryMode mode)
        {
            switch (mode)
            {
                case NavigationGeometryMode.Block:
                    return Block;
                case NavigationGeometryMode.Ignore:
                    return Ignore;
                default:
                    return Include;
            }
        }
    }
}
