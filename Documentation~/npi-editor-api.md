# Editor API для NPI и внешних инструментов

Custom Navigation остаётся standalone package. Внешние Editor tools ссылаются на
assembly `CustomNavigation.NavigationEditor`; пакет не получает reference на consumer,
EFT, physics package или gameplay server.

## Identity ownership

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

`Standalone` использует serialized `NavigationLevel.LevelId`. `External(owner,
levelId)` передаёт canonical ID только для текущего вызова и не изменяет standalone ID.
Пустой owner, неканонический ID и конфликт с другим загруженным `NavigationLevel`
возвращают `Failed` до записи файлов.

`NavigationEditorApi.Bake` выполняет только navigation bake. Он не запускает/export-ит
reference server, physics или внешний NPI pipeline.

## Read-only result

`NavigationEditorResult` предоставляет:

- `Status`, `Succeeded`, `Issues`;
- resolved `LevelId`, `Ownership`, diagnostic `Owner`;
- `Artifact`, `ArtifactPath`, `PayloadPath`, `ManifestPath`;
- полный payload `Digest`;
- payload size, polygon count и source mesh count.

`ReadSummary` проверяет loader, manifest и hash, но не пишет файлы. `Missing` означает
отсутствие asset. `Changed` означает валидный, но потенциально устаревший относительно
текущей scene/source state artifact. `Failed` означает ошибку identity/metadata/
manifest/payload.

## Shared preview

```csharp
NavigationPreviewState state = NavigationPreviewApi.Current;
NavigationPreviewApi.Apply(
    state.WithBaked(true).WithDepth(NavigationPreviewDepth.XRay));
```

`Current` side-effect free. `Apply` обновляет те же `EditorPrefs`, которые используют
Scene View Overlay и Preferences; отдельного набора toggles нет. На событие
`NavigationPreviewApi.Changed` нужно отписываться при unload/reload владельца.

## Compatibility

`NavigationBakeCommand.Validate/Execute` сохранён как public compatibility facade для
старых consumers. Для новых integrations предпочитайте `NavigationEditorApi`, потому
что он поддерживает explicit identity ownership и verified delivery summary.

API впервые опубликован в 0.6.14 и входит в 0.7.0 без изменения signatures. Release
0.7.0 не добавляет dependency на NPI; runtime/server coordinate и wire contracts при этом
намеренно мигрированы на canonical Jitter protocol v2.

Полный справочник: [API reference](api-reference.md). Практический пример:
[Recipes](recipes.md#6-проверить-и-запечь-карту-из-внешнего-editor-tool).
