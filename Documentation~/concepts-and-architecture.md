# Concepts and architecture

## Модель системы

Custom Navigation разделяет дорогую подготовку данных и дешёвое использование:

```text
AUTHORING (Unity scene)             DELIVERY                  RUNTIME

NavigationLevel                    client asset              gameplay caller
  + GeometrySource      Bake       + bytes         Load      + scheduler
  + ModifierVolume   ----------->  + manifest   -----------> + completion callback
  + Link / Portal                  + .asset                  + movement owned by game
  + profiles                           |
                                        +---- Export/Upload ---> .NET server
                                                                 + POST /path
```

Editor собирает world-space triangles, строит Recast/Detour navmesh и сериализует
готовый `DtNavMesh`. Player и сервер не повторяют bake. Они проверяют и загружают одни
и те же bytes.

## Термины

| Термин | Значение в пакете |
| --- | --- |
| Authoring | Компоненты сцены и ScriptableObject-профили, которые задаёт пользователь |
| Bake | Editor-only преобразование mesh triangles в Detour navmesh |
| Artifact | Binary payload + JSON manifest + Unity asset wrapper |
| `levelId` | Канонический ID карты; влияет на имена файлов и выбор карты сервером |
| `artifactHash` | Полный lowercase SHA-256 конкретного binary payload |
| Polygon corridor | Последовательность Detour polygons до построения итоговых точек |
| Straight path | Массив `Vector3`, возвращаемый gameplay-коду |
| Sliced query | Поиск, разбитый на ограниченные порции работы между кадрами |
| Projection | Поиск ближайшей допустимой точки navmesh, не Physics raycast |
| Area | Тип поверхности 0–15 с цветом/стоимостью/flags в authoring |
| Flags | Возможности агента: Walk, Crouch, Swim, Jump, Door, Ladder, Disabled |
| Path fingerprint | SHA-256 точек пути после округления координат до 1 мм |

## Assembly boundaries

| Assembly | Папка | Содержимое | Зависимости |
| --- | --- | --- | --- |
| `CustomNavigation.Authoring` | `Authoring/` | Scene components, profiles, serialized data | UnityEngine |
| `CustomNavigation.Runtime` | `Runtime/` | Loader, scheduler, HTTP client | Authoring, DotRecast Core/Detour |
| `CustomNavigation.NavigationEditor` | `Editor/` | Validator, Recast bake, window, inspectors, migrations | Authoring, Runtime, UnityEditor |
| `CustomNavigation.NavigationEditor.Tests` | `Tests/Editor/` | EditMode contracts | три package assemblies + TestAssemblies |
| `CustomNavigation.Client` | `Samples~/Demos/` после Import | Demo gameplay | Authoring, Runtime, Input System |
| `CustomNavigation.Client.Editor` | `Samples~/Demos/Editor/` после Import | Demo builders/inspectors | package + sample assemblies |
| `DotRecastServer` | `Server~/` после Install | отдельный .NET 9 process | DotRecast Core/Detour |

`Samples~`, `Server~` и `Documentation~` — скрытые UPM folders. Sample компилируется
только после Import. Server source сначала копируется рядом с `Assets` в
`NavigationServer`; Unity его не компилирует.

## Authoring ownership

### `NavigationLevel`

Пользователь создаёт один `NavigationLevel` для самостоятельной карты. Он владеет:

- serialized `levelId` и description;
- ссылкой на `Geometry Root`;
- локальным `NavigationBuildSettings`;
- ссылками на agent, area и performance profiles.

`GeometryRoot` при пустой ссылке возвращает Transform самого уровня. Editor one-click
setup назначает отдельную группу `NavigationGeometry`.

`ConfigureDefaults` только назначает profiles и синхронизирует agent-driven build
settings; bake не запускается. `OnValidate` нормализует ID и применяет preset.

### Scene components

- `NavigationGeometrySource` пользователь добавляет на объекты с `MeshFilter`;
- `NavigationModifierVolume` задаёт box, который блокирует или меняет area;
- `NavigationLink` задаёт off-mesh connection;
- `NavigationPortal` группирует links как authoring/gameplay metadata;
- `NavigationTestPoint` задаёт диагностические точки и stable IDs.

`Portal` не открывает дверь и не меняет Detour state сам. `LinkType` не проигрывает
анимацию. Такие gameplay transitions остаются ответственностью consumer-кода.

### ScriptableObjects

- `NavigationAgentProfile` может разделяться несколькими уровнями;
- `NavigationAreaCatalog` хранит presentation/authoring catalog;
- `NavigationPerformanceProfile` задаёт local scheduler limits;
- `NavigationServerSettings` загружается из `Resources`;
- `NavigationArtifactAsset` генерируется bake и не создаётся через Create menu.

Изменение shared profile влияет на всех потребителей ссылки. Используйте `Make Local
Copy` в Settings, если изменение относится только к одному уровню.

## Bake data flow

1. `NavigationAuthoringValidator` проверяет setup, geometry, IDs и budgets.
2. Builder собирает явные sources под `GeometryRoot`.
3. `Include` добавляет world-space mesh triangles; `Block` и modifier volumes создают
   непроходимые области; `Ignore` пропускается.
4. Agent height/radius/climb/slope и `NavigationBuildSettings` настраивают Recast.
5. Recast строит regions/contours/poly mesh; Detour получает один tile.
6. Links добавляются как off-mesh connections.
7. `DtMeshSetWriter` создаёт canonical bytes.
8. Builder вычисляет SHA-256, выполняет round-trip load и реальный query-check.
9. Unity записывает payload, manifest и `NavigationArtifactAsset` в generated root.

`tileSizeInCells` сериализован, но текущий builder создаёт один tile. Runtime geometry
changes и partial tile rebuild не реализованы.

### Identity и freshness

Имена файлов стабильны по `levelId`, но идентичность содержимого — полный SHA-256.
`NavigationEditorApi.ReadSummary` различает:

- `Missing` — asset отсутствует;
- `Ready` — asset валиден и не выглядит устаревшим;
- `Changed` — сцена dirty или число текущих source meshes отличается;
- `Failed` — identity/manifest/payload не прошли проверку.

Freshness сейчас не является полным content fingerprint всех вершин. Перед релизом
уровня выполняйте explicit `Build for Client`, а не полагайтесь только на `Ready`.

## Local runtime lifecycle

`NavigationQuerySchedulerBehaviour` имеет `DefaultExecutionOrder(-500)`:

1. `Awake` синхронно загружает artifact и создаёт scheduler.
2. При ошибке он пишет `[CustomNavigation] Local navigation initialization failed`,
   логирует exception и выключает component.
3. `Update` вызывает `Tick` и при необходимости rate-limited budget warning.
4. `OnDestroy` выполняет `CancelAll`.

`OnDisable` не отменяет запросы: disabled component перестаёт tick-ать, а callbacks
могут остаться незавершёнными до повторного enable или destroy.

`Configure` безопасно использовать только до `Awake` — например, на заранее
неактивном объекте. Он не пересоздаёт scheduler после инициализации.

Direct `NavigationQueryScheduler` привязывается к thread, на котором создан. Все
`RequestPath`, `Cancel`, `TryProjectPosition`, `Tick` и `CancelAll` должны выполняться
на том же thread. Background workers в текущей версии отсутствуют.

## Состояния запроса

```text
RequestPath
    |
    +-- backlog full --> reject callback
    |                    или eviction callback для менее приоритетного queued request
    v
  Queued --Cancel/expiry--> callback
    |
    | admission limits
    v
  Active sliced query --Cancel/failure/success/partial--> callback
```

Меньшее числовое значение `NavigationQueryPriority` означает более высокий приоритет.
Deadline действует только в backlog и не прерывает active search. `Cancel` отмечает
request; результат приходит при следующем `Tick`. Однако reject/eviction callback
может быть вызван синхронно внутри `RequestPath`, поэтому callback не должен зависеть
от того, что возвращаемый handle уже сохранён.

## Локальный и серверный маршрут

`NavigationComputeMode` описывает три consumer-сценария:

- `LocalOnly` — только scheduler;
- `ServerOnly` — только `POST /path`;
- `ServerPredicted` — немедленный local result и последующая authoritative correction.

Reference server использует тот же artifact, но его query defaults не полностью
совпадают с client scheduler: fixed nearest-poly extents/filter/buffer limits могут дать
другой path fingerprint даже при одинаковом artifact hash. Пакет обнаруживает mismatch,
но не гарантирует byte-identical client/server routes для всех agent filters.

Reference server — navigation service, а не gameplay authority. Сервер матча должен
отдельно проверять движение, collisions и правила игры.

## Cleanup и смена сцен

- Храните scheduler в сцене или persistent bootstrap, но не оставляйте gameplay
  callbacks без владельца.
- Сохраняйте `NavigationPathHandle` и отменяйте его в `OnDisable`/`OnDestroy` caller-а.
- Перед уничтожением direct scheduler вызовите `CancelAll`.
- При смене artifact, agent или performance profile создавайте новый scheduler.
- Не мутируйте profile после constructor: workspace pool и result buffers не
  перестраиваются.
- Coroutine HTTP-запрос заканчивается только пока coroutine выполняется; остановленная
  coroutine не гарантирует callback.

## Public и internal границы

Рекомендуемые внешние входы:

- Runtime: `NavigationQuerySchedulerBehaviour`, `NavigationQueryScheduler`,
  `NavigationServerPathClient`, `NavigationArtifactLoader`;
- Editor integration: `CustomNavigation.Editor.Api.NavigationEditorApi` и
  `NavigationPreviewApi`;
- compatibility: `NavigationBakeCommand` для старых consumers.

Не опирайтесь на internal builder, validator, artifact index, window implementation,
server process manager или sample builders. `NavigationArtifactInstance.NavMesh` и
`CreateQuery()` публичны, но прямой DotRecast access связывает consumer с версией
bundled DLL и считается advanced escape hatch.

Далее: [Configuration](configuration.md), [Runtime API](runtime-api.md) и
[Extending](extending.md).
