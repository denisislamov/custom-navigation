using System;
using System.Collections.Generic;
using System.IO;
using CustomNavigation.Runtime;
using UnityEditor;

namespace CustomNavigation.Editor
{
    internal static class CanonicalJitterEditorPreflight
    {
        public static void EnsureReady()
        {
            string[] assetPaths = AssetDatabase.GetAllAssetPaths();
            var candidates = new List<string>();
            for (int index = 0; index < assetPaths.Length; index++)
            {
                string assetPath = assetPaths[index];
                if (string.Equals(
                        Path.GetFileName(assetPath),
                        CanonicalJitterContract.ApprovedAssemblyName + ".dll",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                    {
                        throw new CanonicalJitterValidationException(
                            CanonicalJitterErrorCode.IdentityMismatch,
                            "Canonical Jitter must be installed as a project-owned Assets plugin, " +
                            "not supplied transitively by another package: " + assetPath);
                    }

                    candidates.Add(Path.GetFullPath(assetPath));
                }
            }

            CanonicalJitterContract.ValidateInstalledFiles(candidates);
        }
    }
}
