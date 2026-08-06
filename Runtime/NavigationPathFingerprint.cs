using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace CustomNavigation.Runtime
{
    public static class NavigationPathFingerprint
    {
        public static string Compute(IReadOnlyList<Vector3> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            var canonical = new StringBuilder(points.Count * 32);
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 point = points[i];
                canonical.Append(Quantize(point.x).ToString(CultureInfo.InvariantCulture));
                canonical.Append(',');
                canonical.Append(Quantize(point.y).ToString(CultureInfo.InvariantCulture));
                canonical.Append(',');
                canonical.Append(Quantize(point.z).ToString(CultureInfo.InvariantCulture));
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

        private static long Quantize(float value)
        {
            return (long)Math.Round(value * 1000d, MidpointRounding.AwayFromZero);
        }
    }
}
