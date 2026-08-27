# Navigation artifact filenames and migration

Current client builds stay under
`Assets/DataSakura/CustomNavigation/Generated/Navigation` and use one stable set per level:

- `<levelId>.navigation.bytes`
- `<levelId>.navigation.manifest.json`
- `<levelId>.navigation.asset`

For example: `npi_multiplayer_test.navigation.bytes`.

The filename is descriptive only. Content identity remains the complete lowercase SHA-256 in
the manifest and `NavigationArtifactAsset`; client and server loaders recompute it before using
the payload. Schema `1`, runtime level identity, DotRecast format, and canonical payload bytes are
unchanged. Level description, UI label, file timestamp, profile changes, and display name are not
written into canonical bytes.

## Explicit migration

Open **Custom Navigation > Diagnostics > Artifact Filename Migration**. The operation:

1. scans existing generated `NavigationArtifactAsset` files;
2. verifies each payload against its full SHA-256 and rejects duplicate destinations;
3. moves payload, manifest, and asset with `AssetDatabase.MoveAsset`;
4. updates the manifest `fileName` and the asset references together;
5. preserves all three Unity GUIDs and the exact payload bytes.

The migration is idempotent. A partially completed GUID-preserving move can be run again. It
does not touch files outside the generated navigation root and does not silently merge two levels
that resolve to the same stable filename.

Legacy `.navmesh.bytes` artifacts remain loadable and exportable before migration. A new build
will ask for the explicit migration instead of creating a competing second asset for the same
level.

## Export safety

Folder export and HTTP upload validate schema, DotRecast version, full SHA-256, polygon count,
and a plain supported `fileName`. Payload, level manifest, and optional `active.manifest.json` are
first written to temporary files. If committing any file fails, previous files are restored and
temporary files are removed, so an active incomplete pair is not left behind.

The current stable filename represents the current export of one level in a folder. To retain
several exports, use clearly named build folders and the same stable filenames inside each one.
No address catalog or new generated-root hierarchy is introduced.
