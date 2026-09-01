# Editor Guide

Этот документ описывает фактический Editor UX пакета DataSakura Custom Navigation
0.7.0. Названия меню, вкладок, кнопок и полей приведены так, как они отображаются в
Unity 6. Пакет не использует встроенный Unity NavMesh и не требует Unity Physics для
authoring или bake.

## Главное окно

Откройте `Tools > DataSakura > Custom Navigation Window`. Это единственный пункт
пакета в меню `Tools`. Окно имеет title `DS Navigation` и пять вкладок:

- `Overview`;
- `Geometry`;
- `Bake`;
- `Settings`;
- `Diagnostics`.

В header отображаются `Custom Navigation Authoring` и пояснение
`Physics-free geometry authoring for local and server DotRecast navigation.` Поле
`Navigation Level` выбирает редактируемый уровень. Если уровень отсутствует, кнопка
`Create Navigation Level Setup` создаёт:

```text
Navigation Level
├── NavigationGeometry
├── NavigationModifiers
├── NavigationLinks
└── NavigationTestPoints
```

Setup назначает существующие project defaults, только если присутствуют все три shared
profile. Если комплект неполный, setup не смешивает его с локальными данными: он создаёт
новый локальный комплект из трёх assets в
`Assets/DataSakura/CustomNavigation/Generated/Settings`, не перезаписывая частично
назначенные project defaults. `Level ID` выводится из имени активной сцены, поэтому
сцену следует сохранить до запуска setup.

Строка состояния использует следующие состояния:

- `[ ] No level selected`;
- `[ ] Not validated - press Validate`;
- `[X] ... errors - export blocked`;
- `[!] ... warnings`;
- `[v] Ready to export`.

Кнопка `Validate` повторно проверяет текущий уровень. Ошибка блокирует новый
`Build for Client`; warning сам по себе его не блокирует. Статус при ошибке содержит
текст `export blocked`, но кнопки доставки уже существующего artifact —
`Export to Folder` и `Upload to Server` — остаются доступны и не повторяют authoring
validation. Перед доставкой убедитесь, что artifact соответствует текущей сцене.

## Overview

В summary отображаются `Level ID`, `Description`, `Geometry sources`,
`Links / portals`, `Test points` и `Device tier`. Ниже validation группирует
замечания по областям `Level setup`, `Geometry`, `Stable ids` и `Runtime budgets`.

Секция `Level setup` редактирует `Level Id`, `Description` и `Geometry Root`.
Секция shared profiles содержит:

- `Agent`;
- `Areas`;
- `Runtime Query Budget`;
- действия `Edit`, `New` и `Make Local Copy`.

`Make Local Copy` полезен, когда конкретной сцене нужен отдельный профиль, но shared
project default менять нельзя.

## Geometry

Вкладка начинается с секции `Explicit geometry sources` и сводки
`Readable MeshFilters`, `Missing source tag`, `Geometry root`.

Основные действия и фильтры:

- `Add N Missing Sources` добавляет `NavigationGeometrySource` найденным
  `MeshFilter` под `Geometry Root`;
- `Add Source To Selected GameObject` работает с текущим выбранным GameObject;
- `Search` фильтрует список;
- `Mode` имеет значения `All`, `Include`, `Block`, `Ignore`;
- foldout списка называется `Sources (N of M)`;
- bulk section называется `Apply to all shown (N)` и содержит `Set mode`,
  `Set area`, `Select all shown in the scene`.

В каждой строке доступны `Mode`, `Area`, `Include Children`,
`Include Inactive Children`, а также `Select` и `Remove`.

Пакет читает `MeshFilter.sharedMesh`. Один `MeshRenderer`, Collider или объект вне
ветки `Geometry Root` не становится источником автоматически. Mesh должен
существовать и быть читаемым.

## Bake

Секция `Build pipeline` содержит:

- `Build for Client`;
- `Upload to Server` или `Uploading...`;
- `Export to Folder`;
- `Remove baked navigation`.

`Build for Client` сначала запускает validation, затем строит navmesh. При ошибке
появляется modal `Build stopped` с кнопкой `Got it`. `Export to Folder` и
`Upload to Server` доставляют уже построенный payload и не выполняют скрытый bake.

Build summary показывает `Level`, `Status`, `Size`, `Contents`, а при наличии —
`Label` и `File UTC`. Доступны `Show in Project`, `Reveal in Finder`,
`Copy Diagnostics`. Foldout `Details` показывает SHA-256, Schema, DotRecast,
Agent profile, Payload и Manifest.

Для `<levelId>` создаются только project-owned файлы:

```text
Assets/DataSakura/CustomNavigation/Generated/Navigation/
├── <levelId>.navigation.bytes
├── <levelId>.navigation.manifest.json
└── <levelId>.navigation.asset
```

`Remove baked navigation` показывает точный список удаляемых generated-файлов.
Подтверждение называется `Delete files`, отмена — `Cancel`. Копии на сервере эта
операция не удаляет.

## Settings

Верхние действия:

- `Open Project Defaults`;
- `Open Scene Preview Preferences`.

В секции bake доступны `Agent the navmesh is built for`,
`Edit the agent profile`, `Bake quality`, `Use Project Bake Default`,
`Mobile query budget`, `Create Mobile Performance Profile` и
`Edit Runtime Query Budget`.

Секция `Navigation server` содержит `Create Navigation Server Settings`, поля
`Host`, `Port`, `Use Https`, `Request Timeout Seconds`, `Server Artifact Folder`,
`Notes` и действия `Show in Project`, `Open the server folder`.

`Connection check` показывает `Address` и предлагает `Apply` и `Check /health`.
В `Artifact upload` поле `Upload token` хранится в Editor preferences, а не в
asset или scene. Для reference server доступны:

- `Use the installed server folder`;
- `Choose folder...`;
- `Install navigation server`;
- `Start server`;
- `Stop server`;
- `Open server folder`;
- `Reinstall from package`.

## Diagnostics

Agent preview:

- `Show the agent reference`;
- `Place at the view center`;
- `Snap to the navmesh`.

Path Probe:

- поля `Start`, `Destination`;
- режимы `Local`, `Server`, `Both`;
- `Swap`, `From Test Points`, `Clear`, `Find Path`.

Analysis:

- `Show the last analysis`;
- `Narrowness threshold (m)`;
- `Analyze Clearance`, `Analyze Slopes`, `Clear analysis`.

Navigation maps:

- `Refresh from server`, `Local folder only`, `Sync all`;
- row actions `Upload to Server`, `Export to Folder`, `Select client asset`.

Миграции запускаются здесь, а не из отдельного `Tools` submenu:

- `Preview / Run pre-0.6.6 Migration`;
- `Preview / Run Artifact Filename Migration`.

Перед `Run Migration` прочитайте confirm dialog и сохраните/закоммитьте
project-owned assets. Отдельного dry-run отчёта текущая реализация не показывает,
несмотря на `Preview / Run` в названии кнопки.

## Navigation Level Inspector

Custom Inspector разделён на `Level`, `Geometry Root`, `Settings`, `Bake Status` и
foldout `Advanced`.

Основные поля: `Level Id`, `Geometry Root`, `Default Agent Profile`,
`Area Catalog`, `Performance Profile`. В `Advanced` находятся `Description` и raw
build settings. Если обязательные ссылки отсутствуют, Inspector показывает
`Create the missing settings`.

Bake status — `Not baked` либо `Ready · ... polygons · ... sources · ... KB`.
Действия: `Validate`, `Bake`, `Open`; в `Advanced` также доступен
`Export for Server`.

## Остальные Inspector

`Navigation Agent Profile` показывает `Agent dimensions`: `Height`, `Radius`,
`Maximum Climb`, `Maximum Slope`. Foldout `How it works (agent diagram)` визуализирует
проход, ступень и вертикальный clearance. `Advanced` содержит `Profile Id`,
`Allowed Movement`, `Forbidden Movement`, `Area Costs`.

`Navigation Performance Profile` предлагает presets `Mobile Low`,
`Mobile Medium`, `Mobile High`, `Custom`; его точные значения описаны в
[Configuration](configuration.md#performance-profile).

Обычные authoring components используют Inspector для сериализованных полей. Их
defaults также сведены в [Configuration](configuration.md).

## Project Settings и Preferences

`Project Settings > DataSakura > Custom Navigation` содержит `Agent`, `Areas`,
`Runtime Query Budget`, `Bake Quality default` и `Create Defaults`. По умолчанию
assets создаются в `Assets/DataSakura/CustomNavigation/Settings` как
`DefaultAgent.asset`, `DefaultAreas.asset` и `DefaultRuntimeQueryBudget.asset`.

`Preferences > DataSakura > Custom Navigation > Scene Preview` содержит:

- `Sources (sand dotted bounds)`;
- `Baked (dusty violet surface)`;
- `Runtime (light lilac routes)`;
- `Scope`;
- `Visibility`.

Открытие этих страниц само по себе не создаёт assets.

## Scene View Overlay

Нативный overlay называется `Custom Navigation` и по умолчанию отображается в
Scene View.

В `Layers` находятся toggles `Sources`, `Baked`, `Runtime`, enums `Scope` и
`Visibility`, status, `Settings` и `Frame Level`.

- `Scope`: `Active Level`, `Selection`, `All Loaded Levels`;
- `Visibility`: `Visible`, `X Ray`;
- defaults: все три layers включены, `Active Level`, `Visible`.

`Sources` рисуются sand-colored dotted bounds, `Baked` — dusty violet surface,
`Runtime` — light lilac routes. В `X Ray` геометрия не скрывает preview. Это только
Editor-визуализация; она не запускает path query автоматически.

## Asset и Component menus

Create Asset paths:

- `Assets > Create > DataSakura > Custom Navigation > Agent Profile`;
- `... > Area Catalog`;
- `... > Performance Profile`;
- `... > Server Settings`.

Add Component paths:

- `DataSakura/Custom Navigation/Navigation Level`;
- `Geometry Source`;
- `Modifier Volume`;
- `Navigation Link`;
- `Navigation Portal`;
- `Navigation Test Point`;
- `Query Scheduler`.

`NavigationArtifactAsset` создаётся build pipeline и намеренно не имеет
`CreateAssetMenu`.

## Sample: границы и побочные эффекты

Package Manager объявляет sample `Navigation Demos & Bots`, но в 0.7.0 исходный
`Samples~/Demos` содержит код и `.asmdef`, а не готовые `.unity` scenes, prefabs,
materials или собственный README.

Перед import учитывайте:

1. Sample assembly жёстко ссылается на `Unity.InputSystem`; установите
   `com.unity.inputsystem` отдельно. Это sample dependency, а не dependency package core.
2. Editor initializer может создать или обновить demo scenes вне imported sample folder
   и изменить Editor Build Settings. Import не является read-only операцией.
3. Автоматически вызываются builders для LocalBots, TopDown и ServerClient. Builders
   Hybrid, MultiLevel и Hub существуют в коде, но не имеют пользовательского menu item
   и не являются автоматически подтверждённым import flow.
4. Не импортируйте sample рядом с legacy-копией, которая уже объявляет assemblies
   `CustomNavigation.Client` и `CustomNavigation.Client.Editor`: одинаковые assembly
   names приведут к compile conflict.

Demo scenes, уже лежащие в development checkout под `Assets/CustomNavigation`, — это
legacy project assets. Они полезны для разработки, но не доказывают, что чистый
consumer project импортирует sample, компилируется и получает те же scenes. Такое
доказательство требует отдельного clean UPM install/import и ручной проверки Console,
Hierarchy, Build Settings и Play Mode.
