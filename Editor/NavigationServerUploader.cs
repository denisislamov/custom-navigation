using System;
using CustomNavigation.Authoring;
using UnityEditor;
using UnityEngine;

namespace CustomNavigation.Editor
{
    /// <summary>
    /// Pushes a baked navmesh to the navigation server with <c>POST /artifacts</c>.
    ///
    /// Writing into <c>NavigationData</c> only works when the server shares a file system
    /// with the machine running Unity. Uploading over HTTP is what makes a remote or
    /// containerised server usable, and it is also the only way that guarantees the
    /// artifact landed where the *running* server actually reads from.
    /// </summary>
    public static class NavigationServerUploader
    {
        /// <summary>
        /// Uploads the artifact. <paramref name="completion"/> receives success and a
        /// message that is safe to show to the user.
        /// </summary>
        public static void Upload(
            NavigationArtifactAsset asset,
            bool setActive,
            Action<bool, string> completion)
        {
            if (asset == null)
            {
                completion?.Invoke(false, "No artifact selected.");
                return;
            }

            if (asset.NavigationData == null || string.IsNullOrWhiteSpace(asset.ManifestJson))
            {
                completion?.Invoke(
                    false,
                    $"Artifact '{asset.LevelId}' has no navmesh or manifest. " +
                    "Rebuild it with Build for Client.");
                return;
            }

            var payload = new UploadRequest
            {
                manifestJson = asset.ManifestJson,
                dataBase64 = Convert.ToBase64String(asset.NavigationData.bytes),
                setActive = setActive
            };

            NavigationServerEditorClient.Post(
                "/artifacts",
                JsonUtility.ToJson(payload),
                (success, response) =>
                {
                    if (!success)
                    {
                        // A 4xx still carries a JSON body explaining what was wrong.
                        completion?.Invoke(
                            false,
                            NavigationServerEditorClient.TryParse(
                                response,
                                out UploadResponse failure)
                            && !string.IsNullOrWhiteSpace(failure.message)
                                ? failure.message
                                : "Upload failed: " + response);
                        return;
                    }

                    if (!NavigationServerEditorClient.TryParse(response, out UploadResponse parsed))
                    {
                        completion?.Invoke(false, "Unrecognized server response: " + response);
                        return;
                    }

                    completion?.Invoke(
                        parsed.success,
                        parsed.success
                            ? $"Uploaded {parsed.levelId} ({NavigationArtifactIndex.Short(parsed.artifactHash)}) " +
                              $"to {NavigationServerEditorClient.BaseUrl}. {parsed.message}"
                            : parsed.message);
                });
        }

        [Serializable]
        private sealed class UploadRequest
        {
            public string manifestJson;
            public string dataBase64;
            public bool setActive;
        }

        [Serializable]
        private sealed class UploadResponse
        {
            public bool success;
            public string levelId;
            public string artifactHash;
            public string fileName;
            public bool setActive;
            public string message;
        }
    }

    /// <summary>
    /// Secret for <c>POST /artifacts</c> on a server exposed to the network.
    ///
    /// It is deliberately kept in EditorPrefs instead of the settings asset: the asset
    /// lives in <c>Resources</c> and would ship the token inside every player build.
    /// </summary>
    public static class NavigationServerUploadToken
    {
        private const string KeyPrefix = "CustomNavigation.UploadToken.";

        private static string Key => KeyPrefix + Application.dataPath.GetHashCode();

        public static string Value
        {
            get => EditorPrefs.GetString(Key, string.Empty);
            set => EditorPrefs.SetString(Key, value ?? string.Empty);
        }
    }
}

