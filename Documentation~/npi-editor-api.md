# NPI editor integration API

Custom Navigation remains standalone. Consumer tools such as NPI call the editor-only assembly;
the package never references the consumer, EFT, a physics package, or a gameplay server.

## Contract and ownership

Import `CustomNavigation.NavigationEditor` from the consumer's Editor asmdef and use:

```csharp
using CustomNavigation.Authoring;
using CustomNavigation.Editor.Api;

NavigationEditorResult standalone = NavigationEditorApi.Validate(level);

NavigationLevelIdBinding managedId =
    NavigationLevelIdBinding.External("NPI", definition.LevelId);
NavigationEditorResult validation = NavigationEditorApi.Validate(level, managedId);
NavigationEditorResult bake = NavigationEditorApi.Bake(level, managedId);
NavigationEditorResult current = NavigationEditorApi.ReadSummary(level, managedId);
```

`Standalone` uses `NavigationLevel.LevelId`. `External(owner, levelId)` supplies a canonical ID
for that call only. It does not serialize or otherwise change the standalone ID. Empty owners,
non-canonical IDs, and an ID already owned by another loaded `NavigationLevel` fail validation;
`Bake` returns before writing an artifact in those cases.

`NavigationEditorApi.Bake` is only the navigation bake. It does not export to or start the optional
HTTP navigation server, trigger physics, or run an NPI pipeline. Existing
`NavigationBakeCommand.Validate/Execute` entrypoints remain supported for older callers.

## Read-only result

`NavigationEditorResult` exposes:

- resolved `LevelId`, `Ownership`, and diagnostic `Owner`;
- `Artifact` reference plus `ArtifactPath`, `PayloadPath`, and `ManifestPath`;
- full payload `Digest` (lowercase SHA-256);
- `Status`, `Issues`, and `Succeeded`;
- payload byte size, polygon count, and source-mesh count when available.

`ReadSummary` verifies the payload hash/schema and manifest agreement. It never assigns an ID,
bakes, imports assets, changes preview settings, or writes files. `Missing` means no current client
artifact. `Changed` means the saved artifact is valid but current scene/source state is newer; it is
not considered successful for an NPI export gate. `Failed` means identity, metadata, manifest, or
payload validation failed.

## Shared preview state

```csharp
NavigationPreviewState state = NavigationPreviewApi.Current; // read only
NavigationPreviewApi.Apply(state.WithBaked(true).WithDepth(NavigationPreviewDepth.XRay));
```

`NavigationPreviewApi` reads and updates the existing Custom Navigation Overlay preferences. It
does not create a second set of toggles. Reading `Current` writes nothing and raises no event;
`Apply` raises the shared `Changed` event once when the complete state differs.

## Consumer fixture

`Samples~/Demos/Editor/NavigationEditorApiExample.cs` is the minimal no-server caller. Focused
coverage lives in `Tests/Editor/NavigationEditorApiTests.cs` and includes standalone/managed IDs,
conflict rejection before writes, delivery summary fields, preview sharing, and dependency checks.

## Revision receipt

- Package/API version prepared for handoff: `0.6.14`.
- Source baseline before CN-06: commit `02d8976` (`0.6.13`).
- CN-06 is not a verified remote revision until these changes are committed and tagged. NPI-01
  must record the resulting commit/tag, not assume that `main` or `latest` is the tested revision.
- The optional package HTTP navigation server was not changed by CN-06. The EFT gameplay server
  is outside this contract.

Verification evidence belongs to the release commit/tag handoff. A successful C# assembly compile
does not replace Unity Test Framework XML, standalone import, runtime, or server evidence.
