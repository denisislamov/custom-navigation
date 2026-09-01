using System;
using Jitter2.LinearMath;

namespace CustomNavigation.Runtime
{
    public static class NavigationWireProtocol
    {
        public const int Version = 2;
        public const string RuntimeCompatibilityId =
            "cn-jmp-v2-f32-jitter-944666bb-math-54b456c0-fingerprint-v2";
    }

    public enum NavigationWireErrorCode
    {
        InvalidJson,
        MissingProperty,
        DuplicateProperty,
        UnexpectedProperty,
        InvalidNumber,
        NonFiniteNumber,
        NumberOverflow,
        ProtocolMismatch,
        RuntimeCompatibilityMismatch,
        PrecisionMismatch,
        CanonicalJitterMismatch,
        DeterministicMathMismatch,
        FingerprintAlgorithmMismatch
    }

    public sealed class NavigationWireFormatException : FormatException
    {
        public NavigationWireErrorCode Code { get; }

        public NavigationWireFormatException(NavigationWireErrorCode code, string message)
            : base(message)
        {
            Code = code;
        }
    }

    public sealed class NavigationPathRequest
    {
        public int ProtocolVersion { get; set; } = NavigationWireProtocol.Version;
        public string RuntimeCompatibilityId { get; set; } = NavigationWireProtocol.RuntimeCompatibilityId;
        public string Precision { get; set; } = NavigationCompatibilityContract.Precision;
        public string CanonicalJitterAssemblySha256 { get; set; } = NavigationCompatibilityContract.CanonicalJitterAssemblySha256;
        public string DeterministicMathCompatibilityId { get; set; } = NavigationCompatibilityContract.DeterministicMathCompatibilityId;
        public int FingerprintAlgorithmVersion { get; set; } = NavigationCompatibilityContract.FingerprintAlgorithmVersion;
        public string RequestId { get; set; } = string.Empty;
        public string LevelId { get; set; } = string.Empty;
        public JVector Start { get; set; }
        public JVector Destination { get; set; }
        public string ClientArtifactHash { get; set; } = string.Empty;
        public string ClientPathFingerprint { get; set; } = string.Empty;
    }

    public sealed class NavigationPathResponse
    {
        public int ProtocolVersion { get; set; } = NavigationWireProtocol.Version;
        public string RuntimeCompatibilityId { get; set; } = NavigationWireProtocol.RuntimeCompatibilityId;
        public string Precision { get; set; } = NavigationCompatibilityContract.Precision;
        public string CanonicalJitterAssemblySha256 { get; set; } = NavigationCompatibilityContract.CanonicalJitterAssemblySha256;
        public string DeterministicMathCompatibilityId { get; set; } = NavigationCompatibilityContract.DeterministicMathCompatibilityId;
        public int FingerprintAlgorithmVersion { get; set; } = NavigationCompatibilityContract.FingerprintAlgorithmVersion;
        public bool Success { get; set; }
        public JVector[] Points { get; set; } = Array.Empty<JVector>();
        public string Message { get; set; } = string.Empty;
        public string RequestId { get; set; } = string.Empty;
        public string ArtifactHash { get; set; } = string.Empty;
        public string PathFingerprint { get; set; } = string.Empty;
        public bool ServerMismatchDetected { get; set; }
    }
}
