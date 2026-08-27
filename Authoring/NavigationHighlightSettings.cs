using System;
using UnityEngine;

namespace CustomNavigation.Authoring
{
    public enum NavigationPreviewScope
    {
        ActiveLevel,
        Selection,
        AllLoadedLevels
    }

    public enum NavigationPreviewDepth
    {
        Visible,
        XRay
    }

    /// <summary>
    /// Personal Scene View preview state. The overlay and Preferences page are two views of
    /// these EditorPrefs values; reading them never creates or modifies project assets.
    /// </summary>
    public static class NavigationHighlightSettings
    {
        public const string EnabledPreferenceKey = "CustomNavigation.NavigationHighlight.Enabled";
        public const string SourcesPreferenceKey = "CustomNavigation.ScenePreview.Sources";
        public const string BakedPreferenceKey = "CustomNavigation.ScenePreview.Baked";
        public const string RuntimePreferenceKey = "CustomNavigation.ScenePreview.Runtime";
        public const string ScopePreferenceKey = "CustomNavigation.ScenePreview.Scope";
        public const string DepthPreferenceKey = "CustomNavigation.ScenePreview.Depth";
        public const bool DefaultEnabled = true;

        public static event Action Changed;

        public static bool Enabled
        {
            get => SourcesEnabled || BakedEnabled || RuntimeEnabled;
            set
            {
                bool changed = SourcesEnabled != value || BakedEnabled != value || RuntimeEnabled != value;
                SetBool(SourcesPreferenceKey, value);
                SetBool(BakedPreferenceKey, value);
                SetBool(RuntimePreferenceKey, value);
#if UNITY_EDITOR
                UnityEditor.EditorPrefs.SetBool(EnabledPreferenceKey, value);
#endif
                if (changed)
                {
                    Changed?.Invoke();
                }
            }
        }

        public static bool SourcesEnabled
        {
            get => GetBool(SourcesPreferenceKey, LegacyDefault);
            set => SetLayer(SourcesPreferenceKey, value);
        }

        public static bool BakedEnabled
        {
            get => GetBool(BakedPreferenceKey, LegacyDefault);
            set => SetLayer(BakedPreferenceKey, value);
        }

        public static bool RuntimeEnabled
        {
            get => GetBool(RuntimePreferenceKey, LegacyDefault);
            set => SetLayer(RuntimePreferenceKey, value);
        }

        public static NavigationPreviewScope Scope
        {
            get => GetEnum(ScopePreferenceKey, NavigationPreviewScope.ActiveLevel);
            set => SetEnum(ScopePreferenceKey, value);
        }

        public static NavigationPreviewDepth Depth
        {
            get => GetEnum(DepthPreferenceKey, NavigationPreviewDepth.Visible);
            set => SetEnum(DepthPreferenceKey, value);
        }

        private static bool LegacyDefault
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetBool(EnabledPreferenceKey, DefaultEnabled);
#else
                return DefaultEnabled;
#endif
            }
        }

        private static bool GetBool(string key, bool fallback)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetBool(key, fallback);
#else
            return fallback;
#endif
        }

        private static void SetLayer(string key, bool value)
        {
            if (GetBool(key, LegacyDefault) == value)
            {
                return;
            }

            SetBool(key, value);
            Changed?.Invoke();
        }

        private static void SetBool(string key, bool value)
        {
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetBool(key, value);
#endif
        }

        private static T GetEnum<T>(string key, T fallback) where T : struct
        {
#if UNITY_EDITOR
            int stored = UnityEditor.EditorPrefs.GetInt(key, Convert.ToInt32(fallback));
            return Enum.IsDefined(typeof(T), stored) ? (T)(object)stored : fallback;
#else
            return fallback;
#endif
        }

        private static void SetEnum<T>(string key, T value) where T : struct
        {
            if (Equals(GetEnum(key, default(T)), value))
            {
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetInt(key, Convert.ToInt32(value));
#endif
            Changed?.Invoke();
        }
    }

    /// <summary>Muted palette reserved for Custom Navigation preview layers.</summary>
    public static class NavigationHighlightPalette
    {
        public static readonly Color Sources = FromHex(0xC5AC83, 0.95f);
        public static readonly Color Baked = FromHex(0x9375A3, 1f);
        public static readonly Color Runtime = FromHex(0xB49BC2, 1f);
        public static readonly Color Changed = FromHex(0xA87945, 1f);
        public static readonly Color Error = FromHex(0x684779, 1f);
        public static readonly Color ErrorBackdrop = FromHex(0xF0DEB8, 1f);

        public static readonly Color Include = Sources;
        public static readonly Color Block = Changed;
        public static readonly Color Ignore = FromHex(0xC5AC83, 0.42f);
        public static readonly Color Link = FromHex(0xB49BC2, 0.95f);
        public static readonly Color PortalOpen = FromHex(0xC5AC83, 0.95f);
        public static readonly Color PortalClosed = FromHex(0x684779, 0.95f);
        public static readonly Color TestPointRequired = FromHex(0xA87945, 0.95f);
        public static readonly Color TestPointOptional = FromHex(0xB49BC2, 0.78f);
        public static readonly Color LevelBounds = FromHex(0xC5AC83, 0.72f);
        public static readonly Color NavigationMeshFallback = Baked;
        public static readonly Color NavigationMeshEdge = FromHex(0x9375A3, 0.96f);
        public static readonly Color NavigationMeshBoundary = FromHex(0x684779, 1f);

        public const float NavigationMeshFillAlpha = 0.22f;

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

        private static Color FromHex(int rgb, float alpha)
        {
            return new Color(
                ((rgb >> 16) & 0xff) / 255f,
                ((rgb >> 8) & 0xff) / 255f,
                (rgb & 0xff) / 255f,
                alpha);
        }
    }
}
