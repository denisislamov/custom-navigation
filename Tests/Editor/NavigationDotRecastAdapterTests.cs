using System;
using System.Linq;
using System.Reflection;
using CustomNavigation.Runtime;
using CustomNavigation.Tests.Shared;
using DotRecast.Core.Numerics;
using Jitter2.LinearMath;
using NUnit.Framework;

namespace CustomNavigation.Editor.Tests
{
    public sealed class NavigationDotRecastAdapterTests
    {
        [Test]
        public void SharedUnityAndDotNetCorpusPasses()
        {
            Assert.That(
                NavigationDotRecastConformanceFixtures.Run(),
                Is.EqualTo("P05_DOTRECAST_BOUNDARY_OK values=9 negatives=3"));
        }

        [Test]
        public void F32ComponentsPreserveExactBitsInBothDirections()
        {
            float[] values =
            {
                0f,
                -0f,
                1.25f,
                -123.5f,
                float.Epsilon,
                BitConverter.Int32BitsToSingle(0x007fffff),
                BitConverter.Int32BitsToSingle(0x00800000),
                float.MaxValue,
                -float.MaxValue
            };

            for (int i = 0; i < values.Length; i++)
            {
                var source = new JVector(values[i], values[(i + 1) % values.Length], values[(i + 2) % values.Length]);
                RcVec3f dotRecast = NavigationDotRecastAdapter.ToDotRecast(in source);
                JVector roundtrip = NavigationDotRecastAdapter.FromDotRecast(in dotRecast);
                SameBits(source.X, dotRecast.X);
                SameBits(source.Y, dotRecast.Y);
                SameBits(source.Z, dotRecast.Z);
                SameBits(source.X, roundtrip.X);
                SameBits(source.Y, roundtrip.Y);
                SameBits(source.Z, roundtrip.Z);
            }
        }

        [Test]
        public void NonFiniteComponentsAreRejectedAtBothSides()
        {
            var invalidJitter = new JVector(float.NaN, 0f, 0f);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => NavigationDotRecastAdapter.ToDotRecast(in invalidJitter));

            var invalidDotRecast = new RcVec3f(0f, float.PositiveInfinity, 0f);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => NavigationDotRecastAdapter.FromDotRecast(in invalidDotRecast));
        }

        [Test]
        public void F64ProfileFailsBeforeAnyNarrowing()
        {
            CanonicalJitterValidationException exception =
                Assert.Throws<CanonicalJitterValidationException>(
                    () => NavigationDotRecastAdapter.EnsureF32(true));
            Assert.That(exception.Code, Is.EqualTo(CanonicalJitterErrorCode.DoublePrecisionUnsupported));
            Assert.That(exception.Message, Does.Contain("narrowing is forbidden"));
        }

        [Test]
        public void RuntimePublicContractDoesNotExposeRcVec3f()
        {
            Type target = typeof(RcVec3f);
            Type[] offenders = typeof(NavigationQueryScheduler).Assembly
                .GetExportedTypes()
                .Where(type => type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Any(member => Exposes(member, target)))
                .ToArray();
            Assert.That(offenders, Is.Empty);
        }

        private static void SameBits(float expected, float actual)
        {
            Assert.That(
                BitConverter.SingleToInt32Bits(actual),
                Is.EqualTo(BitConverter.SingleToInt32Bits(expected)));
        }

        private static bool Exposes(MemberInfo member, Type target)
        {
            return member switch
            {
                PropertyInfo property => Contains(property.PropertyType, target),
                FieldInfo field => Contains(field.FieldType, target),
                MethodInfo method => Contains(method.ReturnType, target)
                                     || method.GetParameters().Any(parameter => Contains(parameter.ParameterType, target)),
                _ => false
            };
        }

        private static bool Contains(Type type, Type target)
        {
            if (type == target || (type.IsByRef && type.GetElementType() == target)) return true;
            return type.IsArray
                ? Contains(type.GetElementType(), target)
                : type.IsGenericType && type.GetGenericArguments().Any(argument => Contains(argument, target));
        }
    }
}
