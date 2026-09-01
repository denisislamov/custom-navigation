# DataSakura Custom Navigation 0.7.0

DataSakura Custom Navigation — UPM-пакет для physics-free навигации в Unity 6. Он
запекает геометрию сцены в Editor, сохраняет один детерминированный Detour-артефакт
для клиента и сервера, выполняет локальные запросы в пределах кадрового бюджета и
при необходимости отправляет запросы авторитетному HTTP-сервису.

Пакет не использует Unity Physics и встроенный Unity NavMesh. Он также не является
контроллером персонажа, системой поведения AI, сетевым фреймворком или runtime-системой
перестроения navmesh: потребитель сам двигает объекты по возвращённым точкам.

## Начните здесь

1. [Установите canonical Jitter prerequisite, затем пакет](installation.md).
2. Пройдите [Quick Start](quick-start.md) — от пустой сохранённой сцены до локального
   запроса пути.
3. Изучите [Editor Guide](editor-guide.md), если уровень готовит дизайнер.
4. Откройте [Runtime API](runtime-api.md) и [рецепты](recipes.md), если подключаете
   игровой код.
5. Используйте [Troubleshooting](troubleshooting.md), если Validate, Bake или runtime
   не дают ожидаемый результат.

*Главная точка входа — `Tools > DataSakura > Custom Navigation Window`; окно имеет
вкладки `Overview`, `Geometry`, `Bake`, `Settings` и `Diagnostics`.*

## Основные возможности

- явная разметка `MeshFilter` через `NavigationGeometrySource`;
- профили агента, surface areas, modifier volumes, off-mesh links и test points;
- Editor-only Recast bake без runtime-геометрии и без Unity Physics;
- проверяемая тройка `<levelId>.navigation.bytes`, manifest и
  `NavigationArtifactAsset` с полным SHA-256;
- owner-thread `NavigationQueryScheduler` с приоритетами, очередью, sliced queries,
  отменой и ограниченными result buffers;
- `NavigationQuerySchedulerBehaviour` для обычного Unity lifecycle;
- `NavigationServerPathClient` и reference server на .NET 9;
- публичные Editor facade `NavigationEditorApi` и `NavigationPreviewApi` для NPI и
  других внешних инструментов;
- нативный Scene View Overlay и ручная диагностика;
- импортируемый UPM sample `Navigation Demos & Bots`.

## Схема работы

```text
NavigationLevel + profiles + tagged MeshFilters
                    |
                    v
        Validate -> Editor-only Recast bake
                    |
                    v
 bytes + manifest + NavigationArtifactAsset + SHA-256
          |                            |
          v                            v
 local ArtifactLoader          Export / HTTP upload
          |                            |
          v                            v
 QueryScheduler -> gameplay     .NET 9 server -> /path
```

`Build for Client` — единственное действие, которое строит navmesh. `Export to Folder`
и `Upload to Server` доставляют уже построенные байты и не выполняют скрытый повторный
bake. Это позволяет сравнивать клиент и сервер по одному `artifactHash`.

Подробнее: [Concepts and architecture](concepts-and-architecture.md).

## Требования и подтверждённая совместимость

| Область | Статус версии 0.7.0 |
| --- | --- |
| Unity | Минимум из `package.json`: `6000.3`; migration source-regression: `6000.3.11f1` |
| Canonical Jitter | Отдельный approved f32 release; package не содержит и не устанавливает Jitter автоматически |
| Render Pipeline | Core не обращается к API Built-in/URP/HDRP; демонстрационный development-проект использует URP |
| Локальный runtime | Managed DotRecast Core + Detour; без native plugin и `unsafe` |
| Editor bake | Editor assembly + DotRecast Recast; в Player не включается |
| HTTP-клиент | Требуется `com.unity.modules.unitywebrequest` |
| Reference server | .NET 9 SDK; устанавливается из `Server~` отдельной явной командой |
| Sample | Требует `com.unity.inputsystem`; это зависимость sample, не package core |
| Mono / IL2CPP / AOT | Reflection, P/Invoke и dynamic code в собственном Runtime не используются; отдельная acceptance-проверка текущей версии на IL2CPP не выполнена |
| Платформы | `.asmdef` не ограничивают Player platforms; текущая документационная проверка не является сертификацией Android/iOS/WebGL/consoles |
| Addressables | Встроенной интеграции нет; артефакт передаётся как `NavigationArtifactAsset` |
| Runtime bake | Не поддерживается намеренно |

> **Важно.** «Нет platform restriction в `.asmdef`» не означает, что каждая платформа
> прошла Player/IL2CPP acceptance. См. [ограничения интеграции](integration.md#платформы-aot-и-stripping).

## Что читать по задаче

| Задача | Страница |
| --- | --- |
| UPM/Git/local/embedded установка | [Installation](installation.md) |
| Первый уровень за 5–15 минут | [Quick Start](quick-start.md) |
| Data flow, lifecycle, ownership | [Concepts and architecture](concepts-and-architecture.md) |
| Все вкладки, Inspector и Overlay | [Editor Guide](editor-guide.md) |
| Профили и значения полей | [Configuration](configuration.md) |
| Локальный scheduler и HTTP client | [Runtime API](runtime-api.md) |
| Новый/существующий проект и asmdef | [Integration](integration.md) |
| Facade, adapter и безопасные extension points | [Extending](extending.md) |
| Практические сценарии | [Recipes](recipes.md) |
| Симптом → проверка → решение | [Troubleshooting](troubleshooting.md) |
| Обновление, миграция и откат | [Migration and upgrading](migration-and-upgrading.md) |
| Сводный справочник типов | [API reference](api-reference.md) |

Дополнительные узкие документы:

- [поля производительности](navigation-performance.md);
- [Editor API для внешних интеграций](npi-editor-api.md);
- [имена и миграция артефактов](artifact-filenames.md);
- [миграция project-owned folders с версий до 0.6.6](package-folder-unification.md);
- после установки reference server — `NavigationServer/README.md` и
  `NavigationServer/ONBOARDING.md`.

## Граница ответственности

Пакет создаёт и проверяет данные маршрутизации. Потребитель отвечает за:

- движение, animation/root motion и обработку off-mesh traversal;
- game-specific открытие дверей, лифты и изменение доступности links;
- сетевую репликацию и authoritative gameplay validation;
- выбор момента replan, кроме budget/admission внутри scheduler;
- хранение `NavigationArtifactAsset` при смене сцен;
- device profiling и production telemetry;
- deployment, TLS/auth и эксплуатацию reference server.

## Лицензии

Код пакета распространяется по MIT (`LICENSE.md`). DotRecast поставляется по zlib;
условия находятся в `Third Party Notices.md` и `Runtime/DotRecast/LICENSE.txt`.
