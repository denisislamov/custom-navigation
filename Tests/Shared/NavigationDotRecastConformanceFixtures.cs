using System;
using CustomNavigation.Runtime;
using DotRecast.Core.Numerics;
using Jitter2.LinearMath;

namespace CustomNavigation.Tests.Shared
{
    public static class NavigationDotRecastConformanceFixtures
    {
        public static string Run()
        {
            float[] values =
            {
                0f, -0f, 1.25f, -123.5f, float.Epsilon,
                BitConverter.Int32BitsToSingle(0x007fffff),
                BitConverter.Int32BitsToSingle(0x00800000),
                float.MaxValue, -float.MaxValue
            };
            for (int i = 0; i < values.Length; i++)
            {
                var source = new JVector(values[i], values[(i + 1) % values.Length], values[(i + 2) % values.Length]);
                RcVec3f dotRecast = NavigationDotRecastAdapter.ToDotRecast(in source);
                JVector roundtrip = NavigationDotRecastAdapter.FromDotRecast(in dotRecast);
                SameBits(source.X, dotRecast.X); SameBits(source.Y, dotRecast.Y); SameBits(source.Z, dotRecast.Z);
                SameBits(source.X, roundtrip.X); SameBits(source.Y, roundtrip.Y); SameBits(source.Z, roundtrip.Z);
            }

            Expect<ArgumentOutOfRangeException>(() =>
            {
                var value = new JVector(float.NaN, 0f, 0f);
                NavigationDotRecastAdapter.ToDotRecast(in value);
            });
            Expect<ArgumentOutOfRangeException>(() =>
            {
                var value = new RcVec3f(0f, float.PositiveInfinity, 0f);
                NavigationDotRecastAdapter.FromDotRecast(in value);
            });
            Expect<CanonicalJitterValidationException>(() => NavigationDotRecastAdapter.EnsureF32(true));
            return "P05_DOTRECAST_BOUNDARY_OK values=" + values.Length + " negatives=3";
        }

        private static void SameBits(float expected, float actual)
        {
            if (BitConverter.SingleToInt32Bits(expected) != BitConverter.SingleToInt32Bits(actual))
                throw new InvalidOperationException("f32 component bits changed at the DotRecast boundary.");
        }

        private static void Expect<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }
    }
}
