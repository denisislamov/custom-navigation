using System;
using System.Collections.Generic;
using System.Text;
using CustomNavigation.Runtime;
using Jitter2.LinearMath;

namespace CustomNavigation.Tests.Shared
{
    public static class NavigationPathFingerprintFixtures
    {
        public static string Run()
        {
            Equal(2, NavigationPathFingerprint.AlgorithmVersion, "algorithm version");
            Equal(
                "cn-path-fingerprint-v2-mm-away-from-zero-stablemath-f32",
                NavigationPathFingerprint.AlgorithmId,
                "algorithm id");

            IReadOnlyList<Fixture> fixtures = Corpus();
            foreach (Fixture fixture in fixtures)
            {
                byte[] actualBytes = NavigationPathFingerprint.GetCanonicalBytes(fixture.Points);
                Equal(fixture.CanonicalUtf8Hex, ToHex(actualBytes), fixture.Name + " canonical bytes");
                Equal(fixture.Hash, NavigationPathFingerprint.Compute(fixture.Points), fixture.Name + " hash");
            }

            ExpectArgumentOutOfRange(
                () => NavigationPathFingerprint.Compute(new[] { new JVector(float.NaN, 0f, 0f) }),
                "non-finite point");

            return "P06_FINGERPRINT_GOLDEN_OK version=2 fixtures=" + fixtures.Count + " negatives=1";
        }

        public static IReadOnlyList<Fixture> Corpus()
        {
            return new[]
            {
                Fixture.Create("empty", Array.Empty<JVector>(), "", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
                Fixture.Create("zero-near", new[] { new JVector(-0f, 0f, float.Epsilon) },
                    "302c302c303b", "898d8af81ecf99eb26a0c523f19e65adad6dba00eba600bb74e106f6874326e9"),
                Fixture.Create("positive-half", new[] { new JVector(0.0005f, 0.0015f, 1.2345f) },
                    "312c322c313233353b", "d6d3b3116e6bb6ebc92ac0f1c5408c1485b91be8dab7ed747c1add304db814eb"),
                Fixture.Create("negative-half", new[] { new JVector(-0.0005f, -0.0015f, -1.2345f) },
                    "2d312c2d322c2d313233353b", "0b1cd79657fdff0c01b4787a18b2dc229e5978fc6d90bf0215045f3a4b33bc45"),
                Fixture.Create("large-finite", new[] { new JVector(123456.789f, -123456.789f, 100199.578125f) },
                    "3132333435363739322c2d3132333435363739322c3130303139393537363b",
                    "98cfe3d161c069a16e6d96eca8e5815f91a2a778d578981e36a8d87525cbe0b5"),
                Fixture.Create("multiple", new[]
                    {
                        new JVector(-0f, 0.0005f, -0.0005f),
                        new JVector(1.2345f, -1.2345f, float.Epsilon),
                        new JVector(123456.789f, -123456.789f, 100199.578125f)
                    },
                    "302c312c2d313b313233352c2d313233352c303b3132333435363739322c2d3132333435363739322c3130303139393537363b",
                    "68f5f8bbafef434c71e319eb0b3192c94a5ae1dcef3215da7dc269047be62a4f")
            };
        }

        private static string ToHex(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        private static void Equal<T>(T expected, T actual, string name)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(name + " mismatch. Expected " + expected + ", got " + actual + ".");
        }

        private static void ExpectArgumentOutOfRange(Action action, string name)
        {
            try
            {
                action();
                throw new InvalidOperationException(name + " unexpectedly passed.");
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        public sealed class Fixture
        {
            public string Name { get; }
            public JVector[] Points { get; }
            public string CanonicalUtf8Hex { get; }
            public string Hash { get; }

            private Fixture(string name, JVector[] points, string canonicalUtf8Hex, string hash)
            {
                Name = name;
                Points = points;
                CanonicalUtf8Hex = canonicalUtf8Hex;
                Hash = hash;
            }

            public static Fixture Create(string name, JVector[] points, string canonicalUtf8Hex, string hash)
            {
                return new Fixture(name, points, canonicalUtf8Hex, hash);
            }
        }
    }
}
