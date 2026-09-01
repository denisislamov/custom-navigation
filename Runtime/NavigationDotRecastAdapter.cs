using DotRecast.Core.Numerics;
using Jitter2;
using Jitter2.LinearMath;

namespace CustomNavigation.Runtime
{
    /// <summary>The single owned conversion boundary between canonical and DotRecast coordinates.</summary>
    internal static class NavigationDotRecastAdapter
    {
        internal static RcVec3f ToDotRecast(in JVector value)
        {
            EnsureF32(Precision.IsDoublePrecision);
            NavigationJitterValidation.RequireFinite(value, nameof(value));
            return new RcVec3f(value.X, value.Y, value.Z);
        }

        internal static JVector FromDotRecast(in RcVec3f value)
        {
            EnsureF32(Precision.IsDoublePrecision);
            var result = new JVector(value.X, value.Y, value.Z);
            return NavigationJitterValidation.RequireFinite(result, nameof(value));
        }

        internal static void EnsureF32(bool isDoublePrecision)
        {
            if (isDoublePrecision)
            {
                throw new CanonicalJitterValidationException(
                    CanonicalJitterErrorCode.DoublePrecisionUnsupported,
                    "DotRecast boundary requires canonical Jitter precision=f32; f64 narrowing is forbidden.");
            }
        }
    }
}
