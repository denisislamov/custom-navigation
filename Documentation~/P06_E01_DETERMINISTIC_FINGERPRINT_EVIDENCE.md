# P06-E01 deterministic fingerprint evidence

## Contract

- Owner: `CustomNavigation.Runtime.NavigationPathFingerprint`, source-linked into the server build.
- Algorithm version: `2`.
- Algorithm id: `cn-path-fingerprint-v2-mm-away-from-zero-stablemath-f32`.
- Input: ordered `IReadOnlyList<JVector>` using the approved f32 canonical Jitter profile.
- Quantization: `StableMath.QuantizeToInt64(component, 1000f)`, millimetres, half-way away from zero.
- Canonical form: invariant decimal signed integers separated by `,`, one `;` per point, UTF-8 without BOM.
- `+0` and `-0` both canonicalize to integer `0`.
- Digest: SHA-256 rendered as 64 lowercase hexadecimal characters.
- Non-finite coordinates and null paths fail closed before hashing.

## Frozen v1 baseline before migration

The removed client/server implementations promoted each f32 coordinate to `double`, multiplied by
`1000d`, and used `Math.Round(..., AwayFromZero)`. The following pre-hash values and hashes were
captured before replacement:

| Case | v1 canonical text | v1 SHA-256 |
|---|---|---|
| empty | empty byte sequence | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| zero/near-zero | `0,0,0;` | `898d8af81ecf99eb26a0c523f19e65adad6dba00eba600bb74e106f6874326e9` |
| positive half-way | `1,2,1235;` | `d6d3b3116e6bb6ebc92ac0f1c5408c1485b91be8dab7ed747c1add304db814eb` |
| negative half-way | `-1,-2,-1235;` | `0b1cd79657fdff0c01b4787a18b2dc229e5978fc6d90bf0215045f3a4b33bc45` |
| large finite | `123456789,-123456789,100199578;` | `6b2a434c3ad522920cd5b6513880f24741d4eb90dcbf8e7e25e0650efeee40df` |

The v2 corpus proves that half-way, signed-zero, near-zero, empty, single, and multiple-path
semantics remain stable. Large coordinates deliberately differ because approved `StableMath`
operates in canonical f32: the v2 large canonical text is
`123456792,-123456792,100199576;`. This observable byte change requires algorithm version 2.

## Cross-runtime proof

`NavigationPathFingerprintFixtures` is compiled unchanged into the Unity EditMode fixture assembly
and the standalone .NET probe. Each case compares both canonical UTF-8 bytes (hex) and lowercase
SHA-256, so matching only the final digest cannot hide a serialization difference.

Run the .NET proof:

```bash
dotnet run --project Packages/com.datasakura.custom-navigation/Server~/Tests/NavigationPathFingerprintProbe.csproj \
  -c Release \
  -p:CanonicalJitterRoot=/absolute/path/to/DataSakura.Jitter2.Core-2.8.9-datasakura.1-rc.1 \
  --disable-build-servers
```

Expected marker: `P06_FINGERPRINT_GOLDEN_OK version=2 fixtures=6 negatives=1`.

## Deterministic math boundary audit

Migrated to approved `StableMath`:

- path fingerprint quantization;
- artifact-affecting region-area rounding and canonical geometry rounding;
- sample route height aggregation, arena bounds, and analytic click-ray intersection decisions.

Allowed host-native exceptions do not enter path, artifact, or compatibility identity:

- `Stopwatch` in query scheduling, editor probes, and progress UI measures elapsed time only;
- `DateTime`/file timestamps label logs and invalidate caches only;
- `Math.Min`/`Math.Max` over integer counts controls bounded work, not simulation values;
- remaining `Mathf` in Inspector, Handles, diagnostic navmesh analysis, camera/UI layout, line-marker
  rendering, and editor mesh visualization is presentation or non-authoritative diagnostics.

P07 must include `AlgorithmVersion` and `AlgorithmId` in compatibility/schema validation and must
fail closed against v1 artifacts or peers until they are re-baked/upgraded.
