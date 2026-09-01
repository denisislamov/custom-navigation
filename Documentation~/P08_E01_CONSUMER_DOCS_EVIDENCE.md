# P08-E01 consumer and documentation evidence

## Consumer migration

- Local, server, hybrid and multi-level sample route state is `JVector`.
- Runtime/server source has no `Vector3Dto`, `ServerVector3`, `ServerPathRequest` or
  `ServerPathResponse` implementation.
- Sample HTTP calls use `NavigationServerPathClient`, which delegates to the shared strict codec.
- Editor path probe creates `NavigationPathRequest` and encodes/decodes it with the same shared
  codec as the .NET server.
- Remaining `Vector3`/`Vector3[]` uses are Unity authoring/presentation boundaries: mesh vertices,
  ray picking, Transform application, Scene View paths, Handles and UI comparison.
- `NavigationUnityAdapter` remains the only package-owned `Vector3`/`JVector` converter.

No serialized sample field was renamed or removed. `NavigationComputeMode` and
`NavigationQueryPriority` ordinals remain covered by reflection tests. `NavigationEditorApi`,
`NavigationPreviewApi`, `OpenServerTab` and `OpenArtifactsTab` compatibility tests remain enabled.

## Separate Jitter server launch

The Unity server launcher now resolves the one approved project-owned Jitter installation through
the same preflight, verifies the pinned Unsafe dependency, and passes its directory as
`CanonicalJitterRoot` to `dotnet run`. The terminal `run-server.sh` path requires explicit
`CUSTOM_NAVIGATION_JITTER_ROOT`; neither path vendors Jitter into Custom Navigation or resolves it
transitively from Jitter Physics Baker.

## Documentation and version decision

Package version is `0.7.0`, matching the intentional source-breaking `Vector3` → `JVector` API,
strict protocol v2 and artifact schema 2. This is not represented as a patch update.

Updated user-facing material covers:

- prerequisite-first installation with exact approved Jitter tag, package commit, ZIP hash and DLL
  hash;
- Quick Start and recipes using `JVector` plus the Unity presentation adapter;
- exact Runtime/API signatures and source-level `Real = System.Single` meaning;
- mandatory client artifact hash and compatibility identity failures;
- migration from private coordinate DTOs and `Vector3[]` routes;
- mandatory re-bake/re-export for schema 1;
- server CLI and Editor launch using the separately installed Jitter root;
- imported sample version path, troubleshooting and rollback boundaries.

The `v0.7.0` package publication is not proven by this epic. Fresh consumer import, PlayMode,
IL2CPP/AOT, device networking and publication remain later gates.

## Automated checks

- Markdown inventory: 29 user/evidence files scanned.
- Relative link targets: PASS.
- Code fences: balanced.
- Unresolved documentation placeholder tokens: zero.
- Current user docs: no stale `0.6.16`, schema-1 runtime declaration, old wire identity or
  `Vector3[] Points` contract.
- Owned source DTO scan: zero forbidden coordinate DTO implementations.
- Package metadata/LFS and `git diff --check`: PASS.

Authoritative compile/regression evidence is recorded in the P08 commit handoff; documentation-only
checks are not substituted for Unity or .NET execution.
