# API Reference

Версия документа: **Custom Navigation 0.7.0**.

Здесь перечислены Runtime API, центральные authoring types и поддерживаемые Editor
facade для внешних инструментов. Serialized-only components и все Inspector fields
сведены в [Configuration](configuration.md); внутренние builders/process helpers не
рекомендуются как consumer API. Сигнатуры соответствуют исходникам package;
конструкторы с модификатором `internal` не являются consumer API.

## Assembly `CustomNavigation.Runtime`

Namespace: `CustomNavigation.Runtime`. Assembly definition:
`Runtime/CustomNavigation.Runtime.asmdef`.

Canonical coordinates are `Jitter2.LinearMath.JVector`. The approved release is compiled with
`Real = System.Single`; f64 is rejected by preflight and must not be narrowed at a Unity or
DotRecast boundary.

## Assembly `CustomNavigation.UnityAdapter`

```csharp
public static class NavigationUnityAdapter
{
    public static Jitter2.LinearMath.JVector ToJitter(UnityEngine.Vector3 value);
    public static UnityEngine.Vector3 ToUnity(Jitter2.LinearMath.JVector value);
}
```

Это единственная package-owned presentation boundary. Runtime/server state и path arrays остаются
`JVector`; `Vector3` создаётся только рядом с Transform, Gizmos, Handles или UI.

### `NavigationArtifactInstance`

Файл: `Runtime/NavigationArtifactLoader.cs`.

```csharp
public sealed class NavigationArtifactInstance
{
    public string LevelId { get; }
    public string ArtifactHash { get; }
    public string AgentProfileId { get; }
    public int PolygonCount { get; }
    public DotRecast.Detour.DtNavMesh NavMesh { get; }

    public DotRecast.Detour.DtNavMeshQuery CreateQuery();
}
```

Instance создаёт loader. `NavMesh` и `CreateQuery` — advanced DotRecast escape hatch: их
использование связывает consumer с bundled DotRecast `2026.1.3`.

### `NavigationArtifactLoader`

Файл: `Runtime/NavigationArtifactLoader.cs`.

```csharp
public static class NavigationArtifactLoader
{
    public const string SupportedSchemaVersion = "2";
    public const string SupportedDotRecastVersion = "2026.1.3";

    public static NavigationArtifactInstance Load(
        CustomNavigation.Authoring.NavigationArtifactAsset artifact);

    public static NavigationArtifactInstance LoadBytes(
        string levelId,
        string expectedHash,
        string agentProfileId,
        int expectedPolygonCount,
        byte[] bytes);

    public static string ComputeSha256(byte[] data);
}
```

- `Load` до чтения payload проверяет schema, DotRecast, f32, canonical Jitter SHA-256,
  StableMath compatibility и fingerprint v2 из asset.
- `LoadBytes` этих параметров не получает; caller проверяет их до вызова.
- `expectedPolygonCount <= 0` отключает сравнение metadata count, но binary всё равно должен
  содержать хотя бы один полигон.
- Возможны `ArgumentNullException`, `InvalidOperationException`, `InvalidDataException` и
  исключения DotRecast reader.

### `NavigationCompatibilityContract`

```csharp
public static class NavigationCompatibilityContract
{
    public const string ArtifactSchemaVersion = "2";
    public const string DotRecastVersion = "2026.1.3";
    public const string Precision = "f32";
    public const string CanonicalJitterAssemblySha256 =
        "944666bbe73dfce5ffc5bfb18569fb0004f50e767dcbb8b471dde15242023ca6";
    public const string DeterministicMathCompatibilityId =
        "54b456c04074909605d2ba138e5001d39a90a338885eafcb32265483b35054b0";
    public const int FingerprintAlgorithmVersion = 2;
}
```

Runtime, manifest writer, editor exporter, server store and wire codec используют один owner.
Mismatch бросает `NavigationCompatibilityException` с точным `NavigationCompatibilityField`.

### `NavigationPathHandle`

Файл: `Runtime/NavigationQueryScheduler.cs`.

```csharp
public readonly struct NavigationPathHandle
{
    public long RequestId { get; }
    public void Cancel();
}
```

Default handle безопасен: `Cancel()` ничего не делает. Обычная отмена deferred до обработки
scheduler-ом и должна вызываться с owner thread.

### `NavigationPathResult`

Файл: `Runtime/NavigationQueryScheduler.cs`.

```csharp
public sealed class NavigationPathResult
{
    public long RequestId { get; }
    public bool Success { get; }
    public bool IsPartial { get; }
    public bool IsCanceled { get; }
    public string Message { get; }
    public Jitter2.LinearMath.JVector[] Points { get; }
    public int Iterations { get; }
    public double LatencyMilliseconds { get; }
}
```

Constructor internal. `Points` никогда не равен `null`; failed result содержит пустой array.
Latency считается от постановки в очередь до доставки callback и включает active processing.

### `NavigationSchedulerMetrics`

Файл: `Runtime/NavigationQueryScheduler.cs`.

```csharp
public readonly struct NavigationSchedulerMetrics
{
    public readonly int QueuedQueries;
    public readonly int ActiveQueries;
    public readonly int CompletedQueries;
    public readonly int RejectedQueries;
    public readonly long TotalIterations;
    public readonly int LastFrameIterations;
    public readonly double LastFrameMilliseconds;
}
```

Constructor internal. `CompletedQueries` считает все terminal callbacks, не только success.

### `NavigationQueryScheduler`

Файл: `Runtime/NavigationQueryScheduler.cs`.

```csharp
public sealed class NavigationQueryScheduler
{
    public NavigationArtifactInstance Artifact { get; }
    public CustomNavigation.Authoring.NavigationPerformanceProfile PerformanceProfile { get; }
    public NavigationSchedulerMetrics Metrics { get; }

    public NavigationQueryScheduler(
        NavigationArtifactInstance loadedArtifact,
        CustomNavigation.Authoring.NavigationPerformanceProfile performanceProfile,
        CustomNavigation.Authoring.NavigationAgentProfile agentProfile);

    public bool TryProjectPosition(
        Jitter2.LinearMath.JVector position,
        out Jitter2.LinearMath.JVector projectedPosition);

    public NavigationPathHandle RequestPath(
        Jitter2.LinearMath.JVector start,
        Jitter2.LinearMath.JVector destination,
        CustomNavigation.Authoring.NavigationQueryPriority priority,
        System.Action<NavigationPathResult> completion);

    public void Cancel(long requestId);
    public void Tick();
    public void CancelAll(string reason = "Navigation scheduler stopped.");
}
```

Ctor проверяет три аргумента на `null`, создаёт filter/extents и заранее выделяет workspaces.
Он не проверяет совпадение artifact/agent IDs. Все методы выше owner-thread only. Callback может
быть вызван синхронно из `RequestPath`, `Tick` или `CancelAll`.

### `NavigationQuerySchedulerBehaviour`

Файл: `Runtime/NavigationQuerySchedulerBehaviour.cs`.

```csharp
[UnityEngine.DefaultExecutionOrder(-500)]
[UnityEngine.DisallowMultipleComponent]
[UnityEngine.AddComponentMenu("DataSakura/Custom Navigation/Query Scheduler")]
public sealed class NavigationQuerySchedulerBehaviour : UnityEngine.MonoBehaviour
{
    public bool IsReady { get; }
    public CustomNavigation.Authoring.NavigationArtifactAsset Artifact { get; }
    public NavigationQueryScheduler Scheduler { get; }
    public NavigationSchedulerMetrics Metrics { get; }

    public void Configure(
        CustomNavigation.Authoring.NavigationArtifactAsset navigationArtifact,
        CustomNavigation.Authoring.NavigationPerformanceProfile mobilePerformance,
        CustomNavigation.Authoring.NavigationAgentProfile navigationAgent);

    public NavigationPathHandle RequestPath(
        Jitter2.LinearMath.JVector start,
        Jitter2.LinearMath.JVector destination,
        CustomNavigation.Authoring.NavigationQueryPriority priority,
        System.Action<NavigationPathResult> completion);

    public bool TryProjectPosition(
        Jitter2.LinearMath.JVector position,
        out Jitter2.LinearMath.JVector projectedPosition);
}
```

`Configure` должен завершиться до `Awake`; после `Awake` он не rebuild-ит scheduler.
`RequestPath` до readiness бросает `InvalidOperationException`; projection до readiness
возвращает `false`.

### `NavigationPathFingerprint`

Файл: `Runtime/NavigationPathFingerprint.cs`.

```csharp
public static class NavigationPathFingerprint
{
    public const int AlgorithmVersion = 2;
    public const string AlgorithmId =
        "cn-path-fingerprint-v2-mm-away-from-zero-stablemath-f32";
    public static string Compute(
        System.Collections.Generic.IReadOnlyList<Jitter2.LinearMath.JVector> points);
}
```

Возвращает lowercase SHA-256 canonical coordinates, квантованных до 1 мм. `points == null`
бросает `ArgumentNullException`.

### `NavigationComputeMode`

Файл: `Runtime/NavigationServerPathClient.cs`.

```csharp
public enum NavigationComputeMode
{
    LocalOnly = 0,
    ServerOnly = 1,
    ServerPredicted = 2
}
```

Значения сериализуются sample components; не меняйте ordinals в consumer data.

### `NavigationServerPathResult`

Файл: `Runtime/NavigationServerPathClient.cs`.

```csharp
public sealed class NavigationServerPathResult
{
    public bool Success;
    public Jitter2.LinearMath.JVector[] Points;
    public string Message;
    public string ArtifactHash;
    public string PathFingerprint;
    public bool ServerMismatchDetected;
}
```

Это mutable result DTO. Initial values — `false`, empty array и empty strings.

### `NavigationServerPathClient`

Файл: `Runtime/NavigationServerPathClient.cs`.

```csharp
public static class NavigationServerPathClient
{
    public static System.Collections.IEnumerator RequestPath(
        string baseUrl,
        string requestId,
        Jitter2.LinearMath.JVector start,
        Jitter2.LinearMath.JVector destination,
        string clientArtifactHash,
        string clientPathFingerprint,
        System.Action<NavigationServerPathResult> completion);

    public static System.Collections.IEnumerator RequestPath(
        string baseUrl,
        string requestId,
        string levelId,
        Jitter2.LinearMath.JVector start,
        Jitter2.LinearMath.JVector destination,
        string clientArtifactHash,
        string clientPathFingerprint,
        System.Action<NavigationServerPathResult> completion);

    public static string BuildUrl(string baseUrl, string path);
}
```

Пустой `baseUrl` заменяется на `NavigationServerRuntimeSettings.CurrentUrl`; overload без
`levelId` отправляет empty level и использует active server map. Caller запускает enumerator как
coroutine. Retries и cancellation abstraction отсутствуют.

### `NavigationServerRuntimeSettings`

Файл: `Runtime/NavigationServerRuntimeSettings.cs`.

```csharp
public static class NavigationServerRuntimeSettings
{
    public static string DefaultUrl { get; }
    public static string CurrentUrl { get; }
    public static bool HasRuntimeOverride { get; }
    public static int RequestTimeoutSeconds { get; }

    public static bool TrySave(
        string input,
        out string normalizedUrl,
        out string error);

    public static void ClearOverride();

    public static bool TryNormalize(
        string input,
        out string normalizedUrl,
        out string error);
}
```

`TrySave` пишет PlayerPrefs и вызывает `PlayerPrefs.Save`. `CurrentUrl` может удалить stale
override, если baseline asset изменился; чтение свойства поэтому может иметь side effect.

## Runtime-зависимые types из `CustomNavigation.Authoring`

Namespace: `CustomNavigation.Authoring`. Assembly definition:
`Authoring/CustomNavigation.Authoring.asmdef`.

### `NavigationLevel`

Файл: `Authoring/NavigationLevel.cs`. Unity создаёт этот sealed component через
`AddComponent` или one-click setup; вручную через `new` его не создают.

```csharp
public sealed class NavigationLevel : UnityEngine.MonoBehaviour
{
    public string LevelId { get; }
    public string Description { get; }
    public UnityEngine.Transform GeometryRoot { get; }
    public NavigationBuildSettings BuildSettings { get; }
    public NavigationAgentProfile DefaultAgentProfile { get; }
    public NavigationAreaCatalog AreaCatalog { get; }
    public NavigationPerformanceProfile PerformanceProfile { get; }

    public void ConfigureDefaults(
        NavigationAgentProfile agentProfile,
        NavigationAreaCatalog catalog,
        NavigationPerformanceProfile mobilePerformance);

    public bool IsReadyToBake(out string reason);
    public bool TryGetGeometryBounds(out UnityEngine.Bounds bounds);
}
```

`GeometryRoot` возвращает transform самого level, если serialized reference пуст.
`ConfigureDefaults` назначает три profile и синхронизирует agent-driven build settings;
он не запускает validation или bake. `IsReadyToBake` — строгая проверка custom
Inspector, которая требует performance profile; главное окно отдельно допускает bake
без него, потому что performance не меняет geometry. `TryGetGeometryBounds` читает
явные sources и не изменяет сцену.

### `NavigationQueryPriority`

Файл: `Authoring/NavigationAuthoringTypes.cs`.

```csharp
public enum NavigationQueryPriority
{
    CriticalCorrection = 0,
    PlayerImmediate = 1,
    CombatBot = 2,
    VisibleBot = 3,
    BackgroundBot = 4,
    Prewarm = 5
}
```

Numeric ordering участвует в scheduler algorithm. Не переставляйте значения и не вставляйте
новое значение между существующими в совместимом release.

### `NavigationDeviceTier`

Файл: `Authoring/NavigationAuthoringTypes.cs`.

```csharp
public enum NavigationDeviceTier
{
    MobileLow = 0,
    MobileMedium = 1,
    MobileHigh = 2,
    Custom = 3
}
```

Это стартовые presets, а не измеренная гарантия производительности.

### `NavigationArea`, `NavigationFlags`, `NavigationAreaCost`, `NavigationFlagsUtility`

Файл: `Authoring/NavigationAuthoringTypes.cs`.

```csharp
public enum NavigationArea
{
    NotWalkable = 0,
    Ground = 1,
    Stairs = 2,
    Danger = 3,
    Crouch = 4,
    Water = 5,
    Road = 6,
    Grass = 7,
    Mud = 8,
    Ice = 9,
    Custom10 = 10,
    Custom11 = 11,
    Custom12 = 12,
    Custom13 = 13,
    Custom14 = 14,
    Custom15 = 15
}

[System.Flags]
public enum NavigationFlags
{
    None = 0,
    Walk = 1 << 0,
    Crouch = 1 << 1,
    Swim = 1 << 2,
    Jump = 1 << 3,
    Door = 1 << 4,
    Ladder = 1 << 5,
    Disabled = 1 << 6
}

public sealed class NavigationAreaCost
{
    public NavigationArea Area { get; }
    public int AreaId { get; }
    public float Cost { get; }
}

public static class NavigationFlagsUtility
{
    public const int AllMask = 0xffff;
    public static int ToMask(NavigationFlags flags);
    public static NavigationFlags FromMask(int mask);
}
```

Area ID передаётся DotRecast как число `0..63`, но встроенный enum именует `0..15`.
`NavigationAreaCost` создаётся/настраивается Unity serialization; публичного consumer constructor
с параметрами нет. `ForbiddenMovement` имеет приоритет через exclude mask scheduler filter.

### `NavigationArtifactAsset`

Файл: `Authoring/NavigationArtifactAsset.cs`.

```csharp
public sealed class NavigationArtifactAsset : UnityEngine.ScriptableObject
{
    public string LevelId { get; }
    public string ArtifactHash { get; }
    public string SchemaVersion { get; }
    public string DotRecastVersion { get; }
    public string Precision { get; }
    public string CanonicalJitterAssemblySha256 { get; }
    public string DeterministicMathCompatibilityId { get; }
    public int FingerprintAlgorithmVersion { get; }
    public string FingerprintAlgorithmId { get; }
    public string AgentProfileId { get; }
    public int PolygonCount { get; }
    public int SourceMeshCount { get; }
    public UnityEngine.TextAsset NavigationData { get; }
    public string ManifestJson { get; }

    public void Configure(
        string newLevelId,
        string newArtifactHash,
        string newSchemaVersion,
        string newDotRecastVersion,
        string newPrecision,
        string newCanonicalJitterAssemblySha256,
        string newDeterministicMathCompatibilityId,
        int newFingerprintAlgorithmVersion,
        string newFingerprintAlgorithmId,
        string newAgentProfileId,
        int newPolygonCount,
        int newSourceMeshCount,
        UnityEngine.TextAsset newNavigationData,
        string newManifestJson);
}
```

Нормальный consumer читает generated asset, но не вызывает `Configure`; этот метод нужен
editor/build tooling и сам по себе не валидирует согласованность metadata.

### `NavigationAgentProfile`

Файл: `Authoring/NavigationAgentProfile.cs`.

```csharp
public sealed class NavigationAgentProfile : UnityEngine.ScriptableObject
{
    public string ProfileId { get; }
    public float Height { get; }
    public float Radius { get; }
    public float MaximumClimb { get; }
    public float MaximumSlope { get; }
    public NavigationFlags AllowedMovement { get; }
    public NavigationFlags ForbiddenMovement { get; }
    public int IncludedPolygonFlags { get; }
    public int ExcludedPolygonFlags { get; }
    public System.Collections.Generic.IReadOnlyList<NavigationAreaCost> AreaCosts { get; }

    public float GetAreaCost(int areaId);
    public float GetAreaCost(NavigationArea area);
}
```

Scheduler фиксирует filter/costs/extents в ctor. `MaximumSlope` и `MaximumClimb` прежде всего
участвуют в bake; runtime projection extents использует `Height` и `Radius`.

### `NavigationPerformanceProfile`

Файл: `Authoring/NavigationPerformanceProfile.cs`.

```csharp
public sealed class NavigationPerformanceProfile : UnityEngine.ScriptableObject
{
    public NavigationDeviceTier DeviceTier { get; }
    public float FrameBudgetMilliseconds { get; }
    public int MaximumIterationsPerFrame { get; }
    public int MaximumIterationsPerQueryStep { get; }
    public int MaximumNewQueriesPerFrame { get; }
    public int MaximumConcurrentSlicedQueries { get; }
    public int MaximumQueuedQueries { get; }
    public int MaximumPathPolygons { get; }
    public int MaximumStraightPathPoints { get; }
    public float CombatBotMinimumReplanSeconds { get; }
    public float VisibleBotMinimumReplanSeconds { get; }
    public float BackgroundBotMinimumReplanSeconds { get; }
    public float QueryDeadlineSeconds { get; }
    public int RouteCacheEntries { get; }
    public int MemoryBudgetMegabytes { get; }
    public int BackgroundWorkerCount { get; }
    public bool CollectProductionMetrics { get; }
    public float BudgetWarningMultiplier { get; }

    public void ApplyStartingPreset(NavigationDeviceTier tier);
}
```

Не вызывайте `ApplyStartingPreset` для profile, уже используемого живым scheduler. Replan
intervals — consumer pacing для sample, warning multiplier читает Behaviour, а route cache,
memory budget, workers и production telemetry остаются reserved compatibility fields.

### `NavigationServerSettings`

Файл: `Authoring/NavigationServerSettings.cs`.

```csharp
public sealed class NavigationServerSettings : UnityEngine.ScriptableObject
{
    public const string ResourcesFolder =
        "Assets/DataSakura/CustomNavigation/Resources/CustomNavigation";
    public const string ResourceName = "NavigationServerSettings";
    public const string ResourcePath = "CustomNavigation/NavigationServerSettings";
    public const string AssetPath =
        "Assets/DataSakura/CustomNavigation/Resources/CustomNavigation/NavigationServerSettings.asset";
    public const string DefaultHost = "127.0.0.1";
    public const int DefaultPort = 5079;
    public const string DefaultServerArtifactFolder = "NavigationServer/NavigationData";

    public string Host { get; }
    public int Port { get; }
    public bool UseHttps { get; }
    public int RequestTimeoutSeconds { get; }
    public string ServerArtifactFolder { get; }
    public string Notes { get; }
    public string BaseUrl { get; }
    public string ListenPrefix { get; }

    public static NavigationServerSettings LoadOrNull();
    public static void InvalidateCache();
    public static string ResolveBaseUrl();
    public static string Compose(string hostValue, int portValue, bool https);
    public static bool TryParse(
        string input,
        out string parsedHost,
        out int parsedPort,
        out bool parsedHttps,
        out string error);

    public bool TryApplyUrl(string input, out string error);
}
```

`LoadOrNull` использует static Resources cache. После editor-side create/delete/change вызывайте
`InvalidateCache`. Reference server в package поддерживает только HTTP.

## Editor integration API

Assembly: `CustomNavigation.NavigationEditor`. Namespace основных facade:
`CustomNavigation.Editor.Api`. Эти типы доступны только в Editor assembly; gameplay
assembly и Player build не должны на них ссылаться.

### `NavigationEditorApi`

Файл: `Editor/Api/NavigationEditorApi.cs`.

```csharp
public enum NavigationLevelIdOwnership
{
    Standalone = 0,
    ExternalManaged = 1
}

public sealed class NavigationLevelIdBinding
{
    public static NavigationLevelIdBinding Standalone { get; }
    public static NavigationLevelIdBinding External(string owner, string levelId);

    public NavigationLevelIdOwnership Ownership { get; }
    public string LevelId { get; }
    public string Owner { get; }
}

public enum NavigationEditorResultStatus
{
    Missing = 0,
    Valid,
    Ready,
    Changed,
    Failed
}

public sealed class NavigationEditorResult
{
    public NavigationEditorResultStatus Status { get; }
    public NavigationLevelIdOwnership Ownership { get; }
    public string Owner { get; }
    public string LevelId { get; }
    public NavigationArtifactAsset Artifact { get; }
    public string ArtifactPath { get; }
    public string PayloadPath { get; }
    public string ManifestPath { get; }
    public string Digest { get; }
    public int PayloadSize { get; }
    public int PolygonCount { get; }
    public int SourceMeshCount { get; }
    public System.Collections.Generic.IReadOnlyList<
        CustomNavigation.Editor.NavigationBakeIssue> Issues { get; }
    public bool Succeeded { get; }
    public bool HasStatistics { get; }
}

public static class NavigationEditorApi
{
    public static NavigationEditorResult Validate(
        NavigationLevel level,
        NavigationLevelIdBinding binding = null);

    public static NavigationEditorResult Bake(
        NavigationLevel level,
        NavigationLevelIdBinding binding = null);

    public static NavigationEditorResult ReadSummary(
        NavigationLevel level,
        NavigationLevelIdBinding binding = null);
}
```

`Validate` и `ReadSummary` не записывают scene/package data. `Bake` выполняет validation,
строит client artifact и пишет generated triplet. Для `ExternalManaged` resolved ID
действует только в текущей операции и не меняет serialized `NavigationLevel.LevelId`.
Ошибки identity/build/verification возвращаются как `Status = Failed` и `Issues`, а не
пробрасываются consumer-у как обычный control flow. Полный сценарий:
[Editor API для внешних инструментов](npi-editor-api.md).

### `NavigationPreviewApi`

Файл: `Editor/Api/NavigationPreviewApi.cs`.

```csharp
public sealed class NavigationPreviewState
{
    public NavigationPreviewState(
        bool sources,
        bool baked,
        bool runtime,
        NavigationPreviewScope scope,
        NavigationPreviewDepth depth);

    public bool Sources { get; }
    public bool Baked { get; }
    public bool Runtime { get; }
    public NavigationPreviewScope Scope { get; }
    public NavigationPreviewDepth Depth { get; }

    public NavigationPreviewState WithSources(bool value);
    public NavigationPreviewState WithBaked(bool value);
    public NavigationPreviewState WithRuntime(bool value);
    public NavigationPreviewState WithScope(NavigationPreviewScope value);
    public NavigationPreviewState WithDepth(NavigationPreviewDepth value);
}

public static class NavigationPreviewApi
{
    public static NavigationPreviewState Current { get; }
    public static event System.Action Changed;
    public static void Apply(NavigationPreviewState state);
}
```

`Current` — side-effect-free snapshot. `Apply` проверяет `null`, записывает общий
EditorPrefs state Overlay/Preferences и синхронно уведомляет `Changed`; владелец обязан
отписаться при unload. Constructor отклоняет неизвестные enum values через
`ArgumentOutOfRangeException`.

### Compatibility facade `NavigationBakeCommand`

Файл: `Editor/NavigationBakeCommand.cs`; namespace `CustomNavigation.Editor`.

```csharp
public static class NavigationBakeCommand
{
    public static NavigationBakeValidationResult Validate(NavigationLevel level);
    public static NavigationBakeResult Execute(NavigationLevel level);
}
```

`NavigationBakeValidationResult` предоставляет `Issues` и `Succeeded`;
`NavigationBakeResult` — `Data`, `Hash`, `PolygonCount`, `SourceMeshCount`, `ByteSize`,
`Asset`, client paths и `ElapsedSeconds`. `Execute` создаёт/обновляет client artifact,
не экспортирует его на server и бросает исключение при `null`/validation/build failure.
API сохранён для совместимости; для новых adapters используйте `NavigationEditorApi`,
который явно моделирует identity ownership и failure result.

## Compatibility rules

В compatible patch/minor update consumer должен считать контрактом:

- public namespace, assembly и signature;
- serialized field names и enum ordinals;
- artifact schema `2`, exact DotRecast version и compatibility identity fields;
- strict HTTP `/path` protocol-v2 field names and writer order;
- fingerprint algorithm version/id;
- callback и owner-thread semantics.

Дополнительные архитектурные варианты описаны в [Extending](extending.md).
