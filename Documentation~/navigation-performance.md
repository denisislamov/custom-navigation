# Navigation Performance: фактическая семантика полей

Контракт перепроверен для документации 0.6.16. Подробные defaults и presets также
приведены в [Configuration](configuration.md#performance-profile).

`NavigationPerformanceProfile` configures the local, owner-thread
`NavigationQueryScheduler`. It does not affect Recast geometry, baked navmesh bytes, or the
reference dedicated server under `Server~`. There is deliberately no Server preset.

## Active and reserved fields

| Serialized field | Classification | Verified read / behavior |
| --- | --- | --- |
| `deviceTier` | Active preset metadata | Inspector preset selection; runtime readiness and budget-warning logs. It is not itself a scheduler limit. |
| `frameBudgetMilliseconds` | Active scheduler | `NavigationQueryScheduler.Tick` stops sliced work and admission when elapsed frame work reaches the budget. `NavigationQuerySchedulerBehaviour` also uses it for warnings. |
| `maximumIterationsPerFrame` | Active scheduler | Caps total completed DotRecast iterations in one `Tick`. |
| `maximumIterationsPerQueryStep` | Active scheduler | Caps one round-robin sliced-query quantum. |
| `maximumNewQueriesPerFrame` | Active scheduler | Caps backlog admission per `Tick`. |
| `maximumConcurrentSlicedQueries` | Active scheduler | Sizes the query workspace pool and caps active queries. |
| `maximumQueuedQueries` | Active scheduler | Caps waiting backlog in `RequestPath`; full backlog rejects or priority-evicts a queued request. |
| `maximumPathPolygons` | Active scheduler | Sizes every polygon-corridor result buffer passed to `FinalizeSlicedFindPath`. |
| `maximumStraightPathPoints` | Active scheduler | Sizes every straight-path result buffer passed to `FindStraightPath`. |
| `queryDeadlineSeconds` | Active scheduler | Checked only while a request is queued, before/while admitting. It does not abort active search. |
| `combatBotMinimumReplanSeconds` | Active consumer | Read by bundled `Samples~/Demos/LocalOnlyBotsNavigationDemo`; not enforced by the scheduler. |
| `visibleBotMinimumReplanSeconds` | Active consumer | Read by the same bundled sample; not enforced by the scheduler. |
| `backgroundBotMinimumReplanSeconds` | Active consumer | Read by the same bundled sample; not enforced by the scheduler. |
| `budgetWarningMultiplier` | Active runtime adapter | `NavigationQuerySchedulerBehaviour.Update` multiplies the frame budget when rate-limited warning logs are enabled. |
| `routeCacheEntries` | Reserved compatibility | No route cache exists in package runtime, server, bundled samples, or audited EFT consumer. |
| `memoryBudgetMegabytes` | Reserved compatibility | No allocator or admission path enforces this value. Actual result buffers use the two maximum-result fields above. |
| `backgroundWorkerCount` | Reserved compatibility | Scheduler verifies owner-thread access and creates no workers. |
| `collectProductionMetrics` | Reserved compatibility | In-memory `NavigationSchedulerMetrics` are always available; no telemetry collector reads this flag. |

The package keeps public getters and exact serialized field names for compatibility. Existing
Mobile Low/Medium/High preset values, including their historical reserved values, are also
preserved. Reserved fields are visible read-only under **Legacy / Diagnostics** rather than
presented as implemented features.

## Consumer and server evidence

- Package runtime reads the scheduler and warning fields listed above.
- `Server~` has no reference to `NavigationPerformanceProfile` or any of its fields. Server
  path limits remain server implementation details, not Mobile preset values.
- The bundled Local Bots sample reads the three replan intervals before submitting a new
  request.
- The audited EFT checkout contains serialized values for every legacy field in its local
  performance assets. Outside its imported copy of the Local Bots sample, no direct getter
  reads were found. Those assets must therefore continue to deserialize without loss.

Audit searches covered property getter names in package `Runtime`, `Editor`, `Samples~`, and
`Server~`, plus getter names and lower-camel serialized YAML fields in the known EFT checkout.
Tooltips were updated only after these reads were classified.

## Queue and result semantics

- **Backlog** is waiting work (`QueuedQueries`); **active** sliced searches have the separate
  concurrent limit (`ActiveQueries`).
- A full backlog rejects a request. A higher-priority incoming request may instead evict the
  worst queued request. Neither case silently expands the queue.
- Cancellation is recorded immediately and delivered by the next owner-thread `Tick`.
- Expiration is the real elapsed wait from `RequestPath` until admission. Editor startup or a
  caller that delays the first `Tick` therefore counts as queue wait. Once admitted, the same
  deadline is not a total-search timeout.
- Polygon corridor and straight-point arrays are allocated from their configured caps. A
  capped corridor can be reported as partial; the scheduler does not allocate unlimited
  output to conceal it.

Changing this profile does not require a geometry bake. Tests retain the existing
payload/hash comparison across performance-profile changes and add ordinary/overloaded
backlog, priority eviction, cancellation, deterministic expiration, result-buffer limits,
preset compatibility, and legacy JSON round-trip coverage.
