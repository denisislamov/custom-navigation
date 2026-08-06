# Custom Navigation

Physics-free navigation for Unity, built on top of [DotRecast](https://github.com/ikpil/DotRecast)
(a C# port of Recast & Detour). The package provides everything needed to bake and
query navigation meshes **without** using Unity Physics or the built-in NavMesh.

## What's inside

| Assembly | Platform | Responsibility |
| --- | --- | --- |
| `CustomNavigation.Authoring` | Runtime | Level/agent/area authoring data (ScriptableObjects & components). |
| `CustomNavigation.Runtime` | Runtime | Navmesh artifact loader, budgeted query scheduler, server path client. |
| `CustomNavigation.NavigationEditor` | Editor | Offline baking, validation, inspectors and the Navigation editor window. |

The DotRecast managed DLLs ship under `Runtime/DotRecast`.

## Architecture

1. **Author** navigation using `NavigationLevel` and agent/area profiles.
2. **Bake** (editor-only) a deterministic binary artifact via the Navigation window.
3. **Query** the artifact at runtime through `NavigationQueryScheduler` (budgeted,
   allocation-friendly) or delegate to the authoritative HTTP server
   (`NavigationServerPathClient`).

Client / gameplay code (bots, waypoint routes, demo scenes) lives **outside** the
package assemblies so the package stays free of gameplay dependencies. A copy of that
code ships as an importable sample (see below).

## Installation

Install via Unity Package Manager → **Add package from git URL…**:

```
https://github.com/denisislamov/custom-navigation.git
```

Or add it to `Packages/manifest.json` directly:

```json
"com.datasakura.custom-navigation": "https://github.com/denisislamov/custom-navigation.git"
```

Pin a specific version with a tag:

```json
"com.datasakura.custom-navigation": "https://github.com/denisislamov/custom-navigation.git#v0.1.0"
```

## Sample: Navigation Demos & Bots

Package Manager → Custom Navigation → **Samples** → *Navigation Demos & Bots* → Import.

It contains `NavigationBotAgent`, `NavigationWaypointRoute`, demo presentation helpers
and editor menu items that **generate** the demo scenes (local / server / hybrid /
multi-level), so no scene assets with fragile GUID references are shipped.

> The sample assembly references `Unity.InputSystem`. Install
> `com.unity.inputsystem` before importing, or remove that reference from
> `CustomNavigation.Client.asmdef` after import.

## Runtime configuration

`NavigationServerSettings` is loaded via
`Resources.Load("CustomNavigation/NavigationServerSettings")`. To point the runtime at
your navigation server, create the asset in **your project** at
`Assets/Resources/CustomNavigation/NavigationServerSettings.asset`
(Create → Custom Navigation → Server Settings). Without it, built-in defaults are used.

## Third-party

DotRecast is distributed under the zlib license — see `Third Party Notices.md`.
Package code is MIT — see `LICENSE.md`.


