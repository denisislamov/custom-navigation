using System;
using System.Collections;
using System.Text;
using Jitter2.LinearMath;
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
        public JVector[] Points = Array.Empty<JVector>();
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
            JVector start,
            JVector destination,
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
            JVector start,
            JVector destination,
            string clientArtifactHash,
            string clientPathFingerprint,
            Action<NavigationServerPathResult> completion)
        {
            var result = new NavigationServerPathResult();
            var payload = new NavigationPathRequest
            {
                RequestId = requestId ?? string.Empty,
                LevelId = levelId ?? string.Empty,
                Start = start,
                Destination = destination,
                ClientArtifactHash = clientArtifactHash ?? string.Empty,
                ClientPathFingerprint = clientPathFingerprint ?? string.Empty
            };
            byte[] body = Encoding.UTF8.GetBytes(NavigationWireCodec.EncodeRequest(payload));

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

            NavigationPathResponse response;
            try
            {
                response = NavigationWireCodec.DecodeResponse(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                result.Message = "Invalid navigation server response: " + exception.Message;
                completion?.Invoke(result);
                yield break;
            }

            if (!response.Success || response.Points == null || response.Points.Length == 0)
            {
                result.Message = string.IsNullOrWhiteSpace(response.Message)
                    ? "Navigation server returned no route."
                    : response.Message;
                result.ArtifactHash = response.ArtifactHash ?? string.Empty;
                completion?.Invoke(result);
                yield break;
            }

            JVector[] points = response.Points;

            result.Success = true;
            result.Points = points;
            result.Message = response.Message ?? string.Empty;
            result.ArtifactHash = response.ArtifactHash ?? string.Empty;
            result.PathFingerprint = NavigationPathFingerprint.Compute(points);
            result.ServerMismatchDetected = response.ServerMismatchDetected
                                            || !string.Equals(
                                                response.PathFingerprint,
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

    }
}
