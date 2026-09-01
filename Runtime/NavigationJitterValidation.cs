using System;
using Jitter2.LinearMath;
using Real = System.Single;

namespace CustomNavigation.Runtime
{
    /// <summary>Validation shared by canonical runtime and presentation boundaries.</summary>
    public static class NavigationJitterValidation
    {
        public static bool IsFinite(Real value)
        {
            return StableMath.IsFinite(value);
        }

        public static bool IsFinite(JVector value)
        {
            return IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);
        }

        public static JVector RequireFinite(JVector value, string parameterName)
        {
            if (!IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Navigation coordinates must be finite canonical Real values.");
            }

            return value;
        }
    }
}
