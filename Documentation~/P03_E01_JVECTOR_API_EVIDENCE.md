# P03-E01 — JVector runtime API и Unity boundary evidence

Дата проверки: 2026-09-01, Asia/Makassar.

Статус: **PASS для P03-E01**.

## Intentional breaking inventory

P03 сохраняет operation names, namespaces и существующие assembly names, но намеренно меняет
coordinate types согласно `JMP-ADR-001`:

| Contract | До P03 | После P03 |
| --- | --- | --- |
| `NavigationPathResult.Points` | `Vector3[]` | `JVector[]` |
| `NavigationServerPathResult.Points` | `Vector3[]` | `JVector[]` |
| `NavigationQueryScheduler.RequestPath` | `Vector3 start, Vector3 destination` | `JVector start, JVector destination` |
| `NavigationQueryScheduler.TryProjectPosition` | `Vector3` / `out Vector3` | `JVector` / `out JVector` |
| `NavigationQuerySchedulerBehaviour` coordinate entrypoints | `Vector3` | `JVector` |
| `NavigationServerPathClient.RequestPath` coordinates | `Vector3` | `JVector` |
| `NavigationPathFingerprint.Compute` | `IReadOnlyList<Vector3>` | `IReadOnlyList<JVector>` |

Это source- и binary-breaking изменение резервирует minor release `0.7.0`. Package version в P03
ещё не меняется: metadata/migration release work принадлежит P08.

## Authoritative ownership

- Scheduler queue (`PendingQuery`), active query endpoints, path results и completion arrays
  используют canonical `JVector`.
- DotRecast output конвертируется в `JVector` сразу; временные `RcVec3f` остаются только возле
  существующего backend call и будут объединены в P05.
- Bot, local, hybrid, multi-level и server-demo route state хранится как `JVector`; Unity
  `Transform`, `LineRenderer`, camera/picking и serialized inspector fields остаются `Vector3`.
- Stopwatch, latency и Unity frame timing остаются host-native telemetry.
- Source-level `Real = System.Single` используется в canonical validation, coordinate-dependent
  scheduler extents и fingerprint placeholder; parallel `JReal`/`NavigationReal`/custom vector
  types не добавлены.

## Unity adapter boundary

Новая assembly `CustomNavigation.UnityAdapter` зависит от `CustomNavigation.Runtime` и одной
separately installed `Jitter2.Core.dll`. Она является единственным Unity presentation converter:

```text
UnityEngine.Vector3
        |
        v
NavigationUnityAdapter  -->  JVector authoritative runtime
```

Обе стороны conversion проверяются на finite canonical Real. Направление references одностороннее,
поэтому assembly cycle отсутствует. Existing `CustomNavigation.Runtime`, editor и sample assembly
names не переименованы. Serialized field names и types в Authoring/MonoBehaviour не менялись;
scene/prefab mass rewrite не нужен.

## Regression evidence

| Gate | Результат | Evidence |
| --- | --- | --- |
| Unity compile | PASS | Unity `6000.3.11f1` graphical batchmode завершился без compiler errors. |
| EditMode regression | PASS | 73/73 passed, 0 failed/skipped; XML `/private/tmp/custom-navigation-p03-editmode-final.xml`. |
| Breaking API reflection snapshot | PASS | JVector signatures, no declared public `UnityEngine.Vector3` member in runtime assembly. |
| Adapter validation | PASS | Exact roundtrip; NaN и Infinity rejected до authoritative work. |
| Scheduler regression | PASS | Existing queue, priority, cancel, deadline, workspace and path tests pass on JVector API. |
| Artifact loading | PASS | Existing artifact roundtrip/load tests remain green. |
| .NET server compile | PASS | Release/net9.0 with canonical Jitter root: 0 warnings, 0 errors. |
| Stable entrypoints | PASS | Existing `OpenServerTab`/`OpenArtifactsTab` reflection test remains green. |
| Enum ordinals | PASS | No enum source changed; targeted compute-mode/query-priority ordinal test passes. |
| Package meta/LFS | PASS | `tools/verify-package-meta.py`: complete meta files, no LFS pointers. |

## Evidence boundaries

P03 не меняет protocol version/JSON codec (P04), не объединяет DotRecast adapters (P05), не
переписывает fingerprint algorithm на StableMath (P06), не меняет artifact schema (P07) и не
обновляет package version (P08). PlayMode, IL2CPP, fresh consumer import и release publication не
заявлены этим scoped regression.
