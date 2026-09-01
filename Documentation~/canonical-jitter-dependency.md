# Canonical Jitter dependency

Custom Navigation `0.7.x` requires one separately installed canonical `Jitter2.Core`. Jitter is not
an automatic UPM dependency and is not supplied transitively by Jitter Physics Baker.

## Approved immutable release

| Field | Value |
| --- | --- |
| Repository | `https://github.com/denisislamov/jitter-physics-baker` |
| Tag | `jitter-v2.8.9-datasakura.1-rc.1` |
| Package commit | `508de73d6d82088d58a74fd41d7e09b70f009b1d` |
| Asset | `DataSakura.Jitter2.Core-2.8.9-datasakura.1-rc.1.zip` |
| Asset SHA-256 | `61896c9d63e6262c113c9c353773b36b1825b10a3630d1f9b4eb05af07977bab` |
| Jitter2.Core.dll SHA-256 | `944666bbe73dfce5ffc5bfb18569fb0004f50e767dcbb8b471dde15242023ca6` |
| Precision | `f32` |
| Source content hash | `sha256:749c79e40c4965cd455ca80a2d1d1c80a24eb580eb7b721e07adc78b41c82762` |
| Compile profile ID | `a2925211b983330117414426be9bf8a2798ce9169c1206e1e55178f708cfa72e` |
| StableMath compatibility ID | `54b456c04074909605d2ba138e5001d39a90a338885eafcb32265483b35054b0` |

Release URL:
`https://github.com/denisislamov/jitter-physics-baker/releases/tag/jitter-v2.8.9-datasakura.1-rc.1`.

## Unity installation order

1. Download the ZIP and detached `.sha256` from the approved release.
2. Verify the detached checksum before extraction.
3. Extract into a staging directory.
4. Copy `Jitter2.Core.dll`, `Jitter2.Core.xml` and
   `System.Runtime.CompilerServices.Unsafe.dll` into one project-owned plugin folder under
   `Assets/`, for example `Assets/Plugins/DataSakura/Jitter2/`.
5. Let Unity generate/import metadata and confirm there is exactly one `Jitter2.Core.dll` in the
   entire Asset Database.
6. Only then install/compile Custom Navigation.

The existing Jitter Physics Baker **Install Jitter2** action may perform step 4, but Baker is not a
dependency source: Custom Navigation validates the resulting project-owned DLL against the release
hash and never references the Baker package.

`CustomNavigation.Runtime.asmdef` declares a direct precompiled reference to `Jitter2.Core.dll`.
Missing Jitter therefore fails at compile time. Duplicate, wrong hash, f64, private StableMath or
identity mismatch also fail the runtime/editor preflight before bake, artifact load or query work.

## .NET server

Extract the same ZIP outside the Custom Navigation package and pass its root explicitly:

```sh
dotnet build Server~/DotRecastServer.csproj -c Release \
  -p:CanonicalJitterRoot=/absolute/path/DataSakura.Jitter2.Core-2.8.9-datasakura.1-rc.1
```

The server project has direct copy-local references to `Jitter2.Core.dll` and the pinned Unsafe
dependency. It does not search a developer checkout, NuGet cache, Baker package or floating path.
Startup inventories its output and validates exactly one DLL, exact SHA, f32 and public StableMath
before artifact loading or HTTP query handling.

## Forbidden layouts

- Jitter source/DLL inside `Packages/com.datasakura.custom-navigation`;
- automatic Jitter entry in Custom Navigation `package.json`;
- two project-owned `Jitter2.Core.dll` files;
- Unity and server using different extracted releases;
- `USE_DOUBLE_PRECISION`/f64;
- a rebuilt local DLL with the same assembly name but another hash;
- resolving Jitter from Jitter Physics Baker or another package at compile time.
