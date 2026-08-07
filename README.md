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

## The navigation server

The package ships the reference .NET 9 navigation server in `Server~`. Unity ignores
folders ending with `~`, which keeps the server sources out of the Unity compilation —
but it also means they live in the read-only `Library/PackageCache`, so the server
cannot be built or run in place. Install it into your project first:

**Tools → Custom Navigation → Navigation Editor → Server tab → Install navigation server**
(or **Tools → Custom Navigation → Server → Install Navigation Server**).

The server is copied to `<project>/NavigationServer`, next to `Assets`, so Unity never
compiles it. Installing also points `NavigationServerSettings.serverArtifactFolder` at
`NavigationServer/NavigationData`, so **Export for Server** writes where the server reads.

Then:

1. Build a level (**Build & Budgets** tab) and press **Export for Server**.
2. **Start server** in the Server tab. The first launch restores packages and compiles,
   so give it a few seconds.
3. **Check /health** to confirm which artifact is loaded.

The order does not matter: the server starts fine with an empty `NavigationData` and
reports `status: "no-artifact"` until you export. Re-exporting is picked up without a
restart.

### Which map answers a request

The server holds every level in its `NavigationData` folder and picks one per request:
`POST /path` uses `levelId` from the body when present, otherwise the map that
`active.manifest.json` points at - which *Export for Server* rewrites every time. With
several exports of one level, the newest wins. `GET /health` lists `availableLevels`,
and `GET /health?level=<levelId>` inspects a specific map.

The server runs as a child process of the editor, logs into the Unity Console and is
stopped when you quit Unity or press **Stop server**. Requires the
[.NET 9 SDK](https://dotnet.microsoft.com/download) on `PATH`.

Outside the editor, run it directly:

```bash
cd NavigationServer
./run-server.sh --listen "http://*:5079/"
```

Reinstalling from the package overwrites the sources but keeps `NavigationData`, so
baked artifacts survive a package update.

## Third-party

DotRecast is distributed under the zlib license — see `Third Party Notices.md`.
Package code is MIT — see `LICENSE.md`.


