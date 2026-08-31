# Имена navigation artifacts и миграция

Новые client builds хранятся в
`Assets/DataSakura/CustomNavigation/Generated/Navigation` и используют одну стабильную
тройку на `levelId`:

- `<levelId>.navigation.bytes`;
- `<levelId>.navigation.manifest.json`;
- `<levelId>.navigation.asset`.

Например: `arena_01.navigation.bytes`.

Имя удобно человеку, но не определяет содержимое. Идентичность остаётся полным
lowercase SHA-256 в manifest и `NavigationArtifactAsset`; loader пересчитывает hash до
использования payload. Schema `1`, DotRecast format `2026.1.3` и canonical bytes не
зависят от UI label, timestamp или имени файла.

## Явная миграция legacy filenames

Откройте `Tools > DataSakura > Custom Navigation Window > Diagnostics` и нажмите
`Preview / Run Artifact Filename Migration`.

Операция:

1. сканирует generated `NavigationArtifactAsset`;
2. проверяет каждый payload по полному SHA-256 и заранее отклоняет destination
   conflicts;
3. переносит payload, manifest и asset через `AssetDatabase.MoveAsset`;
4. одновременно обновляет `fileName` в manifest и serialized asset references;
5. сохраняет GUID всех трёх assets и точные payload bytes.

Migration идемпотентна: повторный запуск после полного или частично завершённого
GUID-safe move должен стать no-op или закончить недостающий шаг. Она не merge-ит два
уровня с одинаковым target path и не трогает файлы вне generated navigation root.

Legacy `.navmesh.bytes` остаются читаемыми и экспортируемыми. Новый build просит
выполнить explicit migration вместо создания второй конкурирующей тройки для того же
уровня.

## Безопасность export/upload

Folder export и HTTP upload проверяют schema, DotRecast version, SHA-256, polygon count
и простое поддерживаемое `fileName`. Payload, level manifest и optional
`active.manifest.json` сначала записываются во временные файлы; при ошибке предыдущие
файлы восстанавливаются.

Stable name представляет текущий export уровня в одной папке. Если нужно сохранить
несколько сборок одного уровня, используйте отдельные versioned folders, сохраняя те же
имена внутри.

См. [Migration and upgrading](migration-and-upgrading.md) и
[Troubleshooting](troubleshooting.md).
