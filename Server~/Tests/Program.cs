using CustomNavigation.Runtime;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: CanonicalJitterContractProbe <Jitter2.Core.dll>");
    return 2;
}

CanonicalJitterContract.ValidateInstalledFiles(new[] { args[0] });

static void Expect(CanonicalJitterErrorCode code, Action action)
{
    try
    {
        action();
        throw new InvalidOperationException($"Expected {code}, but validation succeeded.");
    }
    catch (CanonicalJitterValidationException exception) when (exception.Code == code)
    {
    }
}

Expect(
    CanonicalJitterErrorCode.MissingAssembly,
    () => CanonicalJitterContract.ValidateInstalledFiles(Array.Empty<string>()));

Expect(
    CanonicalJitterErrorCode.DoublePrecisionUnsupported,
    () => CanonicalJitterContract.ValidateMetadata(
        CanonicalJitterContract.ApprovedIdentity,
        true,
        true));

CanonicalJitterIdentity approved = CanonicalJitterContract.ApprovedIdentity;
var mismatched = new CanonicalJitterIdentity(
    approved.Repository,
    approved.Tag,
    approved.PackageCommit,
    new string('0', 64),
    approved.Precision,
    approved.SourceContentHash,
    approved.CompileProfileId,
    approved.StableMathCompatibilityId);
Expect(
    CanonicalJitterErrorCode.IdentityMismatch,
    () => CanonicalJitterContract.ValidateMetadata(mismatched, false, true));

string[] outputCopies = Directory.GetFiles(
    AppContext.BaseDirectory,
    "Jitter2.Core.dll",
    SearchOption.AllDirectories);
if (outputCopies.Length != 1)
{
    throw new InvalidOperationException(
        $"Expected exactly one copy-local Jitter2.Core.dll, got {outputCopies.Length}.");
}

Console.WriteLine(
    "P02_CANONICAL_JITTER_OK " +
    $"tag={CanonicalJitterContract.ApprovedTag} " +
    $"sha256={CanonicalJitterContract.ApprovedAssemblySha256} precision=f32 exactlyOne=true");
return 0;
