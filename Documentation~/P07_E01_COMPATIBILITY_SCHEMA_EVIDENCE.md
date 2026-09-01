# P07-E01 compatibility and schema evidence

## Canonical identity

`NavigationCompatibilityContract` is the single source used by the baker, Unity runtime, strict
wire codec, and source-linked .NET server:

| JSON / asset field | Canonical value | Owner |
|---|---|---|
| `schemaVersion` | `2` | navigation compatibility contract |
| `dotRecastVersion` | `2026.1.3` | navigation compatibility contract |
| `precision` | `f32` | approved canonical Jitter contract |
| `canonicalJitterAssemblySha256` | `944666bbe73dfce5ffc5bfb18569fb0004f50e767dcbb8b471dde15242023ca6` | approved P00 release |
| `deterministicMathCompatibilityId` | `54b456c04074909605d2ba138e5001d39a90a338885eafcb32265483b35054b0` | approved public StableMath |
| `fingerprintAlgorithmVersion` | `2` | P06 fingerprint contract |
| `fingerprintAlgorithmId` | `cn-path-fingerprint-v2-mm-away-from-zero-stablemath-f32` | P06 fingerprint contract |

The manifest writer declares fields in that order before level/content metadata, so Unity
`JsonUtility` emits a deterministic representation. `NavigationArtifactAsset` stores the same
values. Health and artifact-list responses expose the identity; protocol-v2 path request/response
objects expose precision, Jitter hash, math id, and fingerprint version in addition to the aggregate
`runtimeCompatibilityId`.

## Lifecycle and fail-closed order

1. Bake writes schema 2 and all identity fields into the client manifest and asset.
2. Export keeps the exact manifest JSON and payload bytes.
3. Unity loader validates all identity fields before checking/reading the payload.
4. Server `Create` validates all identity fields before payload hash, DotRecast deserialization, or
   polygon inspection.
5. Strict wire decode rejects protocol/precision/Jitter/math/fingerprint mismatches before the
   registry resolves a route.
6. `ServerNavigation.FindPath` validates a non-empty exact artifact hash before entering its
   DotRecast query lock. Missing or different hashes return `success=false` and
   `serverMismatchDetected=true`.

No local fallback is introduced by this epic. Diagnostics identify the exact mismatched field and
show expected/actual values.

## Negative matrix

The same `NavigationCompatibilityConformanceFixtures` source runs in Unity and .NET and checks:

- legacy schema 1;
- DotRecast version;
- f32/f64 precision;
- canonical Jitter assembly SHA-256;
- StableMath compatibility id;
- fingerprint version;
- fingerprint id;
- missing client artifact hash;
- wrong client artifact hash.

Expected marker:

`P07_COMPATIBILITY_MATRIX_OK positives=3 negatives=9 payload=unchanged manifest=changed`

The shared strict wire corpus adds four independent request failures for precision, Jitter, math,
and fingerprint version. Expected marker:

`P04_WIRE_CONFORMANCE_OK valid=4 invalid=15`

## Legacy and byte decision

Schema 1 has no precision/Jitter/math/fingerprint identity and is therefore never upgraded in
place or accepted with defaults. The diagnostic explicitly requires **re-bake and re-export**.

The DotRecast payload schema is unchanged by P07: identity is stored only in the manifest and
`NavigationArtifactAsset`. The cross-runtime fixture hashes the payload before/after the schema
transition and requires identical bytes, while requiring the old and new manifest UTF-8 bytes to
differ. This proves payload and manifest compatibility separately; it does not claim that a future
re-bake will reproduce an old payload after P06 deterministic authoring changes.

## Regression commands

```bash
dotnet run --project Packages/com.datasakura.custom-navigation/Server~/Tests/NavigationCompatibilityConformanceProbe.csproj \
  -c Release -p:CanonicalJitterRoot=/absolute/path/to/approved-jitter --disable-build-servers

dotnet run --project Packages/com.datasakura.custom-navigation/Server~/Tests/NavigationWireConformanceProbe.csproj \
  -c Release -p:CanonicalJitterRoot=/absolute/path/to/approved-jitter --disable-build-servers

dotnet build Packages/com.datasakura.custom-navigation/Server~/DotRecastServer.csproj \
  -c Release -p:CanonicalJitterRoot=/absolute/path/to/approved-jitter --disable-build-servers
```
