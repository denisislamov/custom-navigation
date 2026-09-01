# Navigation wire protocol v2

`POST /path` uses an explicit, fail-closed JSON contract. `JVector` is the in-memory coordinate
type, but it is never serialized by reflection or by `JsonUtility`/`System.Text.Json` field rules.
Both Unity and DotRecastServer execute the same `NavigationWireCodec.cs` source.

## Identity

- `protocolVersion`: `2`;
- `runtimeCompatibilityId`:
  `cn-jmp-v2-f32-jitter-944666bb-math-54b456c0-fingerprint-v2`;
- `precision`: `f32`;
- `canonicalJitterAssemblySha256`:
  `944666bbe73dfce5ffc5bfb18569fb0004f50e767dcbb8b471dde15242023ca6`;
- `deterministicMathCompatibilityId`:
  `54b456c04074909605d2ba138e5001d39a90a338885eafcb32265483b35054b0`;
- `fingerprintAlgorithmVersion`: `2`.

All identity properties are mandatory in every request and response. Missing/v1/wrong identity
is rejected; there is no silent fallback endpoint.

## Canonical JSON

Coordinates have exactly three lowercase properties in writer order `x`, `y`, `z`:

```json
{"x":1.25,"y":0,"z":-2.5}
```

The request writer order is:

1. `protocolVersion`
2. `runtimeCompatibilityId`
3. `precision`
4. `canonicalJitterAssemblySha256`
5. `deterministicMathCompatibilityId`
6. `fingerprintAlgorithmVersion`
7. `requestId`
8. `levelId`
9. `start`
10. `destination`
11. `clientArtifactHash`
12. `clientPathFingerprint`

The response writer order is:

1. `protocolVersion`
2. `runtimeCompatibilityId`
3. `precision`
4. `canonicalJitterAssemblySha256`
5. `deterministicMathCompatibilityId`
6. `fingerprintAlgorithmVersion`
7. `success`
8. `points`
9. `message`
10. `requestId`
11. `artifactHash`
12. `pathFingerprint`
13. `serverMismatchDetected`

The writer uses invariant round-trip f32 formatting, emits no whitespace and canonicalizes both
`-0` and `+0` to `0`. Strings use JSON escaping. The reader accepts property reordering but rejects
unknown and duplicate properties, so every semantic field has one owner.

## Fail-closed categories

`NavigationWireFormatException.Code` distinguishes invalid JSON, missing, duplicate or unexpected
properties, invalid/non-finite/overflow numbers, protocol mismatch and runtime compatibility
mismatch. Coordinate objects missing any of `x/y/z`, or containing `NaN`, `Infinity`, an f32
overflow or an invalid number token, never reach a navigation query. Identity errors additionally
distinguish precision, canonical Jitter, deterministic math and fingerprint algorithm mismatch.
The server requires a non-empty exact `clientArtifactHash` and rejects it before entering the
DotRecast query lock.

## Compatibility decision

The legacy payload kept the same `x/y/z` coordinate shape but had neither `protocolVersion` nor
`runtimeCompatibilityId`. It is intentionally not protocol-v2 compatible. A legacy request fails
with `MissingProperty`; a request explicitly claiming version 1 fails with `ProtocolMismatch`.
Endpoints and existing business field names remain unchanged.
