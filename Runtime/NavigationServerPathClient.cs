using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace CustomNavigation.Runtime
{
    /// <summary>
    /// Where the path is computed for a particular agent.
    /// The same three-mode scheme demonstrated by the DotRecastHybridPredicted scene.
    /// </summary>
    public enum NavigationComputeMode
    {
        /// <summary>Local navmesh only. The server is never queried.</summary>
        LocalOnly = 0,

        /// <summary>Authoritative server only. The local navmesh is not used for pathfinding.</summary>
        ServerOnly = 1,

        /// <summary>Immediate local prediction plus an authoritative correction from the server.</summary>
        ServerPredicted = 2
    }

    /// <summary>Result of a path request to the authoritative navigation server.</summary>
    public sealed class NavigationServerPathResult
    {
        public bool Success;
        public Vector3[] Points = Array.Empty<Vector3>();
        public string Message = string.Empty;
        public string ArtifactHash = string.Empty;
        public string PathFingerprint = string.Empty;
        public bool ServerMismatchDetected;
    }

    /// <summary>
    /// Thin HTTP client for <c>POST /path</c> of DotRecastServer.
    /// Shared by the demo scenes and by the client-side bot agents so that the
    /// request and response formats never drift apart.
    /// </summary>
    public static class NavigationServerPathClient
    {
        public static IEnumerator RequestPath(
            string baseUrl,
            string requestId,
            Vector3 start,
            Vector3 destination,
            string clientArtifactHash,
            string clientPathFingerprint,
            Action<NavigationServerPathResult> completion)
        {
            return RequestPath(
                baseUrl,
                requestId,
                null,
                start,
                destination,
                clientArtifactHash,
                clientPathFingerprint,
                completion);
        }

        /// <summary>
        /// Same request, but pinned to a level. The server holds every exported map, so
        /// <paramref name="levelId"/> is what tells it which one to path on. Leave it
        /// null or empty to use the active map, which is what a single-level game wants.
        /// </summary>
        public static IEnumerator RequestPath(
            string baseUrl,
            string requestId,
            string levelId,
            Vector3 start,
            Vector3 destination,
            string clientArtifactHash,
            string clientPathFingerprint,
            Action<NavigationServerPathResult> completion)
        {
            var result = new NavigationServerPathResult();
            var payload = new ServerPathRequest
            {
                requestId = requestId,
                levelId = levelId ?? string.Empty,
                start = ServerVector3.FromUnity(start),
                destination = ServerVector3.FromUnity(destination),
                clientArtifactHash = clientArtifactHash ?? string.Empty,
                clientPathFingerprint = clientPathFingerprint ?? string.Empty
            };
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));

            using var request = new UnityWebRequest(
                BuildUrl(baseUrl, "/path"),
                UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = NavigationServerRuntimeSettings.RequestTimeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                result.Message = "Navigation server unavailable: " + request.error;
                completion?.Invoke(result);
                yield break;
            }

            ServerPathResponse response;
            try
            {
                response = JsonUtility.FromJson<ServerPathResponse>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                result.Message = "Invalid navigation server response: " + exception.Message;
                completion?.Invoke(result);
                yield break;
            }

            if (response == null || !response.success || response.points == null || response.points.Length == 0)
            {
                result.Message = string.IsNullOrWhiteSpace(response?.message)
                    ? "Navigation server returned no route."
                    : response.message;
                result.ArtifactHash = response?.artifactHash ?? string.Empty;
                completion?.Invoke(result);
                yield break;
            }

            var points = new Vector3[response.points.Length];
            for (int i = 0; i < response.points.Length; i++)
            {
                points[i] = response.points[i].ToUnity();
            }

            result.Success = true;
            result.Points = points;
            result.Message = response.message ?? string.Empty;
            result.ArtifactHash = response.artifactHash ?? string.Empty;
            result.PathFingerprint = NavigationPathFingerprint.Compute(points);
            result.ServerMismatchDetected = response.serverMismatchDetected
                                            || !string.Equals(
                                                response.pathFingerprint,
                                                result.PathFingerprint,
                                                StringComparison.OrdinalIgnoreCase);
            completion?.Invoke(result);
        }

        public static string BuildUrl(string baseUrl, string path)
        {
            string root = string.IsNullOrWhiteSpace(baseUrl)
                ? NavigationServerRuntimeSettings.CurrentUrl
                : baseUrl;
            return root.TrimEnd('/') + path;
        }

        [Serializable]
        private sealed class ServerPathRequest
        {
            public string requestId;
            public string levelId;
            public ServerVector3 start;
            public ServerVector3 destination;
            public string clientArtifactHash;
            public string clientPathFingerprint;
        }

        [Serializable]
        private sealed class ServerPathResponse
        {
            public bool success;
            public ServerVector3[] points;
            public string message;
            public string requestId;
            public string artifactHash;
            public string pathFingerprint;
            public bool serverMismatchDetected;
        }

        [Serializable]
        private sealed class ServerVector3
        {
            public float x;
            public float y;
            public float z;

            public static ServerVector3 FromUnity(Vector3 value)
            {
                return new ServerVector3 { x = value.x, y = value.y, z = value.z };
            }

            public Vector3 ToUnity()
            {
                return new Vector3(x, y, z);
            }
        }
    }
}
