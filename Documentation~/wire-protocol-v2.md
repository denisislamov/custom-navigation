# Navigation wire protocol v2

`POST /path` uses an explicit, fail-closed JSON contract. `JVector` is the in-memory coordinate
type, but it is never serialized by reflection or by `JsonUtility`/`System.Text.Json` field rules.
Both Unity and DotRecastServer execute the same `NavigationWireCodec.cs` source.

## Identity

- `protocolVersion`: `2`;
- `runtimeCompatibilityId`:
  `cn-jmp-v2-f32-54b456c04074909605d2ba138e5001d39a90a338885eafcb32265483b35054b0`;
- canonical Real profile: `f32`.

Both identity properties are mandatory in every request and response. Missing/v1/wrong identity
is rejected; there is no silent fallback endpoint.

## Canonical JSON

Coordinates have exactly three lowercase properties in writer order `x`, `y`, `z`:

```json
{"x":1.25,"y":0,"z":-2.5}
```

The request writer order is:

1. `protocolVersion`
2. `runtimeCompatibilityId`
3. `requestId`
4. `levelId`
5. `start`
6. `destination`
7. `clientArtifactHash`
8. `clientPathFingerprint`

The response writer order is:

1. `protocolVersion`
2. `runtimeCompatibilityId`
3. `success`
4. `points`
5. `message`
6. `requestId`
7. `artifactHash`
8. `pathFingerprint`
9. `serverMismatchDetected`

The writer uses invariant round-trip f32 formatting, emits no whitespace and canonicalizes both
`-0` and `+0` to `0`. Strings use JSON escaping. The reader accepts property reordering but rejects
unknown and duplicate properties, so every semantic field has one owner.

## Fail-closed categories

`NavigationWireFormatException.Code` distinguishes invalid JSON, missing, duplicate or unexpected
properties, invalid/non-finite/overflow numbers, protocol mismatch and runtime compatibility
mismatch. Coordinate objects missing any of `x/y/z`, or containing `NaN`, `Infinity`, an f32
overflow or an invalid number token, never reach a navigation query.

## Compatibility decision

The legacy payload kept the same `x/y/z` coordinate shape but had neither `protocolVersion` nor
`runtimeCompatibilityId`. It is intentionally not protocol-v2 compatible. A legacy request fails
with `MissingProperty`; a request explicitly claiming version 1 fails with `ProtocolMismatch`.
Endpoints and existing business field names remain unchanged.
