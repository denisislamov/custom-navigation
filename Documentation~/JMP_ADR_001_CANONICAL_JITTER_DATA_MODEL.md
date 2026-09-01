# JMP-ADR-001 — canonical Jitter data model для Custom Navigation

- Статус: **ACCEPTED для планирования P02–P11**.
- Дата: 2026-09-01.
- Baseline: Custom Navigation `0.6.16`, source commit `106c5df`.
- Prerequisite: implementation начинается только после опубликованного и независимо проверенного
  `P00-E01 PASS`.

## Контекст

Authoritative runtime/server/network contract сейчас разделён между Unity `Vector3`, server
`Vector3Dto`, несколькими `ServerVector3`, DotRecast `RcVec3f`, `float`, `double` и двумя копиями
`NavigationPathFingerprint`. Это допускает drift между Unity и .NET и не связывает artifact/path
semantics с одной Jitter runtime identity.

Цель migration — использовать public `Jitter2.LinearMath` API (`JVector`, source-level `Real`,
`StableMath`) для authoritative data и оставить Unity types только на authoring/presentation edge.
DotRecast остаётся pathfinding backend с единственной изолированной conversion boundary.

## Решение 1. Breaking API и package version

`NavigationPathResult.Points`, scheduler `RequestPath`/`TryProjectPosition` и authoritative
server-facing navigation values переходят с `UnityEngine.Vector3` на `JVector`. Это source- и
binary-breaking public API change.

Поскольку package ещё находится до `1.0.0`, release migration получает **minor bump `0.7.0`**, а
не patch `0.6.17`. Версия `1.0.0` этим ADR не объявляется: она требует отдельного product readiness
решения. Старые Vector3 overloads могут существовать только как явно deprecated boundary wrappers;
они не могут оставаться authoritative storage/implementation.

## Решение 2. Separate-install Jitter invariant

Jitter устанавливается отдельно до Custom Navigation integration:

- Custom Navigation не содержит Jitter source или DLL;
- Jitter Physics Baker не является транзитивным runtime provider;
- automatic UPM dependency на Jitter не добавляется;
- каждая asmdef/csproj, в коде которой встречается Jitter type, получает прямую reference на одну
  approved `Jitter2.Core`;
- P02 фиксирует exact tag, commit, DLL SHA, source/profile identity и f32 preflight только после
  remote P00 PASS.

## Решение 3. Target assembly graph

```text
separately installed Jitter2.Core (canonical, f32)
                     │
         CustomNavigation.Contracts
         ├── wire DTO/version/error codes
         ├── shared fingerprint contract
         └── no UnityEngine / no DotRecast
             │                 │
             ▼                 ▼
CustomNavigation.Runtime   DotRecastServer (net9)
├── direct Jitter ref      ├── direct Jitter ref
├── Authoring boundary     ├── Contracts/shared source
└── DotRecast Core/Detour  └── DotRecast Core/Detour
             │
     Editor / Samples / Tests
     direct Jitter ref only where their source names Jitter types
```

`CustomNavigation.Contracts` — логический owner. P02 может реализовать его как Unity assembly с
server-compatible linked/shared source либо как проверяемый multi-target project, но не через
копирование DTO/fingerprint implementations. Assembly names существующих assemblies сохраняются.

## Решение 4. JSON protocol strategy

Выбран **protocol version 2**, а не неявная попытка считать новый JVector contract старым
protocol.

- Endpoints `/path`, `/health`, `/artifacts` сохраняются.
- Coordinate JSON shape остаётся explicit DTO `{ "x", "y", "z" }`; JVector не сериализуется
  напрямую ни JsonUtility, ни System.Text.Json.
- Existing field names `requestId`, `levelId`, `start`, `destination`, `points`,
  `clientArtifactHash`, `clientPathFingerprint`, `artifactHash`, `pathFingerprint`,
  `serverMismatchDetected` сохраняются.
- V2 добавляет явные `protocolVersion` и `runtimeCompatibilityId` в authoritative request/response.
- Server проверяет version/identity до DotRecast query. Mismatch возвращает typed error и не
  конструирует/не мутирует query state.
- После migration production server не принимает отсутствующий/v1 compatibility contract как v2.
  Если временный compatibility endpoint понадобится, он требует отдельного решения и отдельного
  имени/маршрута; silent fallback запрещён.
- JSON golden fixtures фиксируют casing, finite-value policy, `-0`, NaN/Infinity rejection и exact
  conversion between wire DTO and JVector.

Это меняет protocol compatibility преднамеренно, даже если обычные x/y/z JSON bytes могли бы
остаться теми же: fail-closed runtime identity важнее silent wire compatibility.

## Решение 5. Navigation artifact schema и re-bake

Выбран **navigation manifest schema 2**.

- Existing DotRecast binary payload format может остаться byte-identical, но manifest получает
  `runtimeCompatibilityId` и protocol/schema identity.
- Любой artifact schema 1 считается legacy и не загружается новой authoritative runtime/server
  как текущий artifact.
- Обязательная migration policy — re-bake/re-export полным комплектом Unity asset, payload и
  manifest.
- Runtime legacy reader не добавляется. Допустим только отдельный offline diagnostic/migration
  reader, который не выдаёт schema 1 за schema 2 и не участвует в gameplay/server startup.
- Artifact hash по-прежнему относится к exact payload bytes; compatibility identity хранится и
  проверяется отдельно, а не подменяет content hash.

## Решение 6. Authoritative types и scalar policy

- Authoritative positions/directions/path points: `JVector`.
- Authoritative scalar alias: source-level `Real`, canonical production profile `f32`.
- Deterministic operations, которые входят в fingerprint/quantization/compatibility:
  public `StableMath` или package-owned integer/bit-defined code.
- Unity serialized fields, Transform/Bounds/Mesh input, gizmos, camera/UI остаются Unity types.
- Stopwatch timestamps, latency, HTTP timeout, editor progress и diagnostic telemetry остаются
  host-native `double`/`float`; они не входят в world identity.
- Scalar, влияющий на query admission/order/result, не считается telemetry только из-за имени
  budget/time и проходит отдельный semantic test.

## Решение 7. Единственная DotRecast boundary

Все `JVector`↔`RcVec3f` conversions принадлежат одному adapter owner. Runtime scheduler, server,
editor builder и probes не заводят собственные conversion formulas.

Boundary contract фиксирует:

- component order X/Y/Z;
- f32 finite-value validation;
- canonical `-0` policy;
- отсутствие scale/axis conversion;
- conversion только при входе в DotRecast и выходе из него;
- contract/golden tests на Unity и .NET.

DotRecast остаётся единственным разрешённым `RcVec3f` owner. Public advanced APIs
`NavigationArtifactInstance.NavMesh` и `CreateQuery()` не удаляются в этой migration без
отдельного breaking decision.

## Решение 8. Fingerprint ownership

Runtime и server используют одну shared implementation. Алгоритм остаётся SHA-256 от canonical
sequence, но `Math.Round(value * 1000d, AwayFromZero)` заменяется approved deterministic
quantization contract. Изменение algorithm/version обязано:

1. иметь explicit fingerprint version;
2. иметь cross-runtime golden fixtures;
3. входить в runtimeCompatibilityId;
4. fail closed при несовпадении client/server.

Sample и editor consumers вызывают shared implementation и не содержат локальных копий.

## Решение 9. Frozen compatibility surface

### Assembly names

Без отдельного ADR нельзя переименовывать:

- `CustomNavigation.Authoring`;
- `CustomNavigation.Runtime`;
- `CustomNavigation.NavigationEditor`;
- `CustomNavigation.Client`;
- `CustomNavigation.Client.Editor`;
- `CustomNavigation.NavigationEditor.Tests`;
- server `DotRecastServer`.

### Public enum ordinals

Текущие ordinals замораживаются, даже где исходник полагается на implicit numbering:

| Enum | Frozen mapping |
| --- | --- |
| `NavigationGeometryMode` | Include=0, Block=1, Ignore=2 |
| `NavigationArea` | NotWalkable=0, Ground=1, Stairs=2, Danger=3, Crouch=4, Water=5, Road=6, Grass=7, Mud=8, Ice=9, Custom10..15=10..15 |
| `NavigationFlags` | None=0; Walk=1; Crouch=2; Swim=4; Jump=8; Door=16; Ladder=32; Disabled=64 |
| `NavigationBakeQuality` | Fast=0, Balanced=1, HighDetail=2, Custom=3 |
| `NavigationLinkType` | Jump=0, Drop=1, Ladder=2, Vault=3, Teleport=4, Scripted=5 |
| `NavigationPortalType` | Door=0, Gate=1, DestructiblePassage=2, Bridge=3, Elevator=4, Scripted=5 |
| `NavigationTestPointType` | Generic=0, TeamSpawn=1, Objective=2, BombSite=3, Extraction=4, Patrol=5, SniperPosition=6 |
| `NavigationDeviceTier` | MobileLow=0, MobileMedium=1, MobileHigh=2, Custom=3 |
| `NavigationQueryPriority` | CriticalCorrection=0, PlayerImmediate=1, CombatBot=2, VisibleBot=3, BackgroundBot=4, Prewarm=5 |
| `NavigationComputeMode` | LocalOnly=0, ServerOnly=1, ServerPredicted=2 |
| `NavigationPreviewScope` | ActiveLevel=0, Selection=1, AllLoadedLevels=2 |
| `NavigationPreviewDepth` | Visible=0, XRay=1 |
| `NavigationLevelIdOwnership` | Standalone=0, ExternalManaged=1 |
| `NavigationEditorResultStatus` | Missing=0, Valid=1, Ready=2, Changed=3, Failed=4 |
| `NavigationBakeIssueSeverity` | Info=0, Warning=1, Error=2 |

Sample-only enum ordinals также не меняются без sample migration: waypoint patrol
Loop=0/PingPong=1/Once=2.

### Serialized fields и stable keys

- Existing `[SerializeField]` names в Authoring/Runtime/Samples сохраняются. Любое необходимое
  rename использует `FormerlySerializedAs` и migration test.
- `NavigationArtifactAsset` fields, level/profile/area/link/portal/test-point IDs и project paths
  не переименовываются этим epic.
- EditorPrefs keys `CustomNavigation.NavigationHighlight.*` и
  `CustomNavigation.ScenePreview.*` сохраняются.
- Artifact file names и server data layout остаются прежними до schema-2 migration с atomic
  export.

### Public API entrypoints

Имена остаются стабильны: `NavigationArtifactLoader.Load/LoadBytes`,
`NavigationQueryScheduler.RequestPath/TryProjectPosition/Tick`,
`NavigationQuerySchedulerBehaviour.RequestPath/TryProjectPosition`,
`NavigationServerPathClient.RequestPath`, `NavigationEditorApi.Validate/Bake/GetSummary`,
`NavigationBakeCommand.Validate/BuildForClient`, server endpoints и editor menu/window entrypoints.

Типы coordinate parameters/results меняются на JVector в `0.7.0`; это объявленная breaking часть,
а не разрешение переименовывать сами operations.

## Consequences

Положительные:

- Unity и server получают одну math/runtime identity;
- fingerprint и wire conversion перестают дрейфовать;
- DotRecast leakage ограничивается одним adapter;
- mismatch определяется до query/world work;
- Unity scenes/prefabs сохраняют serialized authoring data.

Стоимость:

- public consumers перекомпилируются для `0.7.0`;
- network client/server обновляются согласованно до protocol v2;
- navigation artifacts обязательно re-bake/re-export в schema 2;
- assemblies, называющие Jitter types, требуют отдельно установленную direct reference.

## Отклонённые варианты

- Копировать Jitter DLL/source внутрь Custom Navigation.
- Получать Jitter транзитивно из Jitter Physics Baker.
- Добавить automatic UPM dependency без пользовательского решения.
- Сохранить authoritative `Vector3` и только конвертировать на server.
- Сериализовать `JVector` напрямую как JSON contract.
- Считать schema 1 совместимой только потому, что DotRecast payload bytes не изменились.
- Сохранить две fingerprint implementations с одинаковыми тестовыми значениями.
- Заменить все `float`/`double` механически, включая Stopwatch и telemetry.

## Implementation mapping и stop conditions

| Prompt | Нормативный результат ADR |
| --- | --- |
| P02 | direct separately-installed Jitter references, exact identity preflight |
| P03 | JVector/Real public runtime and server contract, Unity boundary wrappers |
| P04 | protocol v2, explicit DTO, golden JSON |
| P05 | единственный DotRecast adapter |
| P06 | shared fingerprint + StableMath |
| P07 | schema 2/runtimeCompatibilityId/fail-closed errors |
| P08 | 0.7.0 migration docs, samples, mandatory re-bake |

Implementation останавливается, если published P00 coordinates не доказаны, появляется вторая
Jitter assembly, требуется automatic dependency, невозможно сохранить serialized data, protocol
v1 должен молча приниматься как v2 или новый owned deterministic finding не имеет owner/category.
