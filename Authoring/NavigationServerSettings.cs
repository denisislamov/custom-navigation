using System;
using UnityEngine;

namespace CustomNavigation.Authoring
{
    /// <summary>
    /// The single source of truth for the authoritative navigation server address.
    /// The asset lives in Resources, so it is available both to editor tools and to a build.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NavigationServerSettings",
        menuName = "Custom Navigation/Server Settings")]
    public sealed class NavigationServerSettings : ScriptableObject
    {
        public const string ResourcesFolder = "Assets/DataSakura/CustomNavigation/Resources/CustomNavigation";
        public const string ResourceName = "NavigationServerSettings";
        public const string ResourcePath = "CustomNavigation/" + ResourceName;
        public const string AssetPath = ResourcesFolder + "/" + ResourceName + ".asset";

        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 5079;

        /// <summary>
        /// Matches where "Install navigation server" puts the server, so the default
        /// works without touching anything. Uploading over HTTP does not use this at
        /// all - it only matters for Export to Folder and for the fallback comparison
        /// in the Artifacts tab.
        /// </summary>
        public const string DefaultServerArtifactFolder = "NavigationServer/NavigationData";

        private static NavigationServerSettings cachedInstance;

        [SerializeField, Tooltip("IP or hostname of the machine running DotRecastServer.")]
        private string host = DefaultHost;
        [SerializeField, Range(1, 65535), Tooltip("TCP port DotRecastServer listens on (--listen).")]
        private int port = DefaultPort;
        [SerializeField, Tooltip("Use https instead of http. The current DotRecastServer supports http only.")]
        private bool useHttps;
        [SerializeField, Range(1, 60), Tooltip("Timeout for HTTP requests to the navigation server, in seconds.")]
        private int requestTimeoutSeconds = 5;
        [SerializeField, Tooltip("Relative path of the folder Export to Folder writes server artifacts to.")]
        private string serverArtifactFolder = DefaultServerArtifactFolder;
        [SerializeField, TextArea(2, 6), Tooltip("Note for the team: where the server runs and who owns it.")]
        private string notes = "Navigation artifacts are baked offline in Unity (Navigation -> Build for Client) " +
                              "and pushed to the server with the Upload to Server button.";

        public string Host => host;
        public int Port => port;
        public bool UseHttps => useHttps;
        public int RequestTimeoutSeconds => Mathf.Clamp(requestTimeoutSeconds, 1, 60);
        public string ServerArtifactFolder => string.IsNullOrWhiteSpace(serverArtifactFolder)
            ? DefaultServerArtifactFolder
            : serverArtifactFolder.Trim().Replace('\\', '/');
        public string Notes => notes;

        public string BaseUrl => Compose(host, port, useHttps);

        /// <summary>HTTP prefix for the <c>--listen</c> argument of DotRecastServer.</summary>
        public string ListenPrefix => $"{(useHttps ? "https" : "http")}://{(IsWildcardHost(host) ? "*" : host)}:{port}/";

        /// <summary>Loads the asset from Resources. Returns null when it does not exist yet.</summary>
        public static NavigationServerSettings LoadOrNull()
        {
            if (cachedInstance != null)
            {
                return cachedInstance;
            }

            cachedInstance = Resources.Load<NavigationServerSettings>(ResourcePath);
            return cachedInstance;
        }

        /// <summary>Clears the Resources cache. Needed by the editor after creating or deleting the asset.</summary>
        public static void InvalidateCache()
        {
            cachedInstance = null;
        }

        /// <summary>Base URL from the asset, or a hard default when the asset does not exist yet.</summary>
        public static string ResolveBaseUrl()
        {
            NavigationServerSettings settings = LoadOrNull();
            return settings != null ? settings.BaseUrl : Compose(DefaultHost, DefaultPort, false);
        }

        public static string Compose(string hostValue, int portValue, bool https)
        {
            string safeHost = string.IsNullOrWhiteSpace(hostValue) ? DefaultHost : hostValue.Trim();
            int safePort = Mathf.Clamp(portValue, 1, 65535);
            return $"{(https ? "https" : "http")}://{safeHost}:{safePort}";
        }

        /// <summary>Parses an address such as <c>192.168.1.10:5079</c> or <c>http://host:port</c>.</summary>
        public static bool TryParse(
            string input,
            out string parsedHost,
            out int parsedPort,
            out bool parsedHttps,
            out string error)
        {
            parsedHost = DefaultHost;
            parsedPort = DefaultPort;
            parsedHttps = false;
            error = string.Empty;

            string candidate = input?.Trim() ?? string.Empty;
            if (candidate.Length == 0)
            {
                error = "Enter the IP or URL of the navigation server.";
                return false;
            }

            if (!candidate.Contains("://", StringComparison.Ordinal))
            {
                candidate = "http://" + candidate;
            }

            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(uri.Host))
            {
                error = "Expected an address such as http://192.168.1.10:5079";
                return false;
            }

            if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            {
                error = "Provide the base address only, without a query or fragment.";
                return false;
            }

            parsedHttps = uri.Scheme == Uri.UriSchemeHttps;
            parsedHost = uri.Host;
            // Uri.IsDefaultPort is also true for an explicit :80 / :443, so the
            // port presence is detected from the source string: otherwise an address behind a reverse proxy
            // on 80/443 would silently fall back to 5079.
            parsedPort = HasExplicitPort(candidate) ? uri.Port : DefaultPort;
            return true;
        }

        private static bool HasExplicitPort(string absoluteUrl)
        {
            int schemeEnd = absoluteUrl.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd < 0)
            {
                return false;
            }

            int authorityStart = schemeEnd + 3;
            int authorityEnd = absoluteUrl.Length;
            for (int i = authorityStart; i < absoluteUrl.Length; i++)
            {
                char c = absoluteUrl[i];
                if (c == '/' || c == '?' || c == '#')
                {
                    authorityEnd = i;
                    break;
                }
            }

            string authority = absoluteUrl.Substring(authorityStart, authorityEnd - authorityStart);
            int userInfoEnd = authority.LastIndexOf('@');
            if (userInfoEnd >= 0)
            {
                authority = authority.Substring(userInfoEnd + 1);
            }

            // For IPv6 (`[::1]:5079`) the port can only appear after the closing bracket.
            int hostEnd = authority.LastIndexOf(']');
            int colonIndex = authority.IndexOf(':', hostEnd + 1);
            return colonIndex >= 0 && colonIndex < authority.Length - 1;
        }

        /// <summary>Applies a string address to the asset. The caller is responsible for saving it.</summary>
        public bool TryApplyUrl(string input, out string error)
        {
            if (!TryParse(input, out string parsedHost, out int parsedPort, out bool parsedHttps, out error))
            {
                return false;
            }

            host = parsedHost;
            port = parsedPort;
            useHttps = parsedHttps;
            return true;
        }

        private static bool IsWildcardHost(string hostValue)
        {
            return string.Equals(hostValue, "0.0.0.0", StringComparison.Ordinal)
                   || string.Equals(hostValue, "*", StringComparison.Ordinal);
        }

        private void OnValidate()
        {
            host = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim();
            port = Mathf.Clamp(port, 1, 65535);
            requestTimeoutSeconds = Mathf.Clamp(requestTimeoutSeconds, 1, 60);
            InvalidateCache();
        }
    }
}
