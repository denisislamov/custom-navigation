# Changelog

All notable changes to this package are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-08-07

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

