# Changelog

All notable changes to this package are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

### Changed
- Separated gameplay/client logic (bot agent, waypoint routes, demo scenes) into the
  project-side `CustomNavigation.Client` / `CustomNavigation.Client.Editor` assemblies so
  the package has no dependency on gameplay code.
- `CustomNavigation.Runtime` no longer references `Unity.InputSystem` (input handling now
  belongs to the client assembly).

