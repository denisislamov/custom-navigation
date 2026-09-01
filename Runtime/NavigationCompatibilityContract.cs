using System;

namespace CustomNavigation.Runtime
{
    public enum NavigationCompatibilityField
    {
        SchemaVersion,
        DotRecastVersion,
        Precision,
        CanonicalJitter,
        DeterministicMath,
        FingerprintAlgorithm,
        ArtifactHash
    }

    public sealed class NavigationCompatibilityException : InvalidOperationException
    {
        public NavigationCompatibilityField Field { get; }

        public NavigationCompatibilityException(NavigationCompatibilityField field, string message)
            : base(message)
        {
            Field = field;
        }
    }

    /// <summary>One compatibility identity shared by bake, Unity runtime, wire codec, and server.</summary>
    public static class NavigationCompatibilityContract
    {
        public const string ArtifactSchemaVersion = "2";
        public const string DotRecastVersion = "2026.1.3";
        public const string Precision = CanonicalJitterContract.ApprovedPrecision;
        public const string CanonicalJitterAssemblySha256 = CanonicalJitterContract.ApprovedAssemblySha256;
        public const string DeterministicMathCompatibilityId =
            CanonicalJitterContract.ApprovedStableMathCompatibilityId;
        public const int FingerprintAlgorithmVersion = NavigationPathFingerprint.AlgorithmVersion;
        public const string FingerprintAlgorithmId = NavigationPathFingerprint.AlgorithmId;

        public static void ValidateArtifact(
            string schemaVersion,
            string dotRecastVersion,
            string precision,
            string canonicalJitterAssemblySha256,
            string deterministicMathCompatibilityId,
            int fingerprintAlgorithmVersion,
            string fingerprintAlgorithmId)
        {
            if (!string.Equals(schemaVersion, ArtifactSchemaVersion, StringComparison.Ordinal))
            {
                string migration = string.Equals(schemaVersion, "1", StringComparison.Ordinal)
                    ? " Schema 1 belongs to the pre-JMP identity and must be re-baked and re-exported."
                    : string.Empty;
                throw Mismatch(NavigationCompatibilityField.SchemaVersion, ArtifactSchemaVersion, schemaVersion, migration);
            }
            Equal(NavigationCompatibilityField.DotRecastVersion, DotRecastVersion, dotRecastVersion);
            Equal(NavigationCompatibilityField.Precision, Precision, precision);
            Equal(NavigationCompatibilityField.CanonicalJitter, CanonicalJitterAssemblySha256,
                canonicalJitterAssemblySha256);
            Equal(NavigationCompatibilityField.DeterministicMath, DeterministicMathCompatibilityId,
                deterministicMathCompatibilityId);
            if (fingerprintAlgorithmVersion != FingerprintAlgorithmVersion)
            {
                throw Mismatch(
                    NavigationCompatibilityField.FingerprintAlgorithm,
                    FingerprintAlgorithmVersion.ToString(),
                    fingerprintAlgorithmVersion.ToString());
            }
            Equal(NavigationCompatibilityField.FingerprintAlgorithm, FingerprintAlgorithmId,
                fingerprintAlgorithmId);
        }

        public static void ValidateArtifactHash(string clientHash, string serverHash)
        {
            if (string.IsNullOrWhiteSpace(clientHash)
                || !string.Equals(clientHash, serverHash, StringComparison.OrdinalIgnoreCase))
            {
                throw Mismatch(NavigationCompatibilityField.ArtifactHash, serverHash, clientHash,
                    " Route query was rejected before DotRecast execution.");
            }
        }

        private static void Equal(NavigationCompatibilityField field, string expected, string actual)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw Mismatch(field, expected, actual);
        }

        private static NavigationCompatibilityException Mismatch(
            NavigationCompatibilityField field,
            string expected,
            string actual,
            string suffix = "")
        {
            return new NavigationCompatibilityException(
                field,
                field + " mismatch: expected '" + expected + "', got '" + (actual ?? string.Empty) + "'." + suffix);
        }
    }
}
