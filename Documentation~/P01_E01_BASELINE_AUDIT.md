# P01-E01 — baseline audit и migration classification

Дата аудита: 2026-09-01, Asia/Makassar.

Статус: **PASS для P01-E01**. Это documentation-only audit: production, test и package code не
изменялись. Переход к P02 по-прежнему требует отдельного `P00-E01 PASS` по опубликованным
canonical Jitter coordinates.

Связанные deliverables:

- [полная usage matrix](P01_E01_JITTER_MIGRATION_USAGE_MATRIX.csv);
- [migration ADR](JMP_ADR_001_CANONICAL_JITTER_DATA_MODEL.md).

## 1. Зафиксированный baseline

| Поле | Наблюдаемое значение |
| --- | --- |
| Общая рабочая ветка | `d.islamov/custom-navigation-jitter-migration` |
| Source HEAD | `106c5df5e2124dd12c1707cce6af1f3e0e73e244` |
| Source commit | `docs(package): add complete 0.6.16 documentation` |
| Package | `com.datasakura.custom-navigation` `0.6.16` |
| Source remote relation | source HEAD на один project-only commit позади `origin/main` (`4c447b4`, `update project version`) |
| Published tag | `v0.6.16` |
| Published package commit | `502091e2b115e8d9e199cc01a49066ac48bc1f70` |
| Remote | `https://github.com/denisislamov/CustomNavigation.git` |

Source HEAD, Unity project HEAD и published package commit являются разными identities. Tag
`v0.6.16` указывает на package-only publication history и не является предком source HEAD. Ни
локальный `main`, ни floating remote branch не используются вместо exact identity.

Migration worktree был чист на входе. В основном checkout сохранены и не открывались для записи
следующие пользовательские untracked paths:

- `CUSTOM_NAVIGATION_JITTER_MIGRATION_DECOMPOSITION.md`;
- `CUSTOM_NAVIGATION_JUNIOR_DEVELOPER_GUIDE.md`;
- `CUSTOM_NAVIGATION_USER_FRIENDLY_PACKAGE_PROPOSALS.md`;
- `P00_E01_CANONICAL_JITTER_PREREQUISITE_RUNBOOK.docx`;
- `P00_E01_CANONICAL_JITTER_SOURCE_AUDIT_AND_CLASSIFIER.md`;
- `TestResults/`.

## 2. Dependency и assembly inventory

### Текущий Unity graph

```text
CustomNavigation.Authoring
└── UnityEngine; Jitter отсутствует

CustomNavigation.Runtime
├── CustomNavigation.Authoring
├── DotRecast.Core.dll
└── DotRecast.Detour.dll

CustomNavigation.NavigationEditor [Editor]
├── CustomNavigation.Authoring
└── CustomNavigation.Runtime

CustomNavigation.Client [Samples]
├── CustomNavigation.Authoring
├── CustomNavigation.Runtime
└── Unity.InputSystem

CustomNavigation.Client.Editor [Editor Samples]
├── Authoring, Runtime, Client
└── CustomNavigation.NavigationEditor

CustomNavigation.NavigationEditor.Tests [Editor]
└── NavigationEditor, Authoring, Runtime
```

`CustomNavigation.Runtime` использует `overrideReferences: true` и прямо перечисляет только
DotRecast Core/Detour. Остальные asmdef не содержат precompiled references. Package `package.json`,
project `manifest.json`, `packages-lock.json`, шесть asmdef и server csproj не содержат Jitter,
Jitter2.Core или Jitter Physics Baker dependency/reference.

### Текущий server graph

```text
DotRecastServer (net9.0, TreatWarningsAsErrors)
├── Server~/lib/DotRecast.Core.dll
└── Server~/lib/DotRecast.Detour.dll
```

Server не ссылается на Unity assemblies. `DotRecast.Recast.dll` лежит в `Server~/lib`, но server
csproj его не компилирует и runtime bake не выполняет.

### Precompiled binary inventory

| Artifact | Runtime SHA-256 | Server lib SHA-256 | Результат |
| --- | --- | --- | --- |
| `DotRecast.Core.dll` | `15499143ae3c3f935d6e6a0963b5650c318f69f84c9c1d2fe7ab843e3debb4a5` | тот же | MATCH |
| `DotRecast.Detour.dll` | `300359950833fd52cea47263ae85bd1e14e01cc06e47304d4041875ca752c687` | тот же | MATCH |
| `DotRecast.Recast.dll` | `0fa499b2b42476650e3a270518fd3a31a34b129340a4157d068feabd319b275f` | тот же, не referenced server runtime | MATCH |

Jitter source/DLL внутри Custom Navigation отсутствует. Это состояние обязано сохраниться:
canonical Jitter устанавливается отдельно, а будущие assemblies получают только direct reference.

## 3. Метод source classification

Scan охватывает owned `.cs` в `Authoring`, `Runtime`, `Editor`, `Samples~`, `Server~` и `Tests`.
Комментарии, строки и char literals маскируются до поиска. Generated `Server~/bin`, `Server~/obj`,
Unity-generated root csproj и precompiled vendor DLL исключаются из source findings и учитываются
отдельно.

Искались:

- `Vector3`, `Quaternion`, `RcVec3f`, `JVector`, `JQuaternion`;
- `float`, `double`;
- `Mathf`, `MathF`, `System.Math`/`Math`;
- `NavigationPathFingerprint`;
- все объявленные path/artifact/health DTO и локальные `ServerVector3` envelopes.

Matrix содержит file, symbol, каждую строку использования, число occurrences, category, owner и
назначенное migration decision. Несколько symbols на одной source line считаются отдельными symbol
occurrences, но одной matched source line.

### Полнота scan

| Метрика | Значение |
| --- | ---: |
| Owned C# files с findings | 54 |
| Matched source lines | 1,030 |
| Symbol occurrences | 1,103 |
| Aggregated CSV rows | 190 |
| Unclassified findings | 0 |
| `JVector` / `JQuaternion` | 0 / 0 |
| `MathF` | 0 |

### Символы

| Группа | Occurrences |
| --- | ---: |
| `Vector3` | 475 |
| `Quaternion` | 16 |
| `RcVec3f` | 33 |
| `float` | 273 |
| `double` | 20 |
| `Mathf` | 159 |
| `Math` / `System.Math` | 2 |
| `NavigationPathFingerprint` | 7 |
| DTO/envelope symbols | 118 |

### Категории matched source lines

| Category | Lines | Owner / решение |
| --- | ---: | --- |
| `authoritative_runtime` | 38 | Runtime: P03 переводит coordinates/scalars на JVector/Real |
| `authoritative_server` | 3 | Server: тот же canonical Jitter data model |
| `authoritative_deterministic` | 11 | P06: один shared fingerprint и owned StableMath |
| `authoring_bake` | 78 | Editor baker: Unity boundary с немедленной canonical conversion; deterministic math review |
| `dotrecast_boundary` | 33 | P05: свести к единственной JVector↔RcVec3f boundary |
| `wire_contract` | 116 | P04: explicit protocol DTO и version strategy |
| `wire_boundary` | 11 | Runtime HTTP client conversion boundary |
| `unity_authoring_boundary` | 122 | Сохранить serialized Unity types/field names; convert once |
| `editor_presentation` | 144 | Оставить Unity types и non-authoritative editor math |
| `sample_presentation` | 426 | Оставить Unity presentation, адаптировать к новому API |
| `timing_or_telemetry` | 25 | Не заменять Stopwatch/time/budget scalars на Real автоматически |
| `test_fixture` | 23 | Обновлять вместе с owner contract, не считать production source |

## 4. Deterministic hotspots

### Runtime и server

- `Runtime/NavigationQueryScheduler.cs` хранит request/result points как `Vector3`, вызывает
  `Mathf.Max/Min` и содержит несколько `Vector3`↔`RcVec3f` conversions.
- `Runtime/NavigationPathFingerprint.cs` квантует через
  `Math.Round(value * 1000d, AwayFromZero)`.
- `Server~/Navigation/ServerNavigation.cs` принимает `Vector3Dto`, конвертирует в `RcVec3f` и
  содержит вторую независимую копию fingerprint с тем же `Math.Round`.
- `Runtime/NavigationServerPathClient.cs` имеет третий data shape — private lowercase
  `ServerVector3` — и повторно вычисляет fingerprint после JSON response.

Эти paths являются simulation/protocol affecting. Они не могут оставаться на Unity `Vector3`,
platform `Math` или двух независимо поддерживаемых fingerprint implementations.

### Editor bake

- `NavigationArtifactBuilder.cs` использует `Mathf.Round`, `Mathf.RoundToInt`, `Vector3` и локальный
  `ToRc`; эти операции влияют на baked bytes/topology.
- `NavigationNavmeshAnalysis.cs` содержит geometry math, которая влияет на analysis/validation
  result и требует отдельного deterministic-vs-diagnostic review в P06.
- `NavigationPathProbe.cs` содержит еще одну пару Unity↔RcVec3f converters; это editor probe, но
  boundary должна использовать тот же canonical adapter contract.

### Timing и telemetry исключение

`Stopwatch`, latency, frame duration, HTTP timeout, editor progress, bot pacing и performance
budget остаются `double`/`float` по host API. Они не входят в world identity, пока конкретное поле
не влияет на admission/order/result. Scheduler iteration/admission limits проверяются отдельно;
простая механическая замена всех scalars на `Real` запрещена.

## 5. DTO и converter duplication

| Owner | Текущая форма |
| --- | --- |
| Server | `Vector3Dto`, `PathRequest`, `PathResponse`, health/artifact DTO |
| Runtime HTTP client | private `ServerVector3`, `ServerPathRequest`, `ServerPathResponse` |
| Editor HTTP client | еще один `ServerVector3`, path/artifact/health envelopes |
| Samples | `ServerVector3`, server/hybrid request-response fixtures |

Server использует `JsonNamingPolicy.CamelCase`; Unity `JsonUtility` DTO содержит lowercase
`x/y/z`. Текущий wire shape поэтому фактически `{"x":...,"y":...,"z":...}`. Публичные Jitter
fields нельзя сериализовать напрямую и считать это protocol contract: explicit DTO остаётся
boundary, а его conversion должна быть общей и тестируемой.

## 6. Generated/vendor exclusions

- `Server~/obj/Release/net9.0/*.g.cs` и `*.AssemblyInfo.cs` являются generated build output.
- `Server~/bin` и `obj` DLL не являются distribution authority.
- Root Unity `.csproj` генерируются Unity и не являются ручными dependency declarations.
- DotRecast поставляется как precompiled binary; vendor source в scan отсутствует.
- `Runtime/DotRecast` и `Server~/lib` — две delivery projections одинаковых DotRecast bytes, а не
  две независимые source implementations.

## 7. Current → target ownership

| Current owner | Target owner |
| --- | --- |
| Runtime public `Vector3` path API | `CustomNavigation.Runtime` public `JVector` API |
| Server `Vector3Dto` algorithm input | shared wire DTO → canonical `JVector` before world/query work |
| Две fingerprint implementations | один shared package-owned deterministic implementation |
| Несколько `ToRc`/`ToUnity` helpers | один explicit DotRecast adapter boundary |
| Authoring serialized `Vector3`/`Quaternion` | сохранить Unity serialization; convert exactly once |
| Samples/editor visualization | сохранить Unity presentation types; adapt at API edge |
| Timing/telemetry scalars | сохранить host-native scalar contracts |

Целевой assembly graph и compatibility решения нормативно зафиксированы в ADR.

## 8. Stop list после P01

1. **P02 BLOCKED**, пока P00 не подтверждён опубликованным immutable tag/asset, remote commit SHA,
   assembly SHA, public StableMath, f32 и clean-consumer evidence. Локальный RC не заменяет это.
2. Не добавлять Jitter в `package.json` как automatic UPM dependency и не копировать DLL/source.
3. Не менять serialized field names, public enum ordinals, assembly names или endpoint/JSON names
   вне решений ADR.
4. Не смешивать protocol v1 и v2 без fail-closed negotiation.
5. Не принимать schema-1 artifact под новой runtime identity; требуется явный re-bake policy.
6. Не удалять DotRecast advanced public API и не менять DotRecast version в рамках math migration.
7. При появлении owned finding с category `unclassified` дальнейшая implementation останавливается.

## 9. P01 acceptance

### Regression evidence

| Gate | Verdict | Фактический результат |
| --- | --- | --- |
| Matrix regeneration | PASS | generated CSV совпал byte-for-byte; 190 rows, 1,103 symbol occurrences, 54 files, 0 unclassified |
| Markdown/CSV diff validation | PASS | `git diff --check`; conflict markers отсутствуют |
| Package metadata/LFS | PASS | complete `.meta`, no Git LFS pointers |
| Server compile | PASS | `DotRecastServer.csproj`, Release net9.0, 0 warnings, 0 errors |
| Unity EditMode | PASS | fresh XML: 62/62, failed 0, skipped 0 |
| Unity PlayMode | NOT RUN | Test Runner завершился Passed, но обнаружил 0 PlayMode tests; это не behavioral evidence |
| Player/IL2CPP | NOT RUN | не требуется для documentation-only P01; остаётся отдельным P10 gate |
| Clean consumer | NOT RUN | относится к P00/P02/P10, не подменяется локальным project compile |

Unity-generated root csproj не используется как самостоятельное доказательство: Release
configuration в нём не объявлена, а Debug surrogate не выдал завершённого compiler result. Fresh
EditMode XML является фактическим Unity compile/test gate. Созданные Unity Test Runner пустые
`Assets/DataSakura/...` metadata side effects удалены до diff/commit.

### Итоговые gates

| Gate | Verdict | Evidence |
| --- | --- | --- |
| Baseline identities/status | PASS | exact branch/source/published/project identities зафиксированы |
| Dependency/assembly inventory | PASS | 7 declarations; Jitter отсутствует; DotRecast hashes совпадают |
| Full owned usage classification | PASS | 1,030 lines, 1,103 occurrences, 0 unclassified |
| Deterministic/wire/DotRecast owners | PASS | hotspots и target owners назначены |
| Breaking/protocol/schema decisions | PASS | ADR accepted for subsequent prompts |
| Production code unchanged | PASS | epic добавляет только audit CSV/Markdown/ADR |
