using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using CustomNavigation.UnityAdapter;
using Jitter2.LinearMath;
using NUnit.Framework;
using UnityEngine;

namespace CustomNavigation.Editor.Tests
{
    public sealed class NavigationJitterApiContractTests
    {
        [Test]
        public void BreakingRuntimeCoordinateApiUsesCanonicalJVector()
        {
            Assert.That(
                typeof(NavigationPathResult).GetProperty(nameof(NavigationPathResult.Points))?.PropertyType,
                Is.EqualTo(typeof(JVector[])));
            Assert.That(
                typeof(NavigationServerPathResult).GetField(nameof(NavigationServerPathResult.Points))?.FieldType,
                Is.EqualTo(typeof(JVector[])));

            MethodInfo requestPath = typeof(NavigationQueryScheduler).GetMethod(
                nameof(NavigationQueryScheduler.RequestPath));
            Assert.That(
                requestPath?.GetParameters().Take(2).Select(parameter => parameter.ParameterType),
                Is.EqualTo(new[] { typeof(JVector), typeof(JVector) }));

            MethodInfo project = typeof(NavigationQueryScheduler).GetMethod(
                nameof(NavigationQueryScheduler.TryProjectPosition));
            Assert.That(project?.GetParameters()[0].ParameterType, Is.EqualTo(typeof(JVector)));
            Assert.That(project?.GetParameters()[1].ParameterType, Is.EqualTo(typeof(JVector).MakeByRefType()));

            MethodInfo fingerprint = typeof(NavigationPathFingerprint).GetMethod(
                nameof(NavigationPathFingerprint.Compute));
            Assert.That(
                fingerprint?.GetParameters().Single().ParameterType,
                Is.EqualTo(typeof(IReadOnlyList<JVector>)));
        }

        [Test]
        public void RuntimeAssemblyDeclaresNoPublicUnityVector3Contract()
        {
            Type vectorType = typeof(Vector3);
            Type[] offenders = typeof(NavigationQueryScheduler).Assembly
                .GetExportedTypes()
                .Where(type => type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Any(member => Exposes(member, vectorType)))
                .ToArray();

            Assert.That(offenders, Is.Empty);
        }

        [Test]
        public void UnityAdapterRoundTripsAndRejectsNonFiniteCoordinates()
        {
            var unity = new Vector3(1.25f, -2.5f, 9.75f);
            JVector jitter = NavigationUnityAdapter.ToJitter(unity);
            Assert.That(jitter.X, Is.EqualTo(unity.x));
            Assert.That(jitter.Y, Is.EqualTo(unity.y));
            Assert.That(jitter.Z, Is.EqualTo(unity.z));
            Assert.That(NavigationUnityAdapter.ToUnity(jitter), Is.EqualTo(unity));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => NavigationUnityAdapter.ToJitter(new Vector3(float.NaN, 0f, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => NavigationJitterValidation.RequireFinite(
                    new JVector(0f, float.PositiveInfinity, 0f),
                    "point"));
        }

        [Test]
        public void ExistingEnumOrdinalsRemainStable()
        {
            Assert.That((int)NavigationComputeMode.LocalOnly, Is.EqualTo(0));
            Assert.That((int)NavigationComputeMode.ServerOnly, Is.EqualTo(1));
            Assert.That((int)NavigationComputeMode.ServerPredicted, Is.EqualTo(2));
            Assert.That((int)NavigationQueryPriority.CriticalCorrection, Is.EqualTo(0));
            Assert.That((int)NavigationQueryPriority.PlayerImmediate, Is.EqualTo(1));
            Assert.That((int)NavigationQueryPriority.CombatBot, Is.EqualTo(2));
            Assert.That((int)NavigationQueryPriority.VisibleBot, Is.EqualTo(3));
            Assert.That((int)NavigationQueryPriority.BackgroundBot, Is.EqualTo(4));
            Assert.That((int)NavigationQueryPriority.Prewarm, Is.EqualTo(5));
        }

        private static bool Exposes(MemberInfo member, Type target)
        {
            return member switch
            {
                PropertyInfo property => Contains(property.PropertyType, target),
                FieldInfo field => Contains(field.FieldType, target),
                MethodInfo method => Contains(method.ReturnType, target)
                                     || method.GetParameters().Any(
                                         parameter => Contains(parameter.ParameterType, target)),
                _ => false
            };
        }

        private static bool Contains(Type type, Type target)
        {
            if (type == target || (type.IsByRef && type.GetElementType() == target))
            {
                return true;
            }

            return type.IsArray
                ? Contains(type.GetElementType(), target)
                : type.IsGenericType && type.GetGenericArguments().Any(argument => Contains(argument, target));
        }
    }
}
