using System;
using System.Collections.Generic;
using CustomNavigation.Runtime;
using Jitter2.LinearMath;

namespace CustomNavigation.Tests.Shared
{
    public static class NavigationWireConformanceFixtures
    {
        public static readonly string CanonicalRequest =
            "{\"protocolVersion\":2,\"runtimeCompatibilityId\":\"" +
            NavigationWireProtocol.RuntimeCompatibilityId +
            "\",\"requestId\":\"req-1\",\"levelId\":\"arena\",\"start\":{\"x\":1.25,\"y\":0,\"z\":-2.5}," +
            "\"destination\":{\"x\":3,\"y\":4.5,\"z\":0},\"clientArtifactHash\":\"abc\"," +
            "\"clientPathFingerprint\":\"def\"}";

        public static readonly string CanonicalResponse =
            "{\"protocolVersion\":2,\"runtimeCompatibilityId\":\"" +
            NavigationWireProtocol.RuntimeCompatibilityId +
            "\",\"success\":true,\"points\":[{\"x\":1.25,\"y\":0,\"z\":-2.5},{\"x\":3,\"y\":4.5,\"z\":0}]," +
            "\"message\":\"ok\",\"requestId\":\"req-1\",\"artifactHash\":\"abc\"," +
            "\"pathFingerprint\":\"def\",\"serverMismatchDetected\":false}";

        public static string Run()
        {
            var request = new NavigationPathRequest
            {
                RequestId = "req-1",
                LevelId = "arena",
                Start = new JVector(1.25f, -0f, -2.5f),
                Destination = new JVector(3f, 4.5f, -0f),
                ClientArtifactHash = "abc",
                ClientPathFingerprint = "def"
            };
            Equal(CanonicalRequest, NavigationWireCodec.EncodeRequest(request), "request write");
            NavigationPathRequest decodedRequest = NavigationWireCodec.DecodeRequest(CanonicalRequest);
            Equal(CanonicalRequest, NavigationWireCodec.EncodeRequest(decodedRequest), "request roundtrip");

            var response = new NavigationPathResponse
            {
                Success = true,
                Points = new[] { request.Start, request.Destination },
                Message = "ok",
                RequestId = "req-1",
                ArtifactHash = "abc",
                PathFingerprint = "def"
            };
            Equal(CanonicalResponse, NavigationWireCodec.EncodeResponse(response), "response write");
            Equal(CanonicalResponse,
                NavigationWireCodec.EncodeResponse(NavigationWireCodec.DecodeResponse(CanonicalResponse)),
                "response roundtrip");

            foreach (InvalidFixture fixture in InvalidRequests())
            {
                try
                {
                    NavigationWireCodec.DecodeRequest(fixture.Json);
                    throw new InvalidOperationException("Fixture unexpectedly passed: " + fixture.Name);
                }
                catch (NavigationWireFormatException exception)
                {
                    if (exception.Code != fixture.Code)
                        throw new InvalidOperationException(
                            fixture.Name + " expected " + fixture.Code + ", got " + exception.Code + ".");
                }
            }

            return "P04_WIRE_CONFORMANCE_OK valid=4 invalid=" + InvalidRequests().Count;
        }

        public static IReadOnlyList<InvalidFixture> InvalidRequests()
        {
            string id = NavigationWireProtocol.RuntimeCompatibilityId;
            string prefix = "{\"protocolVersion\":2,\"runtimeCompatibilityId\":\"" + id +
                            "\",\"requestId\":\"r\",\"levelId\":\"\",\"start\":";
            string suffix = ",\"destination\":{\"x\":1,\"y\":2,\"z\":3}," +
                            "\"clientArtifactHash\":\"\",\"clientPathFingerprint\":\"\"}";
            return new[]
            {
                new InvalidFixture("missing-x", prefix + "{\"y\":0,\"z\":0}" + suffix, NavigationWireErrorCode.MissingProperty),
                new InvalidFixture("duplicate-x", prefix + "{\"x\":0,\"x\":1,\"y\":0,\"z\":0}" + suffix, NavigationWireErrorCode.DuplicateProperty),
                new InvalidFixture("nan", prefix + "{\"x\":NaN,\"y\":0,\"z\":0}" + suffix, NavigationWireErrorCode.NonFiniteNumber),
                new InvalidFixture("infinity", prefix + "{\"x\":Infinity,\"y\":0,\"z\":0}" + suffix, NavigationWireErrorCode.NonFiniteNumber),
                new InvalidFixture("overflow", prefix + "{\"x\":1e999,\"y\":0,\"z\":0}" + suffix, NavigationWireErrorCode.NumberOverflow),
                new InvalidFixture("underflow", prefix + "{\"x\":1e-999,\"y\":0,\"z\":0}" + suffix, NavigationWireErrorCode.NumberOverflow),
                new InvalidFixture("invalid-number", prefix + "{\"x\":01,\"y\":0,\"z\":0}" + suffix, NavigationWireErrorCode.InvalidJson),
                new InvalidFixture("invalid-json", "{", NavigationWireErrorCode.InvalidJson),
                new InvalidFixture(
                    "legacy-v1-shape",
                    "{\"requestId\":\"r\",\"levelId\":\"\",\"start\":{\"x\":0,\"y\":0,\"z\":0}," +
                    "\"destination\":{\"x\":1,\"y\":2,\"z\":3},\"clientArtifactHash\":\"\"," +
                    "\"clientPathFingerprint\":\"\"}",
                    NavigationWireErrorCode.MissingProperty),
                new InvalidFixture("wrong-version", CanonicalRequest.Replace("\"protocolVersion\":2", "\"protocolVersion\":1"), NavigationWireErrorCode.ProtocolMismatch),
                new InvalidFixture("wrong-identity", CanonicalRequest.Replace(id, "wrong"), NavigationWireErrorCode.RuntimeCompatibilityMismatch)
            };
        }

        private static void Equal(string expected, string actual, string name)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new InvalidOperationException(name + " mismatch. Expected " + expected + ", got " + actual + ".");
        }

        public sealed class InvalidFixture
        {
            public string Name { get; }
            public string Json { get; }
            public NavigationWireErrorCode Code { get; }

            public InvalidFixture(string name, string json, NavigationWireErrorCode code)
            {
                Name = name;
                Json = json;
                Code = code;
            }
        }
    }
}
