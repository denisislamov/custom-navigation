using System;
using System.IO;
using CustomNavigation.Runtime;
using NUnit.Framework;
using UnityEditor;

namespace CustomNavigation.Editor.Tests
{
    public sealed class CanonicalJitterContractTests
    {
        [Test]
        public void ApprovedProjectOwnedAssemblyPassesFullPreflight()
        {
            CanonicalJitterEditorPreflight.EnsureReady();
            CanonicalJitterContract.ValidateLoadedAssembly();
        }

        [Test]
        public void MissingAssemblyFailsWithTypedError()
        {
            CanonicalJitterValidationException exception = Assert.Throws<CanonicalJitterValidationException>(
                () => CanonicalJitterContract.ValidateInstalledFiles(Array.Empty<string>()));
            Assert.That(exception.Code, Is.EqualTo(CanonicalJitterErrorCode.MissingAssembly));
            Assert.That(exception.Message, Does.Contain("Install the approved"));
        }

        [Test]
        public void DuplicateAssemblyFailsWithTypedError()
        {
            string installed = FindInstalledAssembly();
            string root = Path.Combine(Path.GetTempPath(), "custom-navigation-p02-duplicate");
            string first = Path.Combine(root, "a", "Jitter2.Core.dll");
            string second = Path.Combine(root, "b", "Jitter2.Core.dll");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(first));
                Directory.CreateDirectory(Path.GetDirectoryName(second));
                File.Copy(installed, first, true);
                File.Copy(installed, second, true);

                CanonicalJitterValidationException exception =
                    Assert.Throws<CanonicalJitterValidationException>(
                        () => CanonicalJitterContract.ValidateInstalledFiles(new[] { first, second }));
                Assert.That(exception.Code, Is.EqualTo(CanonicalJitterErrorCode.DuplicateAssembly));
                Assert.That(exception.Message, Does.Contain("exactly one"));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void TamperedAssemblyFailsBeforeDeterministicWork()
        {
            string root = Path.Combine(Path.GetTempPath(), "custom-navigation-p02-tamper");
            string tampered = Path.Combine(root, "Jitter2.Core.dll");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllBytes(tampered, new byte[] { 1, 2, 3, 4 });
                CanonicalJitterValidationException exception =
                    Assert.Throws<CanonicalJitterValidationException>(
                        () => CanonicalJitterContract.ValidateInstalledFiles(new[] { tampered }));
                Assert.That(exception.Code, Is.EqualTo(CanonicalJitterErrorCode.AssemblyHashMismatch));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void F64MetadataFailsWithTypedError()
        {
            CanonicalJitterValidationException exception = Assert.Throws<CanonicalJitterValidationException>(
                () => CanonicalJitterContract.ValidateMetadata(
                    CanonicalJitterContract.ApprovedIdentity,
                    true,
                    true));
            Assert.That(exception.Code, Is.EqualTo(CanonicalJitterErrorCode.DoublePrecisionUnsupported));
            Assert.That(exception.Message, Does.Contain("precision=f32"));
        }

        [Test]
        public void IdentityMismatchFailsWithTypedError()
        {
            CanonicalJitterIdentity approved = CanonicalJitterContract.ApprovedIdentity;
            var mismatch = new CanonicalJitterIdentity(
                approved.Repository,
                approved.Tag + "-other",
                approved.PackageCommit,
                approved.AssemblySha256,
                approved.Precision,
                approved.SourceContentHash,
                approved.CompileProfileId,
                approved.StableMathCompatibilityId);

            CanonicalJitterValidationException exception = Assert.Throws<CanonicalJitterValidationException>(
                () => CanonicalJitterContract.ValidateMetadata(mismatch, false, true));
            Assert.That(exception.Code, Is.EqualTo(CanonicalJitterErrorCode.IdentityMismatch));
        }

        [Test]
        public void RuntimeAsmdefDeclaresDirectJitterReferenceWithoutPackageDependency()
        {
            string packageRoot = "Packages/com.datasakura.custom-navigation";
            string asmdef = File.ReadAllText(
                packageRoot + "/Runtime/CustomNavigation.Runtime.asmdef");
            string packageJson = File.ReadAllText(packageRoot + "/package.json");

            Assert.That(asmdef, Does.Contain("Jitter2.Core.dll"));
            Assert.That(packageJson, Does.Not.Contain("jitter-physics-baker"));
            Assert.That(packageJson, Does.Not.Contain("Jitter2.Core"));
        }

        private static string FindInstalledAssembly()
        {
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (path.StartsWith("Assets/", StringComparison.Ordinal)
                    && string.Equals(
                        Path.GetFileName(path),
                        "Jitter2.Core.dll",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(path);
                }
            }

            throw new AssertionException("Test project has no separately installed Jitter2.Core.dll.");
        }
    }
}
