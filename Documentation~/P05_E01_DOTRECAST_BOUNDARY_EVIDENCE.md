# P05-E01 — single DotRecast boundary evidence

Дата проверки: 2026-09-01, Asia/Makassar.

Статус: **PASS для P05-E01**.

## Единственный adapter

`Runtime/NavigationDotRecastAdapter.cs` — единственный owned owner двух conversion formulas:

```text
ToDotRecast(in JVector) -> RcVec3f
FromDotRecast(in RcVec3f) -> JVector
```

Файл source-linked в DotRecastServer, поэтому Unity и .NET не поддерживают две копии semantics.
Adapter internal; `RcVec3f` не добавлен в public package API. Editor получает internal access через
явный `InternalsVisibleTo`, не через public leakage.

Обе функции сначала проверяют canonical f32 profile и finite components. Simulated f64 завершается
typed `DoublePrecisionUnsupported` до construction/narrowing DotRecast value.

## Conversion containment

- Scheduler projection, query endpoints, straight-path output и active state используют adapter.
- Server path input/output использует тот же linked adapter.
- Editor path probe и bake/roundtrip boundaries проходят `UnityAdapter -> JVector -> DotRecastAdapter`.
- Локальные `ToRc`/`FromRc`/`ToUnity(RcVec3f)` helpers удалены.
- Остальные `RcVec3f` occurrences являются DotRecast working state: extents, query out values,
  corridor endpoints и vendor API buffers, а не independent coordinate conversion formulas.
- `Runtime/DotRecast/*.dll` и `Server~/lib/*.dll` не изменялись.

## Regression evidence

| Gate | Результат | Evidence |
| --- | --- | --- |
| Unity compile + EditMode | PASS | Unity `6000.3.11f1`: 79/79 passed, 0 failed/skipped; XML `/private/tmp/custom-navigation-p05-editmode-1.xml`. |
| Shared Unity/.NET boundary corpus | PASS | `P05_DOTRECAST_BOUNDARY_OK values=9 negatives=3`. |
| f32 bit preservation | PASS | Exact component bits for +0, -0, normal, subnormal, minimum normal and max finite values in both directions. |
| Negative finite/precision | PASS | NaN and Infinity rejected; f64 guard throws before conversion. |
| Public API reflection | PASS | No exported runtime member exposes `RcVec3f`; existing advanced `NavMesh/CreateQuery` API remains unchanged. |
| DotRecastServer build | PASS | Release/net9.0: 0 warnings, 0 errors. |
| Package meta/LFS | PASS | `tools/verify-package-meta.py`: complete meta files, no LFS pointers. |

## Evidence boundaries

P05 does not replace DotRecast, alter its binaries, change query algorithms, rewrite fingerprint
math (P06), or claim PlayMode/IL2CPP/fresh-consumer coverage.
