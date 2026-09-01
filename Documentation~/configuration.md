# Configuration

Справочник фактических настроек DataSakura Custom Navigation 0.7.0. Значения ниже
относятся к новым assets/components. Старые сериализованные assets сохраняют свои
значения после обновления пакета.

## Уровень и shared profiles

Для успешного `Validate` и `Build for Client` компонент `NavigationLevel` должен иметь:

- непустой `Level Id`;
- `Default Agent Profile`;
- `Area Catalog`;
- хотя бы один валидный geometry source.

`Geometry Root = None` допустим: пакет использует transform самого `NavigationLevel`.
Отсутствие `Performance Profile` даёт информационное сообщение, но не блокирует
`Build for Client` в главном окне; при этом кнопка `Bake` в custom Inspector остаётся
disabled, потому что его `IsReadyToBake` требует профиль. Для runtime scheduler профиль
всё равно обязателен. `Description` не является обязательным. Project defaults настраиваются в
`Project Settings > DataSakura > Custom Navigation`; кнопка `Create Defaults`
создаёт `DefaultAgent.asset`, `DefaultAreas.asset` и
`DefaultRuntimeQueryBudget.asset`, не перезаписывая существующие assets.

## Agent Profile

Новый `NavigationAgentProfile` имеет:

| Поле Inspector | Default | Назначение |
| --- | ---: | --- |
| `Profile Id` | `human_standing` | Stable id профиля в manifest и runtime checks |
| `Height` | `1.8 m` | Минимальный вертикальный clearance |
| `Radius` | `0.45 m` | Радиус erosion и projection extents |
| `Maximum Climb` | `0.35 m` | Максимальная высота ступени |
| `Maximum Slope` | `45°` | Максимальный walkable slope |
| `Allowed Movement` | `Walk (regular walking)`, `Crouch (squeeze through crouched)`, `Swim`, `Jump (leap across a gap)`, `Door (passage through a door)`, `Ladder (ladder or rope)` | Разрешённые traversal flags |
| `Forbidden Movement` | `Disabled (temporarily closed)` | Исключённые flags |
| `Area Costs` | пусто | Overrides стоимости area |

Agent diagram выводит из этих defaults: минимальный диаметр прохода `0.9 m`,
рекомендуемый doorway с clearance `1.1 m`, step `0.35 m` и вертикальный clearance
`1.8 m`.

Изменение размеров агента или build settings требует нового bake. Runtime должен
использовать тот же agent profile, для которого построен артефакт.

## Area Catalog

Важны два разных сценария создания:

- plain `Assets > Create > DataSakura > Custom Navigation > Area Catalog` создаёт
  пустой список `areas`;
- новый catalog, созданный через `Create Navigation Level Setup` или `Create Defaults`,
  заполняется defaults; уже существующая ссылка не сбрасывается;
- явный `ResetToDefaults()` также заполняет defaults.

Reset/default catalog:

| Area | Cost | Цвет preview |
| --- | ---: | --- |
| `Ground` | `1.0` | зелёный |
| `Stairs` | `1.1` | синий |
| `Danger` | `4.0` | красный |
| `Crouch` | `1.5` | фиолетовый |

Документация и gameplay не должны предполагать, что plain-created catalog уже
содержит `Ground`. Проверьте список до назначения `Area` источникам и links.

## Build settings

Новый `NavigationBuildSettings` сериализуется с
`Quality = Balanced (recommended)` и
`Auto Cell Size = true`.

| Raw параметр | Serialized default |
| --- | ---: |
| `Cell size (m)` | `0.25` |
| `Cell height (m)` | `0.15` |
| `Tile size (cells)` | `128` |
| `Vertices per polygon` | `6` |
| `Min region area` | `3` |
| `Merge region area` | `8` |
| `Max edge length (cells)` | `12` |
| `Edge simplification` | `1.3` |
| `Detail mesh step` | `6` |
| `Detail mesh error (m)` | `1.0` |

Inspector показывает raw Recast values под
`Advanced (raw Recast parameters)`. Для preset quality они read-only и пересчитываются
из профиля агента. С default agent (`Radius = 0.45`, `Maximum Climb = 0.35`)
`Balanced` даёт эффективные `Cell size = 0.15 m` и `Cell height = 0.175 m`.

Preset formulas:

| Quality | Cell size при Auto | Edge simplification | Max edge | Min / Merge region | Detail step / error |
| --- | --- | ---: | ---: | ---: | ---: |
| `Fast (quick bake, coarse navmesh)` | Radius / 2 | `2.0` | `16` | `6 / 16` | `0 / 0` |
| `Balanced (recommended)` | Radius / 3 | `1.3` | `12` | `3 / 8` | `6 / 1.0` |
| `High Detail (precise, slow bake)` | Radius / 4 | `1.0` | `8` | `1 / 4` | `6 / 0.5` |

При Auto `Cell height` равен `max(0.01, Maximum Climb / 2)`. Если
`Maximum Climb <= 0`, fallback равен `max(0.01, Cell size × 0.6)`. В
`Custom (manual tuning)` preset calculation не запускается: даже если сериализованный
`Auto Cell Size` остался включён, raw values не выводятся автоматически из agent
profile. Для `Custom` задайте и проверьте их вручную.

## Performance Profile

Inspector предлагает `Mobile Low`, `Mobile Medium`, `Mobile High`, `Custom` и
действие `Reset preset`. Секция активных лимитов называется `Working limits`,
детали — `Advanced · working details`, зарезервированные compatibility fields —
`Legacy / Diagnostics · reserved`.

### Scheduler presets

| Inspector label | Mobile Low | Mobile Medium | Mobile High |
| --- | ---: | ---: | ---: |
| `Frame budget (ms)` | `0.5` | `1.0` | `1.5` |
| `Search steps per frame` | `96` | `256` | `512` |
| `Steps per single pass` | `16` | `32` | `64` |
| `New requests per frame` | `1` | `2` | `4` |
| `Concurrent requests` | `2` | `4` | `8` |
| `Backlog limit` | `32` | `64` | `128` |
| reserved route cache entries | `64` | `128` | `256` |
| reserved memory budget | `16 MB` | `24 MB` | `40 MB` |
| reserved background workers | `0` | `0` | `1` |

Default нового профиля — `Mobile Medium`. Остальные defaults:

| Inspector label / значение | Default |
| --- | ---: |
| `Corridor polygons` | `256` |
| `Route points` | `128` |
| `Combat Bot Minimum Replan Seconds` | `0.25 s` |
| `Visible Bot Minimum Replan Seconds` | `0.5 s` |
| `Background Bot Minimum Replan Seconds` | `1.5 s` |
| `Queue lifetime (s)` | `0.5` |
| `Warning threshold` | `1.25×` |
| `Collect Production Metrics` | `true` |

`Frame budget`, search/admission limits, backlog, corridor, route points, queue
lifetime и warning threshold реально читаются runtime scheduler/adapter. Replan values
читаются sample bots, но scheduler сам их не навязывает.

Route cache, memory budget, background workers и collect-production-metrics остаются
сериализованными compatibility/reserved fields. Package runtime не создаёт background
workers и не применяет эти значения как production limits. Изменение Performance
Profile не требует повторного geometry bake.

## Geometry Source

Defaults нового `NavigationGeometrySource`:

| Поле | Default |
| --- | --- |
| `Mode` | `Include` |
| `Area` | `Ground (regular floor)` |
| `Include Children` | `true` |
| `Include Inactive Children` | `false` |

`Mode = Block` вычитает world-space bounds каждого найденного `MeshFilter`, а не его
точные triangles; `Ignore` явно исключает source. Значение
`Ground (regular floor)` должно существовать в назначенном Area Catalog.

## Modifier Volume

| Поле | Default |
| --- | --- |
| `Mode` | `Block` |
| `Area` | `Not Walkable (blocked)` |
| `Center` | `(0, 0, 0)` |
| `Size` | `(1, 1, 1)` |

Volume участвует в следующем bake; это не runtime obstacle.

## Navigation Link

| Поле | Default |
| --- | --- |
| `Link Id` | генерируется в `Reset` |
| `Link Type` | `Jump` |
| `Local Start` / `Local End` | локально влево / вправо |
| `Bidirectional` | `false` |
| `Radius` | `0.45` |
| `Cost` | `1.0` |
| `Area` | `Ground (regular floor)` |

Link попадает в Detour как off-mesh connection, но `NavigationPathResult` возвращает
только точки: `Link Id`, type и flags в runtime result не передаются. Traversal,
animation и gameplay validation реализует потребитель.

## Navigation Portal

| Поле | Default |
| --- | --- |
| `Portal Id` | генерируется в `Reset` |
| `Portal Type` | `Door` |
| `Open By Default` | `true` |
| `Controlled Links` | пусто |

Открытие/закрытие двери и синхронизация состояния — ответственность gameplay code.

## Navigation Test Point

| Поле | Default |
| --- | --- |
| `Point Id` | генерируется в `Reset` |
| `Point Type` | `Generic` |
| `Group` | `default` |
| `Required` | `true` |

Test points используются authoring diagnostics; они не двигают runtime agents.

## Query Scheduler Behaviour

Defaults компонента `DataSakura/Custom Navigation/Query Scheduler`:

| Поле | Default |
| --- | --- |
| `Artifact` | `None` |
| `Performance Profile` | `None` |
| `Agent Profile` | `None` |
| `Log Budget Warnings` | `true` |

Назначьте все три ссылки до `Awake`. Один behaviour владеет owner-thread scheduler;
он не создаёт background thread и не использует Unity Physics.

## Server Settings

Новый `NavigationServerSettings`:

| Inspector label | Default |
| --- | --- |
| `Host` | `127.0.0.1` |
| `Port` | `5079` |
| `Use Https` | `false` |
| `Request Timeout Seconds` | `5` |
| `Server Artifact Folder` | `NavigationServer/NavigationData` |
| `Notes` | `Navigation artifacts are baked offline in Unity (Navigation -> Build for Client) and pushed to the server with the Upload to Server button.` |

`Upload token` в этот asset не входит: Editor хранит его отдельно в preferences.
Default `Notes` содержит устаревшее сокращение `Navigation -> Build for Client`;
фактический текущий путь — `DS Navigation` → `Bake` → `Build for Client`. Это только
редактируемая team note и на runtime URL/behavior не влияет.
Production deployment, TLS, secret storage и authentication policy остаются задачей
проекта-потребителя.

## Scene Preview defaults

В `Preferences > DataSakura > Custom Navigation > Scene Preview` по умолчанию
включены `Sources`, `Baked` и `Runtime`; `Scope = Active Level`,
`Visibility = Visible`.

Другие значения:

- `Scope = Selection` — уровень, содержащий selection; без подходящего selection
  preview пуст;
- `Scope = All Loaded Levels` — все загруженные уровни;
- `Visibility = X Ray` — preview не скрывается scene geometry.

Настройки preview персональные и не меняют baked artifact.

## Defaults импортируемого sample

Следующие значения относятся только к `Navigation Demos & Bots`, а не к package core:

| Bot Agent field | Default |
| --- | --- |
| `Compute Mode` | `Local Only` |
| `Server Url Override` | пусто |
| `Retry Delay Seconds` | `0.5 s` |
| `Start Waypoint Index` | `0` |
| `Move Speed` | `3` |
| `Arrival Radius` | `0.4` |
| `Ground Offset` | `0` |
| `Snap To Nav Mesh On Start` | `true` |
| `Wait At Waypoint Seconds` | `0` |
| `Rotation Speed` | `360` |
| `Query Priority` | `CombatBot` |
| `Show Path` | `true` |

`NavigationWaypointRoute`: Waypoints пуст, Patrol Mode=`Loop`, Gizmo Radius=`0.3`.

Sample требует отдельный `com.unity.inputsystem`, а его initializer может создавать
demo assets вне imported sample folder и менять Build Settings. Legacy demo scenes в
development project не являются доказательством clean sample import; проверяйте эти
defaults в отдельном consumer project после реального UPM import и успешной compile.
