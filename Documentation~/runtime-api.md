# Runtime API

Версия документа: **Custom Navigation 0.6.16**.

Runtime загружает заранее запечённый Detour navmesh, выполняет локальные sliced-запросы
в рамках покадрового бюджета и, при необходимости, отправляет путь на reference HTTP server.
Runtime **не** выполняет Recast bake, не использует Unity Physics и не использует Unity NavMesh.

## Assemblies и namespaces

| Assembly | Namespace | Что подключает consumer |
| --- | --- | --- |
| `CustomNavigation.Authoring` | `CustomNavigation.Authoring` | `NavigationArtifactAsset`, `NavigationAgentProfile`, `NavigationPerformanceProfile`, настройки сервера и enums. |
| `CustomNavigation.Runtime` | `CustomNavigation.Runtime` | Artifact loader, local scheduler, MonoBehaviour adapter, HTTP client и path fingerprint. |

Определения assemblies: `Authoring/CustomNavigation.Authoring.asmdef` и
`Runtime/CustomNavigation.Runtime.asmdef` относительно корня package.

В asmdef gameplay-сборки добавьте ссылки на обе assembly. Для обычного API прямые ссылки
на DotRecast DLL не нужны. Они требуются только если consumer использует публичные
`NavigationArtifactInstance.NavMesh` или `CreateQuery()`.

## Рекомендуемый локальный сценарий

1. В Editor запеките navigation artifact.
2. Добавьте в сцену компонент **DataSakura/Custom Navigation/Query Scheduler**.
3. До входа в Play Mode назначьте ему `NavigationArtifactAsset`, тот же
   `NavigationAgentProfile`, с которым выполнен bake, и `NavigationPerformanceProfile`.
4. После `Awake` проверьте `IsReady`.
5. Отправляйте запросы с owner thread и обрабатывайте результат в callback.

```csharp
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEngine;

public sealed class PlayerPathRequester : MonoBehaviour
{
    [SerializeField] private NavigationQuerySchedulerBehaviour navigation;

    private NavigationPathHandle pendingRequest;
    private bool requestPending;
    private int requestVersion;

    public void RequestMove(Vector3 destination)
    {
        if (navigation == null || !navigation.IsReady)
        {
            Debug.LogError("Navigation runtime is not ready.", this);
            return;
        }

        if (requestPending)
        {
            pendingRequest.Cancel();
        }

        int version = ++requestVersion;
        requestPending = true;
        NavigationPathHandle handle = navigation.RequestPath(
            transform.position,
            destination,
            NavigationQueryPriority.PlayerImmediate,
            result => OnPathCompleted(version, result));

        // Callback нового или вытесненного старого запроса может выполниться
        // синхронно внутри RequestPath. Не затираем уже завершённое состояние.
        if (version == requestVersion && requestPending)
        {
            pendingRequest = handle;
        }
    }

    private void OnPathCompleted(int version, NavigationPathResult result)
    {
        if (version != requestVersion)
        {
            return;
        }

        requestPending = false;
        if (!result.Success)
        {
            Debug.LogWarning(result.Message, this);
            return;
        }

        ApplyPath(result.Points, result.IsPartial);
    }

    private void OnDisable()
    {
        requestVersion++;
        if (requestPending)
        {
            pendingRequest.Cancel();
            requestPending = false;
        }
    }

    private void ApplyPath(Vector3[] points, bool isPartial)
    {
        // Передайте точки в movement-код игры. Сохраняйте координату Y.
    }
}
```

`RequestPath` требует непустой callback. Успешный result содержит минимум одну точку;
`IsPartial` означает, что Detour вернул ограниченный corridor, а не полный путь до цели.

## Lifecycle `NavigationQuerySchedulerBehaviour`

Компонент имеет `[DefaultExecutionOrder(-500)]` и использует следующий lifecycle:

```text
serialized references / Configure before activation
  -> Awake: Load artifact + create scheduler and workspaces
  -> Update: Tick scheduler, then check the warning threshold
  -> OnDestroy: CancelAll and synchronously deliver canceled results
```

- `Awake` — единственная попытка инициализации. Ошибка логируется, после чего component
  получает `enabled = false`.
- `Configure(...)` только присваивает ссылки. Он не загружает artifact, не включает component
  и не пересоздаёт уже существующий scheduler.
- Поэтому нельзя вызывать `AddComponent<NavigationQuerySchedulerBehaviour>()` на активном
  GameObject и затем рассчитывать на `Configure`: `Awake` уже успеет выполниться. Для runtime
  создания сначала деактивируйте GameObject, добавьте и настройте component, затем активируйте его.
- `OnDisable` не отменяет запросы. Пока component выключен, `Update` не вызывает `Tick`, поэтому
  queued и active requests остаются незавершёнными. Отменяйте собственные handles либо уничтожайте
  component, если ожидание больше не нужно.
- `OnDestroy` вызывает callbacks через `CancelAll`. Callback должен выдерживать вызов во время
  уничтожения сцены и не должен безусловно создавать новый запрос в уничтожаемый scheduler.

## Owner thread и callback contract

`NavigationQueryScheduler` запоминает `Thread.CurrentThread.ManagedThreadId` в конструкторе.
Следующие операции разрешены только с этого же потока:

- `RequestPath`;
- `TryProjectPosition`;
- `Cancel` и `NavigationPathHandle.Cancel`;
- `Tick`;
- `CancelAll`.

Нарушение приводит к `InvalidOperationException`. Поля `BackgroundWorkerCount` в профиле не
создают workers; Jobs, Tasks и внутренней многопоточности в runtime нет.

Callbacks выполняются на owner thread и могут быть reentrant:

- обычное завершение, queued expiration и deferred cancellation приходят внутри `Tick`;
- reject нового запроса или eviction старого при полной очереди вызывают callback прямо внутри
  `RequestPath`, до его возврата;
- `CancelAll` вызывает callbacks синхронно;
- исключение consumer callback перехватывается и пишется через `Debug.LogException`.

Не удерживайте locks, не меняйте коллекцию, по которой сейчас идёт gameplay-обход, и не
предполагайте, что callback обязательно придёт в следующем кадре. Если callback запускает новый
запрос, защитите состояние request version/token-ом consumer-а.

## Очередь, приоритеты и бюджеты

Чем меньше numeric value `NavigationQueryPriority`, тем выше приоритет:

1. `CriticalCorrection` (`0`);
2. `PlayerImmediate` (`1`);
3. `CombatBot` (`2`);
4. `VisibleBot` (`3`);
5. `BackgroundBot` (`4`);
6. `Prewarm` (`5`).

При полной очереди более важный новый запрос вытесняет один худший queued request. Иначе
новый запрос отклоняется. В обоих случаях result имеет `Success == false`, а
`RejectedQueries` увеличивается.

`QueryDeadlineSeconds` ограничивает только ожидание в очереди. После admission active sliced
query не имеет общего timeout и продолжается в последующих `Tick`. `FrameBudgetMilliseconds`
и iteration limits проверяются между шагами: один DotRecast step или consumer callback может
сам превысить оставшийся бюджет.

`TryProjectPosition` — отдельный синхронный `FindNearestPoly`; он не проходит через очередь и
не учитывается как budgeted path request. Не вызывайте его массово для всех агентов каждый кадр.

### Смысл metrics

| Поле | Смысл |
| --- | --- |
| `QueuedQueries` | Ожидающие admission запросы. |
| `ActiveQueries` | Запущенные sliced queries. |
| `CompletedQueries` | Все доставленные callbacks: success, failure, cancel, expiration и reject. |
| `RejectedQueries` | Queue rejection и eviction; это подмножество completed callbacks. |
| `TotalIterations` | Сумма завершённых DotRecast iterations. |
| `LastFrameIterations` | Iterations последнего `Tick`. |
| `LastFrameMilliseconds` | Полная длительность последнего `Tick`, включая синхронные callbacks. |

## Не изменяйте profiles на работающем scheduler

Scheduler хранит ссылку на `NavigationPerformanceProfile`, но не все значения читает одинаково:

- количество workspaces, размеры polygon corridor и straight-path buffers фиксируются в ctor;
- agent filter, area costs и nearest-poly extents также фиксируются в ctor;
- frame, iteration, queue, admission, concurrency и deadline limits читаются позднее из живого asset.

Поэтому runtime-вызов `ApplyStartingPreset` или изменение serialized values после создания
scheduler не поддерживается. Особенно опасно повышение `MaximumConcurrentSlicedQueries`:
admission увидит новый лимит, а workspace pool останется старого размера.

Безопасная последовательность смены профиля:

1. прекратить приём gameplay requests;
2. вызвать `CancelAll` и обработать его синхронные callbacks;
3. отбросить старый scheduler;
4. создать новый scheduler с неизменяемыми на время его жизни artifact/agent/performance assets;
5. снова разрешить requests.

`RouteCacheEntries`, `MemoryBudgetMegabytes`, `BackgroundWorkerCount` и
`CollectProductionMetrics` сохранены для сериализационной совместимости, но route cache,
memory enforcement, workers и production telemetry в runtime 0.6.16 отсутствуют.

## Artifact validation и agent identity

Рекомендуемый вход — `NavigationArtifactLoader.Load(NavigationArtifactAsset)`. Он проверяет:

- наличие binary `TextAsset`;
- exact schema `1`;
- exact DotRecast version `2026.1.3`;
- SHA-256 binary;
- наличие полигонов;
- соответствие polygon count, если metadata count больше нуля.

`LoadBytes(...)` предназначен для advanced external delivery. Он проверяет bytes/hash/polygons,
но не получает и не проверяет schema/DotRecast version — эти проверки обязан выполнить caller.

Runtime не сравнивает `artifact.AgentProfileId` с `agentProfile.ProfileId`. Перед созданием
scheduler проверяйте это явно:

```csharp
using System;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;

public static class NavigationRuntimeFactory
{
    public static NavigationQueryScheduler Create(
        NavigationArtifactAsset artifact,
        NavigationPerformanceProfile performance,
        NavigationAgentProfile agent)
    {
        NavigationArtifactInstance loaded = NavigationArtifactLoader.Load(artifact);
        if (!string.Equals(
                loaded.AgentProfileId,
                agent.ProfileId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Artifact agent '{loaded.AgentProfileId}' does not match '{agent.ProfileId}'.");
        }

        return new NavigationQueryScheduler(loaded, performance, agent);
    }
}
```

## Authoritative HTTP path

`NavigationServerPathClient.RequestPath(...)` возвращает `IEnumerator`; caller запускает его
как coroutine. В overload с `levelId` задавайте стабильный ID уровня. Пустой ID просит сервер
использовать его active map.

```csharp
using CustomNavigation.Runtime;
using UnityEngine;

public sealed class ServerPathRequester : MonoBehaviour
{
    public void Request(
        string levelId,
        Vector3 start,
        Vector3 destination,
        string clientArtifactHash,
        string localPathFingerprint)
    {
        StartCoroutine(NavigationServerPathClient.RequestPath(
            NavigationServerRuntimeSettings.CurrentUrl,
            System.Guid.NewGuid().ToString("N"),
            levelId,
            start,
            destination,
            clientArtifactHash,
            localPathFingerprint,
            OnCompleted));
    }

    private void OnCompleted(NavigationServerPathResult result)
    {
        if (!result.Success)
        {
            Debug.LogWarning(result.Message, this);
            return;
        }

        bool authoritativeCorrectionRequired = result.ServerMismatchDetected;
        ApplyServerPath(result.Points, authoritativeCorrectionRequired);
    }

    private void ApplyServerPath(Vector3[] points, bool correctionRequired)
    {
        // Consumer-owned movement/correction.
    }
}
```

Client не выполняет retries и не предоставляет CancellationToken. Для отмены храните returned
Coroutine и вызывайте `StopCoroutine`; при таком завершении completion callback не гарантирован.
Обычные transport/protocol failures возвращаются как `Success == false`.

`NavigationServerRuntimeSettings.CurrentUrl` берёт base URL из
`Resources/CustomNavigation/NavigationServerSettings` и при наличии применяет PlayerPrefs override.
Default — `http://127.0.0.1:5079`; на телефоне loopback указывает на телефон, поэтому используйте
доступный адрес сервера в LAN. Reference server поддерживает HTTP, но не TLS. Android/iOS
cleartext policy и WebGL mixed-content/CORS зависят от consumer project и deployment.

## Платформы, IL2CPP и AOT

`CustomNavigation.Runtime.asmdef` не ограничивает список платформ. Bundled
`DotRecast.Core.dll` и `DotRecast.Detour.dll` включены для Any Platform; Editor-only
`DotRecast.Recast.dll` в player не попадает, поэтому runtime bake недоступен по дизайну.

C# слой Runtime не использует `unsafe`, P/Invoke, reflection, `dynamic` или Jobs/Burst; локальный
scheduler не создаёт worker threads. В package нет `link.xml`/`[Preserve]`: штатные public entry
points вызываются прямыми C# references, но reflection-only consumer integration обязана сама
добавить linker preservation. DLL собраны как .NET Standard 2.1; фактическую совместимость с
конкретным IL2CPP target подтверждайте player build/run этого target, а не только Editor tests.

## Ошибки и ожидаемая реакция

| Ситуация | Результат | Действие consumer-а |
| --- | --- | --- |
| Нет artifact/data, неверная schema/version/hash, пустой navmesh | Exception из loader; Behaviour логирует её и выключается | Не запускать gameplay; заново bake/export и проверить назначенный asset. |
| `AgentProfileId` не совпадает | Runtime автоматически не обнаруживает | Сравнить ID до ctor, как в примере выше. |
| `RequestPath` до readiness | `InvalidOperationException` у Behaviour | Дождаться успешного `Awake`, проверить `IsReady`. |
| Null completion | `ArgumentNullException` | Всегда передавать callback. |
| Вызов не с owner thread | `InvalidOperationException` | Маршалить запрос на Unity main thread. |
| Queue full / eviction | Синхронный failed result | Обработать backpressure; не делать немедленный бесконечный retry. |
| Start/destination вне search extents | Failed result с пустыми points | Проверить Y, agent dimensions и исходную позицию; при необходимости вызвать projection один раз. |
| Request отменён | `IsCanceled == true`, callback при `Tick` или `CancelAll` | Считать terminal result и не применять points. |
| HTTP недоступен/timeout/invalid response | `NavigationServerPathResult.Success == false` | Показать diagnostic, применить consumer retry/fallback policy. |

Полный список signatures находится в [API Reference](api-reference.md), а поддерживаемые способы
адаптации — в [Extending](extending.md).
