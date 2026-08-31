# Extending Custom Navigation

Версия документа: **Custom Navigation 0.6.16**.

Runtime расширяется через композицию. Package не предоставляет runtime interfaces, service
registry или наследуемую base class: основные типы `sealed`, а helpers `static`. Создавайте
consumer-owned adapters вокруг public API и оставляйте package-owned serialized data неизменными.

## Поддерживаемые точки композиции

| Задача | Используйте | Не используйте |
| --- | --- | --- |
| Gameplay path service | Wrapper над `NavigationQuerySchedulerBehaviour` или `NavigationQueryScheduler` | Reflection к private fields Behaviour. |
| Собственный lifecycle/tick | Direct `NavigationQueryScheduler` в consumer-owned MonoBehaviour/service | Наследование от sealed scheduler/Behaviour. |
| Собственная доставка artifact | Проверка внешних metadata, затем `NavigationArtifactLoader.LoadBytes` | Вызов `LoadBytes` без schema/version/hash policy. |
| Retry/fallback/circuit breaker | Wrapper coroutine над `NavigationServerPathClient` | Изменение private wire DTO package. |
| Hybrid correction | Сравнение artifact hash, fingerprint и `ServerMismatchDetected` | Считать совпадение fingerprint единственной проверкой карты. |
| Низкоуровневый Detour | `NavigationArtifactInstance.NavMesh` / `CreateQuery` как advanced API | Предполагать стабильность DotRecast API между version upgrades. |
| Dependency injection | Consumer-owned interface и adapter | Ожидать package service locator или registration callbacks. |

## Consumer-owned interface

Package намеренно не навязывает DI framework. Игровой код может определить узкий интерфейс и
адаптировать к нему component:

```csharp
using System;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEngine;

public interface IGameNavigation
{
    bool IsReady { get; }

    NavigationPathHandle RequestPath(
        Vector3 start,
        Vector3 destination,
        NavigationQueryPriority priority,
        Action<NavigationPathResult> completion);

    bool TryProject(Vector3 position, out Vector3 projected);
}

public sealed class CustomNavigationAdapter : IGameNavigation
{
    private readonly NavigationQuerySchedulerBehaviour behaviour;

    public CustomNavigationAdapter(NavigationQuerySchedulerBehaviour behaviour)
    {
        this.behaviour = behaviour != null
            ? behaviour
            : throw new ArgumentNullException(nameof(behaviour));
    }

    public bool IsReady => behaviour.IsReady;

    public NavigationPathHandle RequestPath(
        Vector3 start,
        Vector3 destination,
        NavigationQueryPriority priority,
        Action<NavigationPathResult> completion)
    {
        return behaviour.RequestPath(start, destination, priority, completion);
    }

    public bool TryProject(Vector3 position, out Vector3 projected)
    {
        return behaviour.TryProjectPosition(position, out projected);
    }
}
```

Interface принадлежит consumer-у, поэтому игра может подменить local adapter тестовым double или
собственным remote implementation, не расширяя public surface package.

## Собственный scheduler driver

Direct scheduler подходит, если нужен иной execution order, DI composition root или явное
управление временем жизни. Driver всё равно должен вызывать `Tick` с того потока, на котором
создан scheduler.

```csharp
using System;
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEngine;

[DefaultExecutionOrder(-500)]
public sealed class GameNavigationRuntime : MonoBehaviour
{
    [SerializeField] private NavigationArtifactAsset artifact;
    [SerializeField] private NavigationAgentProfile agent;
    [SerializeField] private NavigationPerformanceProfile performance;

    private NavigationArtifactInstance loadedArtifact;
    private NavigationQueryScheduler scheduler;

    public NavigationQueryScheduler Scheduler => scheduler;
    public bool IsReady => scheduler != null;

    private void Awake()
    {
        loadedArtifact = NavigationArtifactLoader.Load(artifact);
        EnsureMatchingAgent(loadedArtifact, agent);
        scheduler = new NavigationQueryScheduler(loadedArtifact, performance, agent);
    }

    private void Update()
    {
        scheduler?.Tick();
    }

    private void OnDestroy()
    {
        NavigationQueryScheduler old = scheduler;
        scheduler = null;
        old?.CancelAll("Game navigation runtime was destroyed.");
    }

    public void ReplacePerformanceProfile(NavigationPerformanceProfile replacement)
    {
        if (replacement == null)
        {
            throw new ArgumentNullException(nameof(replacement));
        }

        // CancelAll вызывает callbacks синхронно. Сначала убираем старый scheduler
        // из доступного состояния, чтобы callback не поставил работу обратно в него.
        NavigationQueryScheduler old = scheduler;
        scheduler = null;
        old?.CancelAll("Navigation performance profile changed.");

        performance = replacement;
        scheduler = new NavigationQueryScheduler(loadedArtifact, performance, agent);
    }

    private static void EnsureMatchingAgent(
        NavigationArtifactInstance loaded,
        NavigationAgentProfile profile)
    {
        if (profile == null || !string.Equals(
                loaded.AgentProfileId,
                profile.ProfileId,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The navigation artifact and agent profile do not match.");
        }
    }
}
```

`ReplacePerformanceProfile` показывает обязательное пересоздание. Не меняйте живой profile через
`ApplyStartingPreset`: workspaces, buffers, filter и projection extents уже зафиксированы ctor-ом.
Если нужно заменить agent или artifact, применяйте ту же последовательность с повторной загрузкой
и identity validation.

## Безопасная внешняя доставка artifact

`LoadBytes` полезен для Addressables, CDN, encrypted storage или consumer-owned cache, но package
не знает внешний manifest. Проверьте compatibility до десериализации:

```csharp
using System;
using System.IO;
using CustomNavigation.Runtime;

public static class ExternalNavigationArtifactLoader
{
    public static NavigationArtifactInstance Load(
        string levelId,
        string schemaVersion,
        string dotRecastVersion,
        string artifactHash,
        string agentProfileId,
        int polygonCount,
        byte[] bytes)
    {
        if (!string.Equals(
                schemaVersion,
                NavigationArtifactLoader.SupportedSchemaVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported schema '{schemaVersion}'.");
        }

        if (!string.Equals(
                dotRecastVersion,
                NavigationArtifactLoader.SupportedDotRecastVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported DotRecast version '{dotRecastVersion}'.");
        }

        return NavigationArtifactLoader.LoadBytes(
            levelId,
            artifactHash,
            agentProfileId,
            polygonCount,
            bytes);
    }
}
```

После `LoadBytes` отдельно сравните `AgentProfileId` с runtime agent. Не принимайте
`expectedPolygonCount = 0` как полноценную metadata validation: это отключает сравнение count.

## Retry и fallback для HTTP client

Package выполняет один request. Retry policy принадлежит игре: она знает, можно ли сохранить
локальный predicted path, сколько ждать и когда показать offline state.

```csharp
using System.Collections;
using CustomNavigation.Runtime;
using UnityEngine;

public sealed class NavigationServerRetry : MonoBehaviour
{
    public IEnumerator RequestWithRetry(
        string levelId,
        Vector3 start,
        Vector3 destination,
        string artifactHash,
        string localFingerprint,
        System.Action<NavigationServerPathResult> completion)
    {
        NavigationServerPathResult last = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            yield return NavigationServerPathClient.RequestPath(
                NavigationServerRuntimeSettings.CurrentUrl,
                $"retry-{GetInstanceID()}-{attempt}",
                levelId,
                start,
                destination,
                artifactHash,
                localFingerprint,
                result => last = result);

            if (last != null && last.Success)
            {
                completion?.Invoke(last);
                yield break;
            }

            if (attempt < 2)
            {
                yield return new WaitForSecondsRealtime(0.25f * (1 << attempt));
            }
        }

        completion?.Invoke(last ?? new NavigationServerPathResult
        {
            Message = "Navigation request ended without a result."
        });
    }
}
```

Не запускайте бесконечный immediate retry: queue/server overload станет хуже. Для
`ServerPredicted` обычно безопаснее продолжить уже применённый local path, записать diagnostic и
повторить позже. Для `ServerOnly` consumer решает, остановить агента или использовать последний
подтверждённый path.

## Callback reentrancy и request version

При queue rejection callback может выполниться до того, как `RequestPath` вернул handle. При
смене цели старый callback также может прийти позднее. Храните consumer request version:

```csharp
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEngine;

public sealed class VersionedPathConsumer : MonoBehaviour
{
    private int requestVersion;

    public void RequestVersioned(
        NavigationQuerySchedulerBehaviour navigation,
        Vector3 start,
        Vector3 destination)
    {
        int version = ++requestVersion;
        navigation.RequestPath(
            start,
            destination,
            NavigationQueryPriority.VisibleBot,
            result =>
            {
                if (version != requestVersion || result.IsCanceled)
                {
                    return;
                }

                Consume(result);
            });
    }

    private void Consume(NavigationPathResult result)
    {
        // Consumer-owned state transition.
    }
}
```

Если callback меняет коллекцию агентов, отложите изменение до безопасной точки gameplay loop.
Package перехватывает исключение callback, но не откатывает уже изменённое consumer state.

## Низкоуровневый DotRecast

`NavigationArtifactInstance.CreateQuery()` создаёт независимый `DtNavMeshQuery` над тем же
`DtNavMesh`. Это подходит для специализированного consumer query, которого нет в scheduler.

Ограничения advanced пути:

- consumer сам отвечает за thread ownership, budgets, buffers и cancellation;
- package не гарантирует стабильность DotRecast types между version upgrades;
- нельзя выполнять runtime Recast bake: `DotRecast.Recast.dll` импортируется только для Editor;
- прямое изменение общего `DtNavMesh` может нарушить scheduler queries и artifact determinism;
- при asmdef с `overrideReferences: true` consumer должен явно добавить нужные DotRecast DLL.

Предпочитайте `RequestPath` и `TryProjectPosition`, пока они покрывают задачу.

## Расширение server protocol

Runtime wire DTO в `NavigationServerPathClient` private и не предназначены для наследования.
Для нового endpoint создайте отдельный consumer-owned client. Для совместимости существующего
`POST /path` сохраняйте:

- camelCase fields;
- `requestId`, optional `levelId`, `start`, `destination`;
- `clientArtifactHash`, `clientPathFingerprint`;
- response artifact hash, path fingerprint и mismatch flag;
- миллиметровый fingerprint algorithm на обеих сторонах.

Reference server и local scheduler используют разные query filters, search extents и result
limits. Hybrid integration должна сравнивать не только fingerprint, но также artifact hash и
`ServerMismatchDetected`.

## Что требует major-version решения

Не делайте как локальный consumer patch:

- изменение public namespace/assembly/signature;
- перестановку enum ordinals;
- переименование serialized fields;
- замену schema или DotRecast binary без coordinated builder/runtime/server update;
- изменение fingerprint canonicalization только на одной стороне;
- превращение sync callback в гарантированно deferred callback или наоборот;
- добавление неявных background threads к owner-thread scheduler.

Такие изменения затрагивают source, serialized, artifact или wire compatibility и должны иметь
отдельную migration policy и regression matrix.
