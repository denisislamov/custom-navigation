# Package folder unification handoff (0.6.6)

Date: 2026-08-25

This release keeps the package id, public namespaces, assembly names, navigation artifact
schema, DotRecast version and server wire contracts unchanged. It changes only the package
display name, project-owned output paths and the public editor bake facade introduced for
consumer integrations.

## Paths before and after

| Purpose | Through 0.6.5 | From 0.6.6 |
| --- | --- | --- |
| UPM display name | `Custom Navigation` | `DataSakura Custom Navigation` |
| Imported sample | `Assets/Samples/Custom Navigation/<version>/Navigation Demos & Bots` | `Assets/Samples/DataSakura Custom Navigation/<version>/Navigation Demos & Bots` |
| Product root | `Assets/CustomNavigation` | `Assets/DataSakura/CustomNavigation` |
| Builder scenes | `Assets/CustomNavigation/Scene` | `Assets/DataSakura/CustomNavigation/Scenes` |
| Generated navigation | `Assets/CustomNavigation/Generated/Navigation` | `Assets/DataSakura/CustomNavigation/Generated/Navigation` |
| Runtime settings asset | `Assets/CustomNavigation/Resources/CustomNavigation/NavigationServerSettings.asset` | `Assets/DataSakura/CustomNavigation/Resources/CustomNavigation/NavigationServerSettings.asset` |
| Server export | `NavigationServer/NavigationData` | unchanged |

```text
Assets/
├── DataSakura/
│   └── CustomNavigation/
│       ├── Generated/
│       ├── Resources/
│       └── Scenes/
└── Samples/
    └── DataSakura Custom Navigation/
        └── 0.6.6/
            └── Navigation Demos & Bots/
```

## Fresh install and upgrade behavior

- Unity Package Manager remains the only sample importer. The package source stays at
  `Samples~/Demos`; no post-import mover or second sample copy is created.
- Upgrade is an explicit action: **Tools > Custom Navigation > Migrate pre-0.6.6 project
  folders**.
- Both the product-root move and `Scene` to `Scenes` rename use
  `AssetDatabase.MoveAsset`, preserving folder and child asset GUIDs.
- Re-running migration is a no-op. A partially completed scene-folder rename is finished
  on the next run.
- If both source and destination roots, or both `Scene` and `Scenes`, exist, migration
  stops before merging and reports the exact conflicting paths.
- Previously imported versioned samples are not renamed by the migration.

## Source changes

- `package.json`, README and CHANGELOG describe the 0.6.6 native sample layout.
- authoring settings, editor builders, validators and sample scene builders use the new
  `Assets/DataSakura/CustomNavigation` root.
- `CustomNavigationLayoutMigration` provides explicit conflict-safe upgrade handling.
- `NavigationBakeCommand` exposes typed public validation and client-bake results without
  widening the internal artifact builder API.
- package editor tests cover manifest/sample metadata, fresh-project idempotence,
  GUID-preserving upgrade, conflict refusal and interrupted-upgrade recovery.
- server data folders, artifact bytes, schema `1`, DotRecast `2026.1.3` and HTTP contracts
  are unchanged.

## Validation record

From the source repository root on Unity `6000.3.11f1`:

```text
python3 tools/verify-package-meta.py
  PASS: complete .meta files, no Git LFS pointers

dotnet build CustomNavigation.NavigationEditor.csproj --no-restore \
  --disable-build-servers -m:1 /p:BuildInParallel=false \
  /p:UseSharedCompilation=false
  PASS: 0 warnings, 0 errors

dotnet build Packages/com.datasakura.custom-navigation/Server~/DotRecastServer.csproj \
  -c Release --no-restore --disable-build-servers -m:1 \
  /p:BuildInParallel=false /p:UseSharedCompilation=false
  PASS: 0 warnings, 0 errors

Unity -batchmode -nographics -runTests -testPlatform EditMode \
  -testFilter CustomNavigation.Editor.Tests
  PASS: 5/5
```

The first filtered Unity attempt exposed an ambiguous `PackageInfo` test type and was
fixed before the passing run. An initial licensing-client protocol mismatch recovered by
launching the editor-matched client; it was not counted as test success.

The package-source run does not mutate the development project's legacy `Assets` tree and
does not rename an already imported sample. Fresh consumer import, generated demo runtime,
EFT content hashes and the EFT two-client `[EPIC-5] PASS` must be recorded in the consumer
repository; they are not claimed by this source-package validation. No physics/navigation
payload was rebuilt here, so no content hash or `contentId` changed as part of this release.
