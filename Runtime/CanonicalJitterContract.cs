using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Jitter2;
using Jitter2.LinearMath;

namespace CustomNavigation.Runtime
{
    public enum CanonicalJitterErrorCode
    {
        MissingAssembly,
        DuplicateAssembly,
        AssemblyHashMismatch,
        DoublePrecisionUnsupported,
        StableMathNotPublic,
        IdentityMismatch
    }

    public sealed class CanonicalJitterValidationException : InvalidOperationException
    {
        public CanonicalJitterErrorCode Code { get; }

        public CanonicalJitterValidationException(
            CanonicalJitterErrorCode code,
            string message)
            : base(message)
        {
            Code = code;
        }
    }

    public sealed class CanonicalJitterIdentity
    {
        public string Repository { get; }
        public string Tag { get; }
        public string PackageCommit { get; }
        public string AssemblySha256 { get; }
        public string Precision { get; }
        public string SourceContentHash { get; }
        public string CompileProfileId { get; }
        public string StableMathCompatibilityId { get; }

        public CanonicalJitterIdentity(
            string repository,
            string tag,
            string packageCommit,
            string assemblySha256,
            string precision,
            string sourceContentHash,
            string compileProfileId,
            string stableMathCompatibilityId)
        {
            Repository = repository ?? string.Empty;
            Tag = tag ?? string.Empty;
            PackageCommit = packageCommit ?? string.Empty;
            AssemblySha256 = assemblySha256 ?? string.Empty;
            Precision = precision ?? string.Empty;
            SourceContentHash = sourceContentHash ?? string.Empty;
            CompileProfileId = compileProfileId ?? string.Empty;
            StableMathCompatibilityId = stableMathCompatibilityId ?? string.Empty;
        }
    }

    /// <summary>
    /// Fail-fast contract for the separately installed canonical Jitter runtime.
    /// This package does not contain or install Jitter2.Core.
    /// </summary>
    public static class CanonicalJitterContract
    {
        public const string ApprovedRepository =
            "https://github.com/denisislamov/jitter-physics-baker";
        public const string ApprovedTag = "jitter-v2.8.9-datasakura.1-rc.1";
        public const string ApprovedPackageCommit =
            "508de73d6d82088d58a74fd41d7e09b70f009b1d";
        public const string ApprovedAssemblyName = "Jitter2.Core";
        public const string ApprovedAssemblySha256 =
            "944666bbe73dfce5ffc5bfb18569fb0004f50e767dcbb8b471dde15242023ca6";
        public const string ApprovedPrecision = "f32";
        public const string ApprovedSourceContentHash =
            "sha256:749c79e40c4965cd455ca80a2d1d1c80a24eb580eb7b721e07adc78b41c82762";
        public const string ApprovedCompileProfileId =
            "a2925211b983330117414426be9bf8a2798ce9169c1206e1e55178f708cfa72e";
        public const string ApprovedStableMathCompatibilityId =
            "54b456c04074909605d2ba138e5001d39a90a338885eafcb32265483b35054b0";

        public static CanonicalJitterIdentity ApprovedIdentity => new CanonicalJitterIdentity(
            ApprovedRepository,
            ApprovedTag,
            ApprovedPackageCommit,
            ApprovedAssemblySha256,
            ApprovedPrecision,
            ApprovedSourceContentHash,
            ApprovedCompileProfileId,
            ApprovedStableMathCompatibilityId);

        public static void ValidateInstalledFiles(IEnumerable<string> candidatePaths)
        {
            if (candidatePaths == null)
            {
                throw Failure(
                    CanonicalJitterErrorCode.MissingAssembly,
                    "Canonical Jitter preflight found no Jitter2.Core.dll. Install the approved " +
                    ApprovedTag + " release before Custom Navigation.");
            }

            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in candidatePaths)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                string path = Path.GetFullPath(candidate);
                if (File.Exists(path)
                    && string.Equals(
                        Path.GetFileName(path),
                        ApprovedAssemblyName + ".dll",
                        StringComparison.OrdinalIgnoreCase))
                {
                    unique.Add(path);
                }
            }

            if (unique.Count == 0)
            {
                throw Failure(
                    CanonicalJitterErrorCode.MissingAssembly,
                    "Canonical Jitter preflight found no Jitter2.Core.dll. Install the approved " +
                    ApprovedTag + " release before Custom Navigation.");
            }

            if (unique.Count != 1)
            {
                throw Failure(
                    CanonicalJitterErrorCode.DuplicateAssembly,
                    "Canonical Jitter preflight found " + unique.Count.ToString(CultureInfo.InvariantCulture) +
                    " Jitter2.Core.dll files. Keep exactly one project-owned copy.");
            }

            string installedPath = string.Empty;
            foreach (string path in unique)
            {
                installedPath = path;
            }

            string actualHash = ComputeSha256(installedPath);
            if (!string.Equals(actualHash, ApprovedAssemblySha256, StringComparison.Ordinal))
            {
                throw Failure(
                    CanonicalJitterErrorCode.AssemblyHashMismatch,
                    "Canonical Jitter assembly hash mismatch. Expected " +
                    ApprovedAssemblySha256 + ", got " + actualHash + ".");
            }

            ValidateLoadedAssembly();
        }

        public static void ValidateLoadedAssembly()
        {
            Assembly expected = typeof(Precision).Assembly;
            int loadedCount = 0;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(
                        assembly.GetName().Name,
                        ApprovedAssemblyName,
                        StringComparison.Ordinal))
                {
                    loadedCount++;
                }
            }

            if (loadedCount == 0)
            {
                throw Failure(
                    CanonicalJitterErrorCode.MissingAssembly,
                    "Jitter2.Core is not loaded. Install canonical Jitter before Custom Navigation.");
            }

            if (loadedCount != 1)
            {
                throw Failure(
                    CanonicalJitterErrorCode.DuplicateAssembly,
                    "More than one Jitter2.Core assembly is loaded. Deterministic work is blocked.");
            }

            if (!string.Equals(
                    expected.GetName().Name,
                    ApprovedAssemblyName,
                    StringComparison.Ordinal))
            {
                throw Failure(
                    CanonicalJitterErrorCode.IdentityMismatch,
                    "The compile-time Jitter assembly name is not " + ApprovedAssemblyName + ".");
            }

            ValidateMetadata(ApprovedIdentity, Precision.IsDoublePrecision, typeof(StableMath).IsPublic);

            string location = expected.Location;
            if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
            {
                string actualHash = ComputeSha256(location);
                if (!string.Equals(actualHash, ApprovedAssemblySha256, StringComparison.Ordinal))
                {
                    throw Failure(
                        CanonicalJitterErrorCode.AssemblyHashMismatch,
                        "Loaded Jitter2.Core hash mismatch. Expected " +
                        ApprovedAssemblySha256 + ", got " + actualHash + ".");
                }
            }
        }

        public static void ValidateMetadata(
            CanonicalJitterIdentity identity,
            bool isDoublePrecision,
            bool stableMathIsPublic)
        {
            if (isDoublePrecision)
            {
                throw Failure(
                    CanonicalJitterErrorCode.DoublePrecisionUnsupported,
                    "Custom Navigation requires canonical Jitter precision=f32; f64 is unsupported.");
            }

            if (!stableMathIsPublic)
            {
                throw Failure(
                    CanonicalJitterErrorCode.StableMathNotPublic,
                    "Canonical Jitter must expose public Jitter2.LinearMath.StableMath.");
            }

            if (identity == null
                || !string.Equals(identity.Repository, ApprovedRepository, StringComparison.Ordinal)
                || !string.Equals(identity.Tag, ApprovedTag, StringComparison.Ordinal)
                || !string.Equals(identity.PackageCommit, ApprovedPackageCommit, StringComparison.Ordinal)
                || !string.Equals(identity.AssemblySha256, ApprovedAssemblySha256, StringComparison.Ordinal)
                || !string.Equals(identity.Precision, ApprovedPrecision, StringComparison.Ordinal)
                || !string.Equals(identity.SourceContentHash, ApprovedSourceContentHash, StringComparison.Ordinal)
                || !string.Equals(identity.CompileProfileId, ApprovedCompileProfileId, StringComparison.Ordinal)
                || !string.Equals(
                    identity.StableMathCompatibilityId,
                    ApprovedStableMathCompatibilityId,
                    StringComparison.Ordinal))
            {
                throw Failure(
                    CanonicalJitterErrorCode.IdentityMismatch,
                    "Canonical Jitter identity does not match the approved " + ApprovedTag + " manifest.");
            }
        }

        public static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] hash = algorithm.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                for (int index = 0; index < hash.Length; index++)
                {
                    builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static CanonicalJitterValidationException Failure(
            CanonicalJitterErrorCode code,
            string message)
        {
            return new CanonicalJitterValidationException(code, message);
        }
    }
}
