# Changelog

All notable changes to this package are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.6.13] - 2026-08-28

### Added
- Added a build summary with Ready/Changed/Invalid state, payload size, polygon/source counts,
  optional description and file UTC, Project/Finder actions, and copyable full diagnostics.
- Added an explicit, idempotent artifact filename migration that uses `AssetDatabase.MoveAsset`
  to preserve payload/manifest/asset GUIDs and serialized references.

### Changed
- New client and server exports use `<levelId>.navigation.bytes`,
  `<levelId>.navigation.manifest.json`, and `<levelId>.navigation.asset` inside the existing
  generated root. Full SHA-256 remains in the manifest and Details, not the normal filename.
- Client and server readers continue accepting legacy hash-based `.navmesh.bytes` artifacts.
  Folder export and HTTP upload now stage and roll back the complete payload/manifest/active set
  when a write fails.

### Tests
- Added current/legacy naming, GUID/reference/byte-preserving repeated migration, corruption,
  duplicate level ID, per-level path isolation, and incomplete-export rollback coverage.

## [0.6.12] - 2026-08-27

### Changed
- Reworked Navigation Performance around Mobile Low/Medium/High/Custom. The default Inspector
  now shows verified local-scheduler limits; working details live in Advanced, while route
  cache, memory target, workers, and production telemetry remain serialized under read-only
  Legacy / Diagnostics.
- Corrected field classification after auditing package runtime, reference server, bundled
  samples, and the known EFT consumer. Replan intervals are sample-consumer pacing values and
  Budget Warning Multiplier is active in `NavigationQuerySchedulerBehaviour`.
- Documented backlog, admission, priority eviction, cancellation, queue-only expiration, and
  result-buffer caps without changing scheduler semantics or adding a Server preset.

### Tests
- Added preset/legacy serialization coverage and scheduler tests for ordinary and overloaded
  queues, priority eviction, cancellation, deterministic expiration, and result limits.

## [0.6.11] - 2026-08-27

### Added
- Added the native Scene View `Custom Navigation` overlay with independently persisted
  Sources, Baked, and Runtime layers, level scope, Visible/X-Ray depth, Preferences access,
  status, and `Frame Level` controls.
- Added explicit `Not baked`, `Out of date`, and `No runtime data` preview states.

### Changed
- Replaced the legacy highlight presentation with a muted sand/violet/tobacco/plum palette,
  dotted source bounds, translucent baked surfaces, polygon boundaries, and directional
  runtime routes.
- Baked preview meshes are now prepared by invalidation callbacks and reused during Scene
  View repaint. Cached meshes and materials are released on invalidation, layer disable,
  assembly reload, and editor shutdown; source meshes are never substituted for baked data.

### Tests
- Added EditMode coverage for native Overlay registration and distinct personal preference
  keys. Existing Tools-menu coverage continues to reject a separate highlight toggle.

## [0.6.10] - 2026-08-27

### Fixed
- Removed the legacy `Tools/Custom Navigation/Navigation Highlight` menu registration.
  The package now exposes only `Tools/DataSakura/Custom Navigation Window`, while Scene
  Preview remains configurable from the window and user Preferences.

## [0.6.9] - 2026-08-27

### Added
- Added `Project Settings/DataSakura/Custom Navigation` for shared Agent, Areas,
  Runtime Query Budget, and local Bake Quality defaults. Assets are created only by the
  explicit `Create Defaults` action, which preserves existing profiles.
- Added `Preferences/DataSakura/Custom Navigation/Scene Preview` for personal preview state
  stored in `EditorPrefs`, separately from versioned project defaults.
- Added `Edit`, `New`, and `Make Local Copy` profile actions. Edit lists dependent scenes,
  prefabs, and loaded levels before opening a shared profile; local copies preserve values
  without changing other levels.

### Changed
- Newly created or repaired Navigation Levels reuse configured project defaults while
  preserving existing references, Undo/Redo, and prefab overrides.
- The Settings tab exposes project defaults and preview preferences even when no
  Navigation Level is selected. Bake Quality remains local to each level.

### Tests
- Added EditMode coverage for provider paths and scopes, no-write-on-open behavior,
  idempotent defaults, shared profiles, local copies, Undo, prefab overrides, and unchanged
  navigation payload bytes/hash across Runtime Query Budget changes.

## [0.6.8] - 2026-08-27

### Added
- Added an explicit `Remove baked navigation` action to the Bake section. Its confirmation
  dialog lists the generated artifact, payload, and manifest files before deleting them;
  server copies are deliberately left unchanged.
- Refused artifact deletion when any selected file is outside the package-owned
  `Assets/DataSakura/CustomNavigation/Generated/Navigation` folder.

### Changed
- Renamed the authoring window dock title to `DS Navigation` to distinguish it from Unity's
  built-in Navigation window and restore the title after editor domain reloads.

### Tests
- Added EditMode coverage for the dock title and deletion of the generated artifact triplet.

## [0.6.7] - 2026-08-27

### Changed
- Added the single `Tools/DataSakura/Custom Navigation` entry and reorganized the authoring
  window into `Overview`, `Geometry`, `Bake`, `Settings`, and `Diagnostics` sections while
  preserving the public automation entrypoints.
- Grouped Create Asset, Add Component, and sample prefab commands under
  `DataSakura/Custom Navigation`; generated navigation artifacts are no longer offered as
  hand-created configuration assets.
- Reworked the `NavigationLevel` Inspector into compact Level, Geometry Root, Settings, and
  Bake Status blocks with `Validate / Bake / Open` actions and an Advanced foldout.
- Moved the explicit pre-0.6.6 project layout migration from the Tools menu into Diagnostics.

### Tests
- Added EditMode coverage for the agreed menu paths, window sections, automation entrypoints,
  authoring menu groups, generated-artifact behavior, and side-effect-free window open/close.

## [0.6.6] - 2026-08-25

### Changed
- The package display name is now `DataSakura Custom Navigation`, so native samples import
  under `Assets/Samples/DataSakura Custom Navigation/<version>`.
- Generated assets and builder-created scenes now use
  `Assets/DataSakura/CustomNavigation/{Generated,Scenes}`.
- Added an explicit, idempotent GUID-preserving migration. It moves the legacy product root,
  renames its `Scene` folder to `Scenes`, finishes that rename after an interrupted migration,
  and refuses to merge conflicting roots or scene folders.

## [0.6.5] - 2026-08-18

### Added
- Public editor facade `NavigationBakeCommand` with typed validation and build results.
  Consumers can bake client navigation artifacts without reflection or access to the
  package-internal `NavigationArtifactBuilder` implementation.
- Server artifact loading now rejects rooted or traversing manifest file names before
  reading the payload outside `NavigationData`.

## [0.6.4] - 2026-08-10

### Added
- **`Server~/ONBOARDING.md` — developer onboarding for the navigation server.** The
  server shipped with usage docs only (`Server~/README.md`), so anyone who had to change
  its code had to reverse-engineer the module layout, the request/response contracts and
  the artifact rules from the sources. The new document covers the process lifecycle and
  CLI arguments, the full HTTP contract with JSON schemas and status codes, how
  `NavigationRegistry` picks a map and hot-reloads it, artifact validation and upload
  authorization, the determinism pitfalls between client and server (hard-coded
  `searchExtents (2, 4, 2)`, the 256/256 path limits and `DtQueryDefaultFilter`, none of
  which match the client), known limitations and a checklist of gotchas.
  It is installed together with the server, so it also lands in
  `<project>/NavigationServer` next to the code it describes.

### Fixed
- **`Server~/README.md` no longer contains a pasted stack trace.** The section explaining
  that a missing artifact is a normal first-run state had a `FileNotFoundException` dump
  (twice) spliced into the middle of a sentence, which described behaviour the server has
  not had since artifacts became lazily resolved in 0.5.0.

### Changed
- The package README points at both server documents and says what each is for.

> Note: 0.6.3 was never published to the package repository, so consumers move straight
> from 0.6.2 to 0.6.4 and pick up the 0.6.3 fix listed below.

## [0.6.3] - 2026-08-09

### Fixed
- **Importing the samples no longer creates a `NavigationServer/NavigationData` folder
  next to `Assets`.** The LocalOnly sample scene builders (`LocalBotsDemoSceneBuilder`,
  `MultiLevelDemoSceneBuilder`) called `NavigationArtifactBuilder.BuildAndExport`, which
  writes the baked artifact and `active.manifest.json` into the server data folder.
  Because those builders run from `[InitializeOnLoadMethod]` right after import, a fresh
  project got a server folder populated with `local_bots_arena` data even when no server
  was installed and no server demo was ever opened. Both builders now call
  `BuildForClient`, so they only produce the client artifact under
  `Assets/DataSakura/CustomNavigation/Generated/Navigation`.
  The server folder is created only by explicit user actions: *Server → Install*,
  *Export to Folder* and *Upload to Server* in the Navigation Editor.

### Changed
- `NavigationArtifactBuilder.BuildAndExport` is documented as an explicit user action
  and must not be called from importers or `[InitializeOnLoadMethod]` hooks.

## [0.6.2] - 2026-08-08

### Changed
- **The Tools → Custom Navigation menu was trimmed to the essentials**: *Navigation
  Editor* and *Create Bot Agent Prefab* (sample). Removed as menu items:
  - the *Server* submenu (Install / Start / Stop / Open Folder) - the Server tab of the
    Navigation Editor is the single place to drive the local server, with the state
    visible right next to the buttons;
  - every *Rebuild ... Scene* entry of the sample and *Rebuild Demo Hub Scene*;
  - the *Diagnostics* submenu (Artifact Roundtrip Test, Navigation Highlight Report,
    Review Demo Levels) and *Verify No Unity Physics*.
  The underlying methods are intact and callable from code; only the `[MenuItem]`
  attributes were removed, so the entries can come back at any time.

## [0.6.1] - 2026-08-07

### Changed
- The default `Server Artifact Folder` is now `NavigationServer/NavigationData` - the
  folder the installed server actually reads - instead of the legacy
  `DotRecastServer/NavigationData`, which was the server's location in the source
  repository before it moved into the package as `Server~` (v0.2.0). A freshly created
  settings asset used to point at a folder no consumer project ever had, and *Export to
  Folder* would write where nothing reads. Explicitly configured folders are untouched.
- The "Copy the launch command" snippet and the docs now reference
  `NavigationServer/run-server.sh` instead of the legacy path.

## [0.6.0] - 2026-08-07

### Added
- **Artifacts can be uploaded to the server over HTTP** with the new `POST /artifacts`.
  Writing into `NavigationData` only ever worked when the server shared a file system
  with the machine running Unity, which a remote or containerised server does not.
  **Upload to Server** (Build & Budgets tab and per row in Artifacts) pushes the baked
  navmesh to whatever address the settings asset points at, and the running server
  serves it immediately - no file copying, no restart.
  - The payload is fully validated *before* anything is written: schema, DotRecast
    version, SHA-256 and polygon count. A corrupt upload cannot leave a half-written map.
  - The artifact file name is taken from the manifest and refused unless it is a plain
    `<level>.<hash>.navmesh.bytes`, so an upload cannot escape the data folder.
  - Uploads are open on a loopback server, and require `--upload-token <secret>` plus a
    matching `X-Navigation-Token` header once the server listens on a real interface -
    it never silently accepts navmesh writes from the network.
  - The token is stored in EditorPrefs, not in the settings asset, so it is never
    shipped inside a player build.
- The Server tab has a **Choose folder...** picker for the server artifact folder, which
  previously could only be typed by hand.

### Fixed
- **The server artifact folder could silently diverge from the installed server.**
  Installing the server only repointed the folder when the settings asset already
  existed, so a project that created it afterwards kept the default
  `DotRecastServer/NavigationData` while the server ran on
  `NavigationServer/NavigationData` - and *Export for Server* wrote where nothing read.
  Installing now creates the settings asset when missing, and the Server tab warns about
  a mismatch and offers a one-click fix.

### Changed
- **Export for Server** is now **Export to Folder**, and its messages no longer claim the
  server needs a restart (it hot-reloads) or that the files reached a server that may
  read a different folder.

## [0.5.0] - 2026-08-07

### Fixed
- **The server crashed on startup when nothing had been exported yet**
  (`Unhandled exception. System.IO.FileNotFoundException: Navigation manifest was not
  found`). That was a chicken-and-egg trap: the server could not run before the first
  export, which is exactly when you want it running. A missing artifact is now a normal
  state - the server boots, listens, and reports it through `GET /health`
  (`status: "no-artifact"`) and in the `POST /path` response, with a message that says
  what to do about it.

### Added
- **The server now serves every exported level, not one map per process.**
  `POST /path` accepts `levelId` and answers on that map; without it the active manifest
  is used, so existing single-level clients are unaffected. When several exports of one
  level are present, the newest wins. Levels are loaded lazily and cached.
- **Hot reload.** The cache is keyed on the manifest timestamp, so re-running
  *Export for Server* is picked up without restarting the server.
- `GET /health` reports `dataDirectory`, `availableLevels` and a `message`, and accepts
  `?level=<levelId>` to inspect a specific map. The Server tab shows which levels are
  ready to serve, and warns instead of claiming OK when nothing is loaded.
- New `--data <folder>` argument selects the artifact folder. `--manifest` still exists
  and now means "pin this one map", which is what a dedicated instance wants.
- `NavigationServerPathClient.RequestPath` has an overload taking `levelId`.

### Changed
- **Start server** no longer refuses to launch when no artifact has been exported, and
  passes `--data` instead of `--manifest` so the running server can serve every level.

## [0.4.1] - 2026-08-07

### Fixed
- **The demo scenes rendered nothing in Play mode** ("Display 1 - No cameras rendering",
  no player, no bots, no path lines). `NavigationDemoIsometricCameraRig` is a
  `MonoBehaviour`, but it was declared inside `NavigationDemoPresentation.cs`. Unity
  only creates a `MonoScript` for the type whose name matches the file name, so
  `AddComponent<NavigationDemoIsometricCameraRig>()` returned `null`, logged
  "The referenced script (Unknown) on this Behaviour is missing!" and the very next
  field assignment threw a `NullReferenceException` - which aborted `Start()` before
  the camera, the agents and the materials were created. Every demo except the hub
  builds its camera through that rig, so all of them came up empty.
  The rig now lives in `NavigationDemoIsometricCameraRig.cs`.
- `NavigationDemoHubReturn` had the same defect (declared in `NavigationDemoHub.cs`),
  which broke the "Back to the level catalog" overlay. Moved to its own file.

## [0.4.0] - 2026-08-07

### Added
- **The standalone navigation server is now usable straight from the package.** It has
  always shipped inside the package as `Server~`, but that folder resolves to
  `Library/PackageCache/...`, which is read-only and wiped on reimport, so it could
  neither be built nor run in place.
  - `NavigationServerInstaller` copies the server into `<project>/NavigationServer`
    (next to `Assets`, so Unity never compiles its .NET sources), keeps existing
    `NavigationData` on reinstall, and points
    `NavigationServerSettings.serverArtifactFolder` at the installed copy so
    **Export for Server** writes exactly where the server reads.
  - `NavigationServerProcess` starts and stops the server as a child process of the
    editor, mirrors its stdout/stderr into the Unity Console, survives assembly
    reloads via `SessionState`, kills the whole `dotnet run` process tree on stop and
    shuts the server down on `EditorApplication.quitting`.
  - The **Server** tab of the Navigation Editor gained a *Local server* section:
    install / reinstall, start / stop, install path and live status, plus a .NET SDK
    availability check.
  - Menu shortcuts under **Tools → Custom Navigation → Server**: *Install Navigation
    Server*, *Start Server*, *Stop Server*, *Open Server Folder*.
- The editor passes `--listen` and `--manifest` to the server explicitly, and refuses
  to start it when no `active.manifest.json` has been exported yet - instead of
  letting the server fail with a file-not-found at startup.

## [0.3.3] - 2026-08-07

### Fixed
- **The DotRecast assemblies were shipped as Git LFS pointers**, which was the real
  cause of `CS0246: The type or namespace name 'DotRecast' could not be found` for
  anyone installing from a git URL. The source repository tracks `*.dll` with Git
  LFS, and Unity Package Manager clones a git package **without LFS support**, so
  each DLL arrived as a ~130-byte text stub instead of a 76-108 KB assembly.
  The package now carries its own `.gitattributes` that opts every binary out of
  LFS, so the assemblies travel as plain git blobs.
- `tools/publish-package.sh` inspects the blobs it is about to push and refuses to
  publish if any binary is still an LFS pointer.

## [0.3.2] - 2026-08-07

### Fixed
- **Package installed from a git URL failed to compile** (`CS0246: The type or
  namespace name 'DotRecast' could not be found`). Every `.meta` file in the package
  was truncated to `fileFormatVersion` + `guid` with no importer block. Unity can
  silently repair those for writable (embedded/local) packages, but
  `Library/PackageCache` is immutable, so the DotRecast DLLs were never registered
  as managed plugins and the asmdef `precompiledReferences` could not resolve.
  All `.meta` files now carry a complete `PluginImporter` / `MonoImporter` block
  (GUIDs unchanged, so existing references keep working).
- `DotRecast.Recast.dll` is now marked editor-only - the runtime never bakes, it
  only loads a prebuilt navmesh - which also keeps it out of player builds.

### Notes
- `package.json` had drifted behind the published tags (it still said `0.1.0` at
  `v0.3.0`/`v0.3.1`). The manifest version now matches the tag again, and
  `tools/publish-package.sh` verifies that before every release.
- Tags `v0.1.1`, `v0.2.0` and `v0.2.1` were mis-numbered duplicates of this line and
  have been removed; use `v0.1.0` → `v0.3.0` → `v0.3.1` → `v0.3.2`.

## [0.3.1] - 2026-08-07

### Changed
- Cosmetic cleanup of `LICENSE.md`.

## [0.3.0] - 2026-08-07

### Added
- Standalone .NET 9 navigation server sources (`Server~`) — the reference
  authoritative HTTP server for the `ServerOnly` / `ServerPredicted` modes,
  with its DotRecast DLLs, `run-server.sh` and API README.

### Fixed
- Demo scene builders now create the target scene folder before saving, so sample
  scene generation works in a clean consumer project.
- `Verify No Unity Physics` scans the package and imported sample locations instead
  of the pre-package folder layout, and treats the standalone server as optional.

## [0.1.0] - 2026-08-07

### Added
- Initial extraction of the Custom Navigation system into a UPM package
  (`com.datasakura.custom-navigation`), published at
  https://github.com/denisislamov/custom-navigation.
- Bundled DotRecast (Core, Detour, Recast) managed DLLs under `Runtime/DotRecast`.
- Assemblies: `CustomNavigation.Authoring`, `CustomNavigation.Runtime`,
  `CustomNavigation.NavigationEditor`.
- Importable sample *Navigation Demos & Bots* (`Samples~/Demos`) with the bot agent,
  waypoint routes and editor scene builders.
- `LICENSE.md` (MIT) and explicit Unity module dependencies in `package.json`.
- Separated gameplay/client logic (bot agent, waypoint routes, demo scenes) into the
  project-side `CustomNavigation.Client` / `CustomNavigation.Client.Editor` assemblies so
  the package has no dependency on gameplay code.
- `CustomNavigation.Runtime` no longer references `Unity.InputSystem` (input handling now
  belongs to the client assembly).
