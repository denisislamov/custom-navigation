using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jitter2.LinearMath;

namespace CustomNavigation.Runtime
{
    public static class NavigationPathFingerprint
    {
        public const int AlgorithmVersion = 2;
        public const string AlgorithmId = "cn-path-fingerprint-v2-mm-away-from-zero-stablemath-f32";

        public static string Compute(IReadOnlyList<JVector> points)
        {
            byte[] bytes = GetCanonicalBytes(points);
            using SHA256 algorithm = SHA256.Create();
            byte[] hash = algorithm.ComputeHash(bytes);
            var result = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                result.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return result.ToString();
        }

        internal static byte[] GetCanonicalBytes(IReadOnlyList<JVector> points)
        {
            if (points == null) throw new System.ArgumentNullException(nameof(points));

            var canonical = new StringBuilder(points.Count * 32);
            for (int i = 0; i < points.Count; i++)
            {
                JVector point = points[i];
                NavigationJitterValidation.RequireFinite(point, nameof(points));
                canonical.Append(Quantize(point.X).ToString(CultureInfo.InvariantCulture));
                canonical.Append(',');
                canonical.Append(Quantize(point.Y).ToString(CultureInfo.InvariantCulture));
                canonical.Append(',');
                canonical.Append(Quantize(point.Z).ToString(CultureInfo.InvariantCulture));
                canonical.Append(';');
            }

            return Encoding.UTF8.GetBytes(canonical.ToString());
        }

        private static long Quantize(float value)
        {
            // Quantized zero has one canonical integer representation, so +0 and -0 serialize identically.
            return StableMath.QuantizeToInt64(value, 1000f);
        }
    }
}
