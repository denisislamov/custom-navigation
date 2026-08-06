using CustomNavigation.Authoring;
using UnityEngine;

namespace CustomNavigation.Runtime
{
    /// <summary>
    /// Runtime access to the navigation server address.
    /// The base value always comes from <see cref="NavigationServerSettings"/> (an asset in Resources),
    /// while PlayerPrefs only stores a temporary override typed inside the build itself (for example on a phone).
    /// As soon as the address in the ScriptableObject changes the override is cleared: otherwise an old value
    /// on the device would silently survive any settings change.
    /// </summary>
    public static class NavigationServerRuntimeSettings
    {
        private const string OverrideKey = "CustomNavigation.ServerUrl";
        private const string OverrideBaselineKey = "CustomNavigation.ServerUrl.Baseline";

        public static string DefaultUrl => NavigationServerSettings.ResolveBaseUrl();

        /// <summary>Address from the ScriptableObject, taking the runtime override into account.</summary>
        public static string CurrentUrl
        {
            get
            {
                string assetUrl = DefaultUrl;
                string overrideUrl = PlayerPrefs.GetString(OverrideKey, string.Empty);
                if (string.IsNullOrEmpty(overrideUrl))
                {
                    return assetUrl;
                }

                string baseline = PlayerPrefs.GetString(OverrideBaselineKey, string.Empty);
                if (baseline != assetUrl)
                {
                    ClearOverride();
                    return assetUrl;
                }

                return TryNormalize(overrideUrl, out string normalizedUrl, out _)
                    ? normalizedUrl
                    : assetUrl;
            }
        }

        /// <summary>True when a local override is active instead of the ScriptableObject value.</summary>
        public static bool HasRuntimeOverride => CurrentUrl != DefaultUrl;

        /// <summary>HTTP request timeout from the ScriptableObject, with a safe default.</summary>
        public static int RequestTimeoutSeconds
        {
            get
            {
                NavigationServerSettings settings = NavigationServerSettings.LoadOrNull();
                return settings != null ? settings.RequestTimeoutSeconds : 5;
            }
        }

        public static bool TrySave(string input, out string normalizedUrl, out string error)
        {
            if (!TryNormalize(input, out normalizedUrl, out error))
            {
                return false;
            }

            if (normalizedUrl == DefaultUrl)
            {
                ClearOverride();
                return true;
            }

            PlayerPrefs.SetString(OverrideKey, normalizedUrl);
            PlayerPrefs.SetString(OverrideBaselineKey, DefaultUrl);
            PlayerPrefs.Save();
            return true;
        }

        public static void ClearOverride()
        {
            PlayerPrefs.DeleteKey(OverrideKey);
            PlayerPrefs.DeleteKey(OverrideBaselineKey);
            PlayerPrefs.Save();
        }

        public static bool TryNormalize(string input, out string normalizedUrl, out string error)
        {
            if (!NavigationServerSettings.TryParse(
                    input,
                    out string host,
                    out int port,
                    out bool useHttps,
                    out error))
            {
                normalizedUrl = string.Empty;
                return false;
            }

            normalizedUrl = NavigationServerSettings.Compose(host, port, useHttps);
            return true;
        }
    }
}
