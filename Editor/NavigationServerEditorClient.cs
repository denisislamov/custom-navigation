using System;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Minimal HTTP client for the navigation server, used by editor windows.
    /// The editor has no coroutines, so the request is pumped from EditorApplication.update.
    /// </summary>
    internal static class NavigationServerEditorClient
    {
        public static string BaseUrl
        {
            get
            {
                NavigationServerSettings settings = NavigationServerSettings.LoadOrNull();
                return settings != null ? settings.BaseUrl : NavigationServerRuntimeSettings.DefaultUrl;
            }
        }

        public static int TimeoutSeconds
        {
            get
            {
                NavigationServerSettings settings = NavigationServerSettings.LoadOrNull();
                return settings != null ? settings.RequestTimeoutSeconds : 5;
            }
        }

        public static void Get(string path, Action<bool, string> completion)
        {
            Send(UnityWebRequest.Get(BaseUrl.TrimEnd('/') + path), BaseUrl.TrimEnd('/') + path, completion);
        }

        /// <summary>POST with a JSON body. Used by the Path Probe and by artifact uploads.</summary>
        public static void Post(string path, string json, Action<bool, string> completion)
        {
            string url = BaseUrl.TrimEnd('/') + path;
            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");

            // A server exposed to the network refuses uploads without this header.
            string token = NavigationServerUploadToken.Value;
            if (!string.IsNullOrEmpty(token))
            {
                request.SetRequestHeader("X-Navigation-Token", token);
            }

            Send(request, url, completion);
        }

        private static void Send(UnityWebRequest request, string url, Action<bool, string> completion)
        {
            request.timeout = TimeoutSeconds;
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            bool finished = false;

            void Finish()
            {
                if (finished)
                {
                    return;
                }

                finished = true;
                EditorApplication.update -= Poll;
                AssemblyReloadEvents.beforeAssemblyReload -= Abort;
            }

            void Poll()
            {
                if (!operation.isDone)
                {
                    return;
                }

                Finish();
                bool success = request.result == UnityWebRequest.Result.Success;

                // A 4xx/5xx still carries a JSON body that explains the refusal, and it
                // is far more useful than the bare "HTTP/1.1 400 Bad Request".
                string body = request.downloadHandler != null ? request.downloadHandler.text : null;
                string payload = success || !string.IsNullOrWhiteSpace(body)
                    ? body
                    : $"{url}: {request.error}";
                request.Dispose();
                completion?.Invoke(success, payload);
            }

            // A domain reload destroys the EditorApplication.update subscription, so
            // without an explicit abort the native request handle would leak and no callback would arrive.
            void Abort()
            {
                Finish();
                request.Abort();
                request.Dispose();
            }

            EditorApplication.update += Poll;
            AssemblyReloadEvents.beforeAssemblyReload += Abort;
        }

        /// <summary>Response of <c>GET /artifacts</c>. The fields match DotRecastServer/Contracts.cs.</summary>
        [Serializable]
        public sealed class ArtifactsResponse
        {
            public string loadedLevelId;
            public string loadedArtifactHash;
            public string dataDirectory;
            public ServerArtifact[] artifacts;
        }

        [Serializable]
        public sealed class ServerArtifact
        {
            public string levelId;
            public string description;
            public string artifactHash;
            public string schemaVersion;
            public string dotRecastVersion;
            public string precision;
            public string canonicalJitterAssemblySha256;
            public string deterministicMathCompatibilityId;
            public int fingerprintAlgorithmVersion;
            public string fingerprintAlgorithmId;
            public string agentProfileId;
            public int polygonCount;
            public int sourceMeshCount;
            public string fileName;
            public bool dataPresent;
            public bool hashMatchesData;
            public bool isActive;
            public bool isLoaded;
            public string error;
        }

        [Serializable]
        public sealed class HealthResponse
        {
            public string status;
            public string dotRecastVersion;
            public int navigationPolygons;
            public string levelId;
            public string description;
            public string artifactHash;
            public string schemaVersion;
            public string precision;
            public string canonicalJitterAssemblySha256;
            public string deterministicMathCompatibilityId;
            public int fingerprintAlgorithmVersion;

            /// <summary>Set when <see cref="status"/> is not "ok": says what to do about it.</summary>
            public string message;

            /// <summary>Folder the server serves artifacts from.</summary>
            public string dataDirectory;

            /// <summary>Every level the server can path on right now.</summary>
            public string[] availableLevels;
        }

        public static bool TryParse<T>(string json, out T value) where T : class
        {
            try
            {
                value = JsonUtility.FromJson<T>(json);
                return value != null;
            }
            catch (Exception)
            {
                value = null;
                return false;
            }
        }
    }
}
