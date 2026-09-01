# P04-E01 — shared JVector wire codec evidence

Дата проверки: 2026-09-01, Asia/Makassar.

Статус: **PASS для P04-E01**.

## Реализованный результат

- `NavigationPathRequest`/`NavigationPathResponse` остаются explicit protocol envelopes, а их
  coordinates/points используют `JVector`.
- Unity runtime, editor probes, samples и .NET server используют один shared
  `NavigationWireCodec` с fixed casing/order и invariant f32 numbers.
- `Vector3Dto`, `ServerVector3` и `HybridVector3` удалены из owned `.cs`; source audit даёт ноль
  matches.
- `JsonUtility` и `System.Text.Json` больше не сериализуют `/path`; они остаются только у
  несвязанных health/artifact/Unity settings contracts.
- Protocol v2 и exact runtime compatibility identity обязательны. Legacy shape fail closed.

Нормативная wire specification: [wire-protocol-v2.md](wire-protocol-v2.md).

## Shared conformance corpus

Один и тот же `Tests/Shared/NavigationWireConformanceFixtures.cs` запускается Unity test assembly
и отдельным .NET probe. Он проверяет 4 positive write/roundtrip gates и 11 negative payloads:
missing coordinate, duplicate coordinate, NaN, Infinity, f32 overflow/underflow, invalid number, invalid
JSON, legacy v1 shape, wrong protocol version и wrong runtime identity.

| Gate | Результат | Evidence |
| --- | --- | --- |
| Unity compile + EditMode | PASS | Unity `6000.3.11f1`: 74/74 passed, 0 failed/skipped; XML `/private/tmp/custom-navigation-p04-editmode-final3.xml`. |
| Unity shared corpus under `fr-FR` | PASS | `P04_WIRE_CONFORMANCE_OK valid=4 invalid=11`. |
| .NET shared corpus | PASS | Тот же marker и те же expected error categories. |
| DotRecastServer build | PASS | Release/net9.0: 0 warnings, 0 errors. |
| DTO source audit | PASS | 0 owned matches for `Vector3Dto`, `ServerVector3`, `HybridVector3`. |
| Package meta/LFS | PASS | `tools/verify-package-meta.py`: complete meta files, no LFS pointers. |

Первый cross-runtime corpus run обнаружил различную платформенную классификацию `1e999`:
.NET возвращал Infinity, Unity `TryParse=false`. Codec исправлен так, что syntactically valid
unrepresentable number в обеих средах получает `NumberOverflow`; только последующие совпадающие
runs засчитаны как PASS.

## Evidence boundaries

P04 не объединяет JVector/DotRecast conversion (P05), не меняет fingerprint algorithm (P06), не
вводит artifact schema 2 (P07) и не доказывает сетевой two-process/IL2CPP/consumer regression.
