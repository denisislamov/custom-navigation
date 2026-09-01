using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Jitter2.LinearMath;
using Real = System.Single;

namespace CustomNavigation.Runtime
{
    /// <summary>Strict protocol-v2 JSON codec shared byte-for-byte by Unity and .NET server.</summary>
    public static class NavigationWireCodec
    {
        public static string EncodeRequest(NavigationPathRequest value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ValidateIdentity(value.ProtocolVersion, value.RuntimeCompatibilityId, value.Precision,
                value.CanonicalJitterAssemblySha256, value.DeterministicMathCompatibilityId,
                value.FingerprintAlgorithmVersion);
            var json = new StringBuilder(384);
            json.Append('{');
            Property(json, "protocolVersion", value.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
            Property(json, "runtimeCompatibilityId", Quote(value.RuntimeCompatibilityId), true);
            Property(json, "precision", Quote(value.Precision), true);
            Property(json, "canonicalJitterAssemblySha256", Quote(value.CanonicalJitterAssemblySha256), true);
            Property(json, "deterministicMathCompatibilityId", Quote(value.DeterministicMathCompatibilityId), true);
            Property(json, "fingerprintAlgorithmVersion", value.FingerprintAlgorithmVersion.ToString(CultureInfo.InvariantCulture), true);
            Property(json, "requestId", Quote(value.RequestId), true);
            Property(json, "levelId", Quote(value.LevelId), true);
            Property(json, "start", Vector(value.Start), true);
            Property(json, "destination", Vector(value.Destination), true);
            Property(json, "clientArtifactHash", Quote(value.ClientArtifactHash), true);
            Property(json, "clientPathFingerprint", Quote(value.ClientPathFingerprint), true);
            return json.Append('}').ToString();
        }

        public static NavigationPathRequest DecodeRequest(string json)
        {
            var reader = new Reader(json);
            NavigationPathRequest result = reader.ReadRequest();
            reader.RequireEnd();
            ValidateIdentity(result.ProtocolVersion, result.RuntimeCompatibilityId, result.Precision,
                result.CanonicalJitterAssemblySha256, result.DeterministicMathCompatibilityId,
                result.FingerprintAlgorithmVersion);
            return result;
        }

        public static string EncodeResponse(NavigationPathResponse value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            ValidateIdentity(value.ProtocolVersion, value.RuntimeCompatibilityId, value.Precision,
                value.CanonicalJitterAssemblySha256, value.DeterministicMathCompatibilityId,
                value.FingerprintAlgorithmVersion);
            var json = new StringBuilder(512);
            json.Append('{');
            Property(json, "protocolVersion", value.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
            Property(json, "runtimeCompatibilityId", Quote(value.RuntimeCompatibilityId), true);
            Property(json, "precision", Quote(value.Precision), true);
            Property(json, "canonicalJitterAssemblySha256", Quote(value.CanonicalJitterAssemblySha256), true);
            Property(json, "deterministicMathCompatibilityId", Quote(value.DeterministicMathCompatibilityId), true);
            Property(json, "fingerprintAlgorithmVersion", value.FingerprintAlgorithmVersion.ToString(CultureInfo.InvariantCulture), true);
            Property(json, "success", value.Success ? "true" : "false", true);
            Property(json, "points", Vectors(value.Points), true);
            Property(json, "message", Quote(value.Message), true);
            Property(json, "requestId", Quote(value.RequestId), true);
            Property(json, "artifactHash", Quote(value.ArtifactHash), true);
            Property(json, "pathFingerprint", Quote(value.PathFingerprint), true);
            Property(json, "serverMismatchDetected", value.ServerMismatchDetected ? "true" : "false", true);
            return json.Append('}').ToString();
        }

        public static NavigationPathResponse DecodeResponse(string json)
        {
            var reader = new Reader(json);
            NavigationPathResponse result = reader.ReadResponse();
            reader.RequireEnd();
            ValidateIdentity(result.ProtocolVersion, result.RuntimeCompatibilityId, result.Precision,
                result.CanonicalJitterAssemblySha256, result.DeterministicMathCompatibilityId,
                result.FingerprintAlgorithmVersion);
            return result;
        }

        private static void ValidateIdentity(
            int version,
            string compatibilityId,
            string precision,
            string canonicalJitterAssemblySha256,
            string deterministicMathCompatibilityId,
            int fingerprintAlgorithmVersion)
        {
            if (version != NavigationWireProtocol.Version)
            {
                throw Error(NavigationWireErrorCode.ProtocolMismatch,
                    "Expected protocolVersion=" + NavigationWireProtocol.Version + ", got " + version + ".");
            }
            if (!string.Equals(compatibilityId, NavigationWireProtocol.RuntimeCompatibilityId, StringComparison.Ordinal))
            {
                throw Error(NavigationWireErrorCode.RuntimeCompatibilityMismatch,
                    "runtimeCompatibilityId does not match this runtime.");
            }
            RequireIdentity(precision, NavigationCompatibilityContract.Precision,
                NavigationWireErrorCode.PrecisionMismatch, "precision");
            RequireIdentity(canonicalJitterAssemblySha256,
                NavigationCompatibilityContract.CanonicalJitterAssemblySha256,
                NavigationWireErrorCode.CanonicalJitterMismatch, "canonicalJitterAssemblySha256");
            RequireIdentity(deterministicMathCompatibilityId,
                NavigationCompatibilityContract.DeterministicMathCompatibilityId,
                NavigationWireErrorCode.DeterministicMathMismatch, "deterministicMathCompatibilityId");
            if (fingerprintAlgorithmVersion != NavigationCompatibilityContract.FingerprintAlgorithmVersion)
                throw Error(NavigationWireErrorCode.FingerprintAlgorithmMismatch,
                    "fingerprintAlgorithmVersion does not match this runtime.");
        }

        private static void RequireIdentity(
            string actual,
            string expected,
            NavigationWireErrorCode code,
            string field)
        {
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw Error(code, field + " does not match this runtime.");
        }

        private static string Vectors(JVector[] values)
        {
            values = values ?? Array.Empty<JVector>();
            var json = new StringBuilder(values.Length * 48 + 2).Append('[');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(Vector(values[i]));
            }
            return json.Append(']').ToString();
        }

        private static string Vector(JVector value)
        {
            NavigationJitterValidation.RequireFinite(value, nameof(value));
            return "{\"x\":" + Number(value.X) + ",\"y\":" + Number(value.Y) + ",\"z\":" + Number(value.Z) + "}";
        }

        private static string Number(Real value)
        {
            if (!NavigationJitterValidation.IsFinite(value))
                throw Error(NavigationWireErrorCode.NonFiniteNumber, "Coordinates must be finite.");
            return value == 0f ? "0" : value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void Property(StringBuilder json, string name, string value, bool comma = false)
        {
            if (comma) json.Append(',');
            json.Append('"').Append(name).Append("\":").Append(value);
        }

        private static string Quote(string value)
        {
            value = value ?? string.Empty;
            var result = new StringBuilder(value.Length + 2).Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    case '\b': result.Append("\\b"); break;
                    case '\f': result.Append("\\f"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (c < 0x20) result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else result.Append(c);
                        break;
                }
            }
            return result.Append('"').ToString();
        }

        private static NavigationWireFormatException Error(NavigationWireErrorCode code, string message)
        {
            return new NavigationWireFormatException(code, message);
        }

        private sealed class Reader
        {
            private readonly string text;
            private int index;

            public Reader(string json)
            {
                text = json ?? throw Error(NavigationWireErrorCode.InvalidJson, "JSON is null.");
            }

            public NavigationPathRequest ReadRequest()
            {
                var value = new NavigationPathRequest();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                ReadObject(property =>
                {
                    Duplicate(seen, property);
                    switch (property)
                    {
                        case "protocolVersion": value.ProtocolVersion = ReadInteger(); break;
                        case "runtimeCompatibilityId": value.RuntimeCompatibilityId = ReadString(); break;
                        case "precision": value.Precision = ReadString(); break;
                        case "canonicalJitterAssemblySha256": value.CanonicalJitterAssemblySha256 = ReadString(); break;
                        case "deterministicMathCompatibilityId": value.DeterministicMathCompatibilityId = ReadString(); break;
                        case "fingerprintAlgorithmVersion": value.FingerprintAlgorithmVersion = ReadInteger(); break;
                        case "requestId": value.RequestId = ReadString(); break;
                        case "levelId": value.LevelId = ReadString(); break;
                        case "start": value.Start = ReadVector(); break;
                        case "destination": value.Destination = ReadVector(); break;
                        case "clientArtifactHash": value.ClientArtifactHash = ReadString(); break;
                        case "clientPathFingerprint": value.ClientPathFingerprint = ReadString(); break;
                        default: throw Error(NavigationWireErrorCode.UnexpectedProperty, "Unexpected request property: " + property);
                    }
                });
                Require(seen, "protocolVersion", "runtimeCompatibilityId", "precision", "canonicalJitterAssemblySha256", "deterministicMathCompatibilityId", "fingerprintAlgorithmVersion", "requestId", "levelId", "start", "destination", "clientArtifactHash", "clientPathFingerprint");
                return value;
            }

            public NavigationPathResponse ReadResponse()
            {
                var value = new NavigationPathResponse();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                ReadObject(property =>
                {
                    Duplicate(seen, property);
                    switch (property)
                    {
                        case "protocolVersion": value.ProtocolVersion = ReadInteger(); break;
                        case "runtimeCompatibilityId": value.RuntimeCompatibilityId = ReadString(); break;
                        case "precision": value.Precision = ReadString(); break;
                        case "canonicalJitterAssemblySha256": value.CanonicalJitterAssemblySha256 = ReadString(); break;
                        case "deterministicMathCompatibilityId": value.DeterministicMathCompatibilityId = ReadString(); break;
                        case "fingerprintAlgorithmVersion": value.FingerprintAlgorithmVersion = ReadInteger(); break;
                        case "success": value.Success = ReadBoolean(); break;
                        case "points": value.Points = ReadVectors(); break;
                        case "message": value.Message = ReadString(); break;
                        case "requestId": value.RequestId = ReadString(); break;
                        case "artifactHash": value.ArtifactHash = ReadString(); break;
                        case "pathFingerprint": value.PathFingerprint = ReadString(); break;
                        case "serverMismatchDetected": value.ServerMismatchDetected = ReadBoolean(); break;
                        default: throw Error(NavigationWireErrorCode.UnexpectedProperty, "Unexpected response property: " + property);
                    }
                });
                Require(seen, "protocolVersion", "runtimeCompatibilityId", "precision", "canonicalJitterAssemblySha256", "deterministicMathCompatibilityId", "fingerprintAlgorithmVersion", "success", "points", "message", "requestId", "artifactHash", "pathFingerprint", "serverMismatchDetected");
                return value;
            }

            public void RequireEnd()
            {
                White();
                if (index != text.Length) throw Invalid("Trailing JSON content.");
            }

            private void ReadObject(Action<string> property)
            {
                Expect('{');
                White();
                if (Take('}')) return;
                while (true)
                {
                    string name = ReadString();
                    Expect(':');
                    property(name);
                    White();
                    if (Take('}')) return;
                    Expect(',');
                }
            }

            private JVector ReadVector()
            {
                Real x = 0f, y = 0f, z = 0f;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                ReadObject(property =>
                {
                    Duplicate(seen, property);
                    switch (property)
                    {
                        case "x": x = ReadReal(); break;
                        case "y": y = ReadReal(); break;
                        case "z": z = ReadReal(); break;
                        default: throw Error(NavigationWireErrorCode.UnexpectedProperty, "Unexpected coordinate property: " + property);
                    }
                });
                Require(seen, "x", "y", "z");
                return new JVector(x, y, z);
            }

            private JVector[] ReadVectors()
            {
                Expect('[');
                var values = new List<JVector>();
                White();
                if (Take(']')) return values.ToArray();
                while (true)
                {
                    values.Add(ReadVector());
                    White();
                    if (Take(']')) return values.ToArray();
                    Expect(',');
                }
            }

            private Real ReadReal()
            {
                White();
                if (Match("NaN") || Match("Infinity") || Match("-Infinity"))
                    throw Error(NavigationWireErrorCode.NonFiniteNumber, "NaN and Infinity are forbidden.");
                string token = ReadNumberToken();
                double wide;
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out wide))
                    throw Error(NavigationWireErrorCode.NumberOverflow, "Coordinate overflows canonical Real: " + token);
                if (double.IsNaN(wide) || double.IsInfinity(wide))
                    throw Error(NavigationWireErrorCode.NumberOverflow, "Coordinate overflows canonical Real: " + token);
                if (wide == 0d && MantissaHasNonZeroDigit(token))
                    throw Error(NavigationWireErrorCode.NumberOverflow, "Coordinate underflows canonical Real: " + token);
                if (wide > float.MaxValue || wide < -float.MaxValue)
                    throw Error(NavigationWireErrorCode.NumberOverflow, "Coordinate overflows canonical Real: " + token);
                Real value = (Real)wide;
                if (!NavigationJitterValidation.IsFinite(value))
                    throw Error(NavigationWireErrorCode.NonFiniteNumber, "Coordinate is not finite: " + token);
                if (wide != 0d && value == 0f)
                    throw Error(NavigationWireErrorCode.NumberOverflow, "Coordinate underflows canonical Real: " + token);
                return value == 0f ? 0f : value;
            }

            private static bool MantissaHasNonZeroDigit(string token)
            {
                for (int i = 0; i < token.Length && token[i] != 'e' && token[i] != 'E'; i++)
                    if (token[i] >= '1' && token[i] <= '9') return true;
                return false;
            }

            private int ReadInteger()
            {
                string token = ReadNumberToken();
                int value;
                if (token.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0
                    || !int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
                    throw Error(NavigationWireErrorCode.InvalidNumber, "Expected integer, got: " + token);
                return value;
            }

            private string ReadNumberToken()
            {
                White();
                int start = index;
                if (Take('-')) { }
                if (index >= text.Length) throw Invalid("Missing number.");
                if (text[index] == '0') index++;
                else
                {
                    if (text[index] < '1' || text[index] > '9') throw Invalid("Invalid number token.");
                    while (index < text.Length && char.IsDigit(text[index])) index++;
                }
                if (Take('.'))
                {
                    int fraction = index;
                    while (index < text.Length && char.IsDigit(text[index])) index++;
                    if (fraction == index) throw Invalid("Fraction requires digits.");
                }
                if (Take('e') || Take('E'))
                {
                    if (!Take('+')) Take('-');
                    int exponent = index;
                    while (index < text.Length && char.IsDigit(text[index])) index++;
                    if (exponent == index) throw Invalid("Exponent requires digits.");
                }
                return text.Substring(start, index - start);
            }

            private bool ReadBoolean()
            {
                White();
                if (Match("true")) return true;
                if (Match("false")) return false;
                throw Invalid("Expected boolean.");
            }

            private string ReadString()
            {
                White();
                if (!Take('"')) throw Invalid("Expected string.");
                var value = new StringBuilder();
                while (index < text.Length)
                {
                    char c = text[index++];
                    if (c == '"') return value.ToString();
                    if (c < 0x20) throw Invalid("Unescaped control character in string.");
                    if (c != '\\') { value.Append(c); continue; }
                    if (index >= text.Length) throw Invalid("Incomplete string escape.");
                    char escape = text[index++];
                    switch (escape)
                    {
                        case '"': value.Append('"'); break;
                        case '\\': value.Append('\\'); break;
                        case '/': value.Append('/'); break;
                        case 'b': value.Append('\b'); break;
                        case 'f': value.Append('\f'); break;
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'u': value.Append(ReadUnicode()); break;
                        default: throw Invalid("Invalid string escape.");
                    }
                }
                throw Invalid("Unterminated string.");
            }

            private char ReadUnicode()
            {
                if (index + 4 > text.Length) throw Invalid("Incomplete unicode escape.");
                int value;
                if (!int.TryParse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
                    throw Invalid("Invalid unicode escape.");
                index += 4;
                return (char)value;
            }

            private void Duplicate(HashSet<string> seen, string property)
            {
                if (!seen.Add(property))
                    throw Error(NavigationWireErrorCode.DuplicateProperty, "Duplicate property: " + property);
            }

            private static void Require(HashSet<string> seen, params string[] names)
            {
                for (int i = 0; i < names.Length; i++)
                    if (!seen.Contains(names[i]))
                        throw Error(NavigationWireErrorCode.MissingProperty, "Missing property: " + names[i]);
            }

            private void Expect(char expected)
            {
                White();
                if (!Take(expected)) throw Invalid("Expected '" + expected + "'.");
            }

            private bool Take(char value)
            {
                if (index < text.Length && text[index] == value) { index++; return true; }
                return false;
            }

            private bool Match(string value)
            {
                if (index + value.Length > text.Length
                    || string.CompareOrdinal(text, index, value, 0, value.Length) != 0) return false;
                index += value.Length;
                return true;
            }

            private void White()
            {
                while (index < text.Length)
                {
                    char c = text[index];
                    if (c != ' ' && c != '\t' && c != '\r' && c != '\n') break;
                    index++;
                }
            }

            private NavigationWireFormatException Invalid(string message)
            {
                return Error(NavigationWireErrorCode.InvalidJson, message + " Offset " + index + ".");
            }
        }
    }
}
