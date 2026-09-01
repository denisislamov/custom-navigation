using CustomNavigation.Tests.Shared;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CustomNavigation.Editor.Tests
{
    public sealed class NavigationCompatibilityContractTests
    {
        [Test]
        public void SharedUnityAndDotNetMismatchMatrixPasses()
        {
            Assert.That(
                NavigationCompatibilityConformanceFixtures.Run(),
                Is.EqualTo("P07_COMPATIBILITY_MATRIX_OK positives=3 negatives=9 payload=unchanged manifest=changed"));
        }

        [Test]
        public void RuntimeLoaderRejectsLegacySchemaBeforeReadingPayload()
        {
            var artifact = ScriptableObject.CreateInstance<NavigationArtifactAsset>();
            try
            {
                artifact.Configure("legacy", "hash", "1", NavigationCompatibilityContract.DotRecastVersion,
                    NavigationCompatibilityContract.Precision,
                    NavigationCompatibilityContract.CanonicalJitterAssemblySha256,
                    NavigationCompatibilityContract.DeterministicMathCompatibilityId,
                    NavigationCompatibilityContract.FingerprintAlgorithmVersion,
                    NavigationCompatibilityContract.FingerprintAlgorithmId,
                    "agent", 1, 1, null, "{}");
                NavigationCompatibilityException exception =
                    Assert.Throws<NavigationCompatibilityException>(() => NavigationArtifactLoader.Load(artifact));
                Assert.That(exception.Field, Is.EqualTo(NavigationCompatibilityField.SchemaVersion));
                Assert.That(exception.Message, Does.Contain("re-baked and re-exported"));
            }
            finally
            {
                Object.DestroyImmediate(artifact);
            }
        }
    }
}
