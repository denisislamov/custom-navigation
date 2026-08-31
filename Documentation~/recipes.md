# Recipes

Каждый рецепт использует публичный API 0.6.16. Если код находится в собственной
`.asmdef`, добавьте references из [Integration](integration.md).

## 1. Запросить путь и отменить его вместе с объектом

```csharp
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEngine;

public sealed class BotPathRequester : MonoBehaviour
{
    [SerializeField] private NavigationQuerySchedulerBehaviour navigation;
    [SerializeField] private Transform destination;

    private NavigationPathHandle pending;

    public void Replan()
    {
        pending.Cancel();
        pending = navigation.RequestPath(
            transform.position,
            destination.position,
            NavigationQueryPriority.VisibleBot,
            OnCompleted);
    }

    private void OnDisable()
    {
        pending.Cancel();
    }

    private void OnCompleted(NavigationPathResult result)
    {
        if (!result.Success)
        {
            Debug.LogWarning(result.Message, this);
            return;
        }

        // Consumer owns movement along result.Points.
        Debug.Log($"Path points: {result.Points.Length}", this);
    }
}
```

**Проверяемый результат:** callback получает success/failure/canceled state; объект не
оставляет намеренно живой request после disable.

> Callback reject/eviction может выполниться прямо внутри `RequestPath`. Не стройте
> callback-логику на предположении, что поле `pending` уже содержит возвращённый handle.

## 2. Сначала спроецировать пользовательскую цель

Клик или GPS/gameplay point может лежать рядом с navmesh, но не на нём:

```csharp
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEngine;

public static class NavigationTargeting
{
    public static bool TryRequest(
        NavigationQuerySchedulerBehaviour navigation,
        Vector3 start,
        Vector3 rawDestination,
        System.Action<NavigationPathResult> completion,
        out NavigationPathHandle handle)
    {
        if (!navigation.TryProjectPosition(rawDestination, out Vector3 projected))
        {
            handle = default;
            return false;
        }

        handle = navigation.RequestPath(
            start,
            projected,
            NavigationQueryPriority.PlayerImmediate,
            completion);
        return true;
    }
}
```

**Проверяемый результат:** `false` означает, что рядом с target не найден допустимый
polygon; Physics raycast для этой проверки не используется.

## 3. Владеть scheduler без MonoBehaviour adapter

Это подходит для собственного game loop или DI service:

```csharp
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using UnityEngine;

public sealed class NavigationLoop : MonoBehaviour
{
    [SerializeField] private NavigationArtifactAsset artifact;
    [SerializeField] private NavigationPerformanceProfile performance;
    [SerializeField] private NavigationAgentProfile agent;

    public NavigationQueryScheduler Scheduler { get; private set; }

    private void Awake()
    {
        if (artifact.AgentProfileId != agent.ProfileId)
        {
            throw new System.InvalidOperationException("Artifact and agent profile differ.");
        }

        NavigationArtifactInstance loaded = NavigationArtifactLoader.Load(artifact);
        Scheduler = new NavigationQueryScheduler(loaded, performance, agent);
    }

    private void Update()
    {
        Scheduler.Tick();
    }

    private void OnDestroy()
    {
        Scheduler?.CancelAll("Navigation loop was destroyed.");
    }
}
```

**Проверяемый результат:** metrics меняются только после `Tick`; все вызовы идут с
owner thread, на котором создан scheduler.

## 4. Отправить запрос конкретной карты на сервер

```csharp
using System.Collections;
using CustomNavigation.Runtime;
using UnityEngine;

public sealed class ServerPathRequester : MonoBehaviour
{
    public IEnumerator Request(Vector3 start, Vector3 destination)
    {
        bool callbackReceived = false;
        yield return NavigationServerPathClient.RequestPath(
            NavigationServerRuntimeSettings.CurrentUrl,
            System.Guid.NewGuid().ToString("N"),
            "arena_01",
            start,
            destination,
            string.Empty,
            string.Empty,
            result =>
            {
                callbackReceived = true;
                Debug.Log(result.Success ? $"Server points: {result.Points.Length}" : result.Message);
            });

        if (!callbackReceived)
        {
            Debug.LogWarning("The request coroutine ended without a callback.");
        }
    }
}
```

Запускайте через `StartCoroutine(Request(start, destination))`. В production передавайте
реальный client artifact hash и local path fingerprint, если сравниваете prediction.

**Проверяемый результат:** request уходит на `POST /path` с `levelId = arena_01`.

## 5. Сохранить временный адрес сервера на устройстве

```csharp
using CustomNavigation.Runtime;
using UnityEngine;

public static class NavigationServerAddressUi
{
    public static bool Apply(string input)
    {
        if (!NavigationServerRuntimeSettings.TrySave(input, out string normalized, out string error))
        {
            Debug.LogError(error);
            return false;
        }

        Debug.Log("Navigation server: " + normalized);
        return true;
    }

    public static void RestoreProjectDefault()
    {
        NavigationServerRuntimeSettings.ClearOverride();
    }
}
```

Override хранится в `PlayerPrefs` только пока baseline URL в settings asset не изменился.
Не используйте его для upload token или других секретов.

## 6. Проверить и запечь карту из внешнего Editor tool

Файл должен находиться в Editor assembly:

```csharp
using CustomNavigation.Authoring;
using CustomNavigation.Editor.Api;
using UnityEngine;

public static class ExternalNavigationBake
{
    public static NavigationEditorResult Bake(
        NavigationLevel level,
        string externallyOwnedLevelId)
    {
        NavigationLevelIdBinding binding =
            NavigationLevelIdBinding.External("MyLevelPipeline", externallyOwnedLevelId);

        NavigationEditorResult validation = NavigationEditorApi.Validate(level, binding);
        if (!validation.Succeeded)
        {
            foreach (var issue in validation.Issues)
            {
                Debug.LogError(issue.Message, issue.Context);
            }
            return validation;
        }

        return NavigationEditorApi.Bake(level, binding);
    }
}
```

External binding действует только в рамках операции и не меняет serialized
`NavigationLevel.LevelId`. `Bake` не экспортирует artifact на сервер.

**Проверяемый результат:** `Status = Ready`, а `Digest`, `PayloadPath` и
`ManifestPath` заполнены.

## 7. Подписаться на общий Scene View preview state

Editor-only пример с cleanup при assembly reload:

```csharp
using CustomNavigation.Authoring;
using CustomNavigation.Editor.Api;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class NavigationPreviewBridge
{
    static NavigationPreviewBridge()
    {
        NavigationPreviewApi.Changed += OnChanged;
        AssemblyReloadEvents.beforeAssemblyReload += Dispose;
    }

    public static void ShowBakedXRay()
    {
        NavigationPreviewState current = NavigationPreviewApi.Current;
        NavigationPreviewApi.Apply(
            current.WithBaked(true).WithDepth(NavigationPreviewDepth.XRay));
    }

    private static void OnChanged()
    {
        Debug.Log("Custom Navigation preview changed.");
    }

    private static void Dispose()
    {
        NavigationPreviewApi.Changed -= OnChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
    }
}
```

`Current` читает state без записи. `Apply` изменяет те же `EditorPrefs`, что Overlay и
Preferences, и вызывает общий event один раз при реальном изменении snapshot.

## 8. Минимальный integration test артефакта

Поместите в Editor test assembly с references на Authoring/Runtime:

```csharp
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using NUnit.Framework;

public sealed class NavigationDeliveryTests
{
    private const string ArtifactPath =
        "Assets/DataSakura/CustomNavigation/Generated/Navigation/arena_01.navigation.asset";

    [Test]
    public void DeliveredArtifactLoadsAndMatchesAgent()
    {
        NavigationArtifactAsset artifact =
            UnityEditor.AssetDatabase.LoadAssetAtPath<NavigationArtifactAsset>(ArtifactPath);
        Assert.That(artifact, Is.Not.Null);

        NavigationArtifactInstance loaded = NavigationArtifactLoader.Load(artifact);
        Assert.That(loaded.PolygonCount, Is.GreaterThan(0));
        Assert.That(loaded.ArtifactHash, Is.EqualTo(artifact.ArtifactHash));
    }
}
```

Такой тест подтверждает delivered bytes/schema/hash/polygon read. Он не заменяет
runtime route, Player/IL2CPP или server/E2E acceptance.
