using CustomNavigation.Runtime;
using Jitter2.LinearMath;
using UnityEngine;

namespace CustomNavigation.UnityAdapter
{
    /// <summary>
    /// The only supported conversion boundary between Unity presentation coordinates and
    /// canonical navigation coordinates.
    /// </summary>
    public static class NavigationUnityAdapter
    {
        public static JVector ToJitter(Vector3 value)
        {
            return NavigationJitterValidation.RequireFinite(
                new JVector(value.x, value.y, value.z),
                nameof(value));
        }

        public static Vector3 ToUnity(JVector value)
        {
            NavigationJitterValidation.RequireFinite(value, nameof(value));
            return new Vector3(value.X, value.Y, value.Z);
        }
    }
}
