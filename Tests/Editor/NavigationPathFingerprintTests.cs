using System;
using CustomNavigation.Runtime;
using CustomNavigation.Tests.Shared;
using Jitter2.LinearMath;
using NUnit.Framework;

namespace CustomNavigation.Editor.Tests
{
    public sealed class NavigationPathFingerprintTests
    {
        [Test]
        public void SharedUnityAndDotNetGoldenCorpusPasses()
        {
            Assert.That(
                NavigationPathFingerprintFixtures.Run(),
                Is.EqualTo("P06_FINGERPRINT_GOLDEN_OK version=2 fixtures=6 negatives=1"));
        }

        [Test]
        public void NullAndNonFiniteInputsFailClosed()
        {
            Assert.Throws<ArgumentNullException>(() => NavigationPathFingerprint.Compute(null));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => NavigationPathFingerprint.Compute(new[] { new JVector(float.NaN, 0f, 0f) }));
        }
    }
}
