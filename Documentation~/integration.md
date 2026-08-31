# Integration guide

## Новый проект

Для нового проекта рекомендуемый порядок такой:

1. Установить exact tag по [Installation](installation.md).
2. Дождаться завершения compile без ошибок.
3. Создать project defaults через
   `Edit > Project Settings > DataSakura > Custom Navigation > Create Defaults`.
4. Пройти [Quick Start](quick-start.md) в сохранённой сцене.
5. Только затем импортировать `Navigation Demos & Bots`, если нужен sample.

Core package не требует Input System. Он становится обязательным только при импорте
текущего sample, потому что `CustomNavigation.Client.asmdef` ссылается на
`Unity.InputSystem`.

## Существующий проект: preflight

Перед добавлением package проверьте:

- нет ли assemblies с именами `CustomNavigation.Authoring`,
  `CustomNavigation.Runtime`, `CustomNavigation.NavigationEditor`,
  `CustomNavigation.Client` или `CustomNavigation.Client.Editor`;
- нет ли legacy folders `Assets/CustomNavigation`;
- где уже хранятся generated navigation data;
- используется ли Unity NavMesh или другой pathfinding runtime под теми же gameplay
  abstractions;
- кто владеет movement, scene lifetime и cancellation;
- поддерживает ли целевая платформа HTTP к configured server address.

Если найдена pre-0.6.6 структура, сначала прочитайте
[Migration and upgrading](migration-and-upgrading.md). Не переносите `.meta` и generated
assets вручную.

> **Предупреждение.** Не импортируйте package sample рядом с legacy
> `Assets/CustomNavigation/Client`: обе копии используют assembly names
> `CustomNavigation.Client` и `CustomNavigation.Client.Editor`.

## Assembly Definition references

Gameplay assembly для локальных запросов:

```json
{
  "name": "Game.Navigation",
  "references": [
    "CustomNavigation.Authoring",
    "CustomNavigation.Runtime"
  ]
}
```

Editor-only adapter:

```json
{
  "name": "Game.Navigation.Editor",
  "references": [
    "Game.Navigation",
    "CustomNavigation.Authoring",
    "CustomNavigation.Runtime",
    "CustomNavigation.NavigationEditor"
  ],
  "includePlatforms": ["Editor"]
}
```

Namespace не заменяет assembly reference. `CustomNavigation.Editor.Api` находится в
`CustomNavigation.NavigationEditor` и не должен попадать в Player assembly.

Не добавляйте прямую ссылку на `DotRecast.Recast.dll` в gameplay: Recast включён только
для Editor bake. Runtime assembly уже содержит явные references на Core и Detour.

## Порядок инициализации

### Serialized MonoBehaviour path

Самый безопасный вариант — заранее добавить `NavigationQuerySchedulerBehaviour` в сцену
и назначить `Artifact`, `Performance Profile` и `Agent Profile` в Inspector.

```csharp
using CustomNavigation.Runtime;
using UnityEngine;

public sealed class NavigationConsumer : MonoBehaviour
{
    [SerializeField] private NavigationQuerySchedulerBehaviour navigation;

    private void Start()
    {
        if (!navigation.IsReady)
        {
            Debug.LogError("Navigation was not initialized.", navigation);
        }
    }
}
```

Scheduler behaviour имеет execution order `-500`, поэтому его `Awake` выполняется до
обычного `Start` consumer-а.

### Programmatic path

Не вызывайте `AddComponent<NavigationQuerySchedulerBehaviour>()`, а затем `Configure`
на уже активном объекте: `Awake` может выполниться до назначения ссылок. Создавайте
объект неактивным, настройте component и только затем активируйте:

```csharp
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEngine;

public static class NavigationBootstrap
{
    public static NavigationQuerySchedulerBehaviour Create(
        NavigationArtifactAsset artifact,
        NavigationPerformanceProfile performance,
        NavigationAgentProfile agent)
    {
        var owner = new GameObject("Navigation Runtime");
        owner.SetActive(false);
        var behaviour = owner.AddComponent<NavigationQuerySchedulerBehaviour>();
        behaviour.Configure(artifact, performance, agent);
        owner.SetActive(true);
        return behaviour;
    }
}
```

После `Awake` метод `Configure` не перезагружает artifact и не перестраивает buffers.
Для смены уровня/profile уничтожьте старый owner после отмены consumers и создайте новый.

## Dependency Injection

Пакет не зависит от Zenject, VContainer, Extenject, Microsoft DI или собственного
service locator. Регистрируйте один из двух объектов, которыми владеет consumer:

- scene-owned `NavigationQuerySchedulerBehaviour`;
- plain `NavigationQueryScheduler`, если ваш game loop сам вызывает `Tick`.

DI container должен управлять временем жизни и вызвать `CancelAll` перед удалением plain
scheduler. Не регистрируйте Editor builder/validator в runtime container.

## Смена сцен и domain reload

### Scene-owned runtime

При выгрузке сцены `OnDestroy` behaviour отменяет все queued/active requests. Каждый
caller всё равно должен отменять собственный handle в `OnDisable`/`OnDestroy`, чтобы его
намерение было локально понятно.

### Persistent runtime

Если объект использует `DontDestroyOnLoad`:

1. не отправляйте запросы новой карты через scheduler старого artifact;
2. дождитесь завершения или отмените старые callbacks;
3. создайте новый scheduler с новым artifact и тем же либо совместимым agent profile;
4. замените ссылку в consumer services атомарно;
5. уничтожьте старый owner.

### Editor domain reload

Window сохраняет часть UI state, но in-flight Editor HTTP request не переживает domain
reload. Validation snapshot также сбрасывается в `Not evaluated`; это ожидаемо.

## Agent/profile compatibility

Перед созданием scheduler проверяйте сами:

```csharp
if (artifact.AgentProfileId != agent.ProfileId)
{
    throw new System.InvalidOperationException(
        $"Artifact expects '{artifact.AgentProfileId}', got '{agent.ProfileId}'.");
}
```

Текущий loader не выполняет это сравнение автоматически. Неверный agent может тихо
изменить filter costs/flags и nearest-poly extents для navmesh, испечённого под другие
габариты.

Не вызывайте `ApplyStartingPreset` и не меняйте значения agent/performance profile после
создания scheduler. Constructor фиксирует filter, extents, workspace pool и result buffer
sizes. Пересоздайте scheduler.

## Сервер и сетевой код

`NavigationServerPathClient.RequestPath` — Unity coroutine. Пакет не предоставляет
`Task`, `async/await`, retries или `CancellationToken`. Остановка coroutine может
оставить consumer без callback, поэтому явно завершайте собственное состояние.

Base URL по умолчанию берётся из
`Assets/DataSakura/CustomNavigation/Resources/CustomNavigation/NavigationServerSettings.asset`.
Runtime override хранится в `PlayerPrefs` и автоматически сбрасывается, если изменился
asset baseline.

Для multiplayer передавайте `levelId`, если сервер хранит несколько карт. Artifact hash
и path fingerprint — диагностические checks; они не заменяют authoritative validation
позиции игрока.

Reference server поддерживает HTTP. Для production network deployment самостоятельно
обеспечьте TLS/reverse proxy, authentication, observability, rate limits и lifecycle.

## Addressables и другие loaders

Встроенной Addressables/AssetBundle integration нет. `NavigationArtifactAsset` содержит
ссылку на `TextAsset` payload, поэтому любой внешний loader должен доставить asset вместе
с зависимостью и удерживать их до завершения загрузки `NavigationArtifactLoader.Load`.

Если используете Addressables:

- не удаляйте generated payload/manifest из проекта до build;
- проверьте, что dependency `TextAsset` вошла в bundle;
- выполните Player acceptance на целевой платформе;
- не выдавайте успешную Editor-загрузку за подтверждение remote catalog/stripping.

Пакет не использует `Resources` для navigation artifact; только server settings имеет
фиксированный Resources path.

## UI и события приложения

Пакет не содержит gameplay UI. Показывайте loading/error state на основе:

- `NavigationQuerySchedulerBehaviour.IsReady`;
- `NavigationPathResult.Success`, `IsPartial`, `IsCanceled`, `Message`;
- `NavigationServerPathResult.Success`, `Message`, `ServerMismatchDetected`;
- `NavigationSchedulerMetrics`.

`CompletedQueries` в metrics включает failures/cancellations/rejections, а не только
успешные маршруты.

## Платформы, AOT и stripping

Подтверждено статически:

- Runtime/Authoring assemblies имеют `Any Platform`;
- Core/Detour — managed .NET Standard 2.1 DLL;
- Recast DLL отключена для Player;
- собственный Runtime не использует reflection, P/Invoke, `unsafe`, Burst/Jobs,
  dynamic code или background workers;
- `link.xml` и `[Preserve]` отсутствуют.

Не подтверждено текущим документационным релизом:

- Android/iOS IL2CPP и managed stripping;
- WebGL CORS/mixed-content;
- Android cleartext network policy;
- iOS ATS и local-network permissions;
- console platform certification.

Для каждого production target выполните минимум:

1. Player build нужного scripting backend;
2. загрузку real artifact;
3. projection + успешный и partial/failed query;
4. scene reload/cancellation;
5. server request через фактическую network policy;
6. inspection build logs на stripping/AOT errors.

## Cleanup checklist

- отменяйте handles в lifecycle caller-а;
- не ожидайте callback после остановки HTTP coroutine;
- вызывайте `CancelAll` для plain scheduler;
- не меняйте profiles «на лету»;
- не сохраняйте upload token в asset или player data;
- не держите Editor assembly reference в runtime asmdef;
- после package update повторно проверяйте imported sample copies.
