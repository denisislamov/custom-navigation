# P02-E01 — separate-install dependency contract evidence

Дата проверки: 2026-09-01, Asia/Makassar.

Статус: **PASS для P02-E01**.

## Реализованный контракт

- Canonical Jitter остаётся отдельной prerequisite-установкой и не добавлен в `package.json`.
- В package source нет `Jitter2.Core.dll`, Jitter source или зависимости на Jitter Physics Baker.
- `CustomNavigation.Runtime` прямо ссылается на `Jitter2.Core.dll`; server принимает тот же
  artifact только через явный `CanonicalJitterRoot`.
- Одобренная identity закреплена exact coordinates: tag
  `jitter-v2.8.9-datasakura.1-rc.1`, package commit
  `508de73d6d82088d58a74fd41d7e09b70f009b1d`, DLL SHA-256
  `944666bbe73dfce5ffc5bfb18569fb0004f50e767dcbb8b471dde15242023ca6`, precision `f32`,
  source hash, compile-profile id и StableMath id из P00.
- Unity preflight требует ровно одну project-owned DLL под `Assets`, запрещает transitive
  package provider и проверяет hash, precision, public `StableMath` и compatibility identity.
- Preflight вызывается до bake/authoring validation, artifact load и scheduler query setup.
- Server выполняет тот же fail-closed contract до загрузки artifact.

Точный порядок установки и команды описаны в
[canonical-jitter-dependency.md](canonical-jitter-dependency.md).

## Regression evidence

| Gate | Результат | Evidence |
| --- | --- | --- |
| Package containment | PASS | После исключения generated `bin/obj` внутри package source отсутствуют Jitter DLL/NuGet artifacts; `package.json` не содержит Jitter dependency. |
| Package meta/LFS | PASS | `python3 tools/verify-package-meta.py`: `OK: complete .meta files, no Git LFS pointers.` |
| Unity compile + EditMode | PASS | Unity `6000.3.11f1`, graphical batchmode: 69/69 passed, 0 failed/skipped; в том числе 7/7 `CanonicalJitterContractTests`. XML: `/private/tmp/custom-navigation-p02-editmode-graphics.xml`. |
| .NET server build | PASS | Release/net9.0, explicit `CanonicalJitterRoot`: 0 warnings, 0 errors. |
| .NET copy-local/identity probe | PASS | `P02_CANONICAL_JITTER_OK ... precision=f32 exactlyOne=true`. |
| Missing .NET prerequisite | PASS (negative) | Build без `CanonicalJitterRoot` завершается ошибкой до compile/runtime load. |
| Missing/duplicate/hash/f64/identity | PASS (negative) | Typed Unity contract tests подтверждают отдельный fail-fast code для каждого класса ошибки. |
| Public navigation API | UNCHANGED | P02 не меняет signatures/DTO Navigation API; миграция остаётся в P03/P04. |

Дополнительный запуск с `-nographics` дал 68/69: единственный отказ — существующий UI-тест
`OpeningAndClosingTheWindowDoesNotCreateAssetsOrDirtyTheScene` с сообщением
`No graphic device is available to initialize the view`. Семь P02 tests при этом прошли. Этот
environmental результат не засчитан как regression PASS; authoritative gate — полный graphical
batchmode 69/69 выше.

## Evidence boundaries

P02 доказывает dependency resolution, compile и fail-fast preflight в source project. Он не
доказывает consumer import, PlayMode, IL2CPP, re-bake content identity, server/client wire
совместимость или package publication. Эти gates принадлежат последующим epic.
