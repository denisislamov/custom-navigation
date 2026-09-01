using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jitter2.LinearMath;
using Real = System.Single;

namespace CustomNavigation.Runtime
{
    public static class NavigationPathFingerprint
    {
        public static string Compute(IReadOnlyList<JVector> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

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

            byte[] bytes = Encoding.UTF8.GetBytes(canonical.ToString());
            using SHA256 algorithm = SHA256.Create();
            byte[] hash = algorithm.ComputeHash(bytes);
            var result = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                result.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return result.ToString();
        }

        private static long Quantize(Real value)
        {
            return (long)Math.Round(value * 1000d, MidpointRounding.AwayFromZero);
        }
    }
}
