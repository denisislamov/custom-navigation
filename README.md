# DataSakura Custom Navigation

DataSakura Custom Navigation `0.6.16` — physics-free навигация для Unity 6 на базе
DotRecast. Пакет запекает navmesh в Editor, сохраняет один детерминированный бинарный
артефакт для клиента и сервера и выполняет локальные запросы в пределах кадрового
бюджета. Unity Physics и встроенный Unity NavMesh не используются.

[Открыть полное руководство](Documentation~/index.md)

## Что входит в пакет

| Assembly | Где работает | Назначение |
| --- | --- | --- |
| `CustomNavigation.Authoring` | Editor и Player | `NavigationLevel`, профили, areas, links и другие authoring-данные. |
| `CustomNavigation.Runtime` | Player | Загрузка артефакта, budgeted scheduler и HTTP-клиент. |
| `CustomNavigation.NavigationEditor` | Только Editor | Validation, bake, Inspector, Scene View Overlay и окно `DS Navigation`. |

Управление персонажем, AI behavior, сетевая репликация и runtime-перестроение navmesh
не входят в пакет: игровой код получает точки пути и сам применяет их.

## Требования

- Unity `6000.3` или новее;
- для основного пакета дополнительные Unity packages не нужны: managed-сборки
  DotRecast уже включены;
- sample `Navigation Demos & Bots` требует `com.unity.inputsystem`;
- опциональный reference server требует .NET 9 SDK.

## Установка

Рекомендуемый воспроизводимый вариант — установить фиксированный Git tag через
`Window > Package Management > Package Manager` → `+` →
`Add package from git URL...`:

```text
https://github.com/denisislamov/custom-navigation.git#v0.6.16
```

После импорта откройте единственную основную точку входа:
`Tools > DataSakura > Custom Navigation Window`.

Git URL, `Packages/manifest.json`, local disk, embedded package, удаление и ограничения
форматов описаны в [руководстве по установке](Documentation~/installation.md).

## Quick Start

Чтобы получить первый локальный путь без HTTP-сервера:

1. Пройдите [Quick Start за 5–15 минут](Documentation~/quick-start.md).
2. Откройте `Tools > DataSakura > Custom Navigation Window`.
3. Используйте вкладки `Overview` → `Geometry` → `Bake`.
4. Нажмите `Validate`, затем `Build for Client`.

`Build for Client` создаёт в
`Assets/DataSakura/CustomNavigation/Generated/Navigation` тройку:

```text
<levelId>.navigation.bytes
<levelId>.navigation.manifest.json
<levelId>.navigation.asset
```

## Sample: Navigation Demos & Bots

Сначала установите `com.unity.inputsystem`, затем откройте пакет в Package Manager и
на вкладке `Samples` нажмите `Import` у `Navigation Demos & Bots`. Unity скопирует
sample в:

```text
Assets/Samples/DataSakura Custom Navigation/0.6.16/Navigation Demos & Bots
```

Sample содержит runtime-компоненты и Editor builders для local, server, hybrid и
multi-level сценариев. Сами scene assets в package не поставляются: builders создают
их в проекте и могут обновить Build Settings.

## Reference server

Исходники .NET 9 сервера находятся в `Server~`. В окне `DS Navigation` выберите
`NavigationLevel`, затем откройте `Settings` → `Local server` и нажмите
`Install navigation server`. Сервер будет скопирован в `<project>/NavigationServer`.

После `Build for Client` на вкладке `Bake`:

- `Upload to Server` отправляет артефакт работающему серверу по HTTP;
- `Export to Folder` записывает его в настроенную локальную папку.

Подробности находятся в [Server README](Server~/README.md).

## Документация

- [Полное оглавление](Documentation~/index.md)
- [Установка и удаление](Documentation~/installation.md)
- [Quick Start](Documentation~/quick-start.md)
- [Обновление, миграция и откат](Documentation~/migration-and-upgrading.md)
- [Editor API для внешних интеграций](Documentation~/npi-editor-api.md)
- [Поля производительности](Documentation~/navigation-performance.md)
- [Имена и миграция артефактов](Documentation~/artifact-filenames.md)

## Обновление

Не меняйте versioned sample folders и generated artifacts вручную. Перед сменой tag
сделайте backup, затем выполните применимые явные миграции на вкладке `Diagnostics`.
Полная процедура: [Migration and upgrading](Documentation~/migration-and-upgrading.md).

## Лицензии

Код пакета распространяется по [MIT](LICENSE.md). DotRecast поставляется по лицензии
zlib; см. [Third Party Notices](Third%20Party%20Notices.md).
