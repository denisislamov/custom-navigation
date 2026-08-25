# CustomNavigation Server — Onboarding для backend/.NET-разработчика

> Документ составлен по исходникам `Server~` и Unity-стороны интеграции
> (`Editor/NavigationServer*`, `Runtime/NavigationServer*`).
> Пользовательская документация сервера — [`README.md`](README.md) рядом,
> документация пакета — `../README.md`.
> Цель: за 1–2 часа дать полную картину сервера — что он делает, как устроен,
> где грабли.

---

## 0. TL;DR — что это вообще такое

**Standalone .NET 9 HTTP-сервис**, который авторитетно считает пути по navmesh,
испечённому в Unity Editor. Ни Unity-ассембли, ни Unity Physics, ни runtime-бейка:
сервер читает готовый бинарный Detour-артефакт и отвечает на запросы.

| Свойство | Реализация |
|---|---|
| **Рантайм** | .NET 9, `System.Net.HttpListener` (не ASP.NET, не Kestrel), 0 NuGet-зависимостей |
| **Зависимости** | Только `DotRecast.Core.dll` + `DotRecast.Detour.dll` (локальные DLL в `lib/`). `Recast` (бейк) сюда не нужен |
| **Формат данных** | `<level>.<hash>.navmesh.bytes` + `<level>.<hash>.manifest.json`, испечённые Unity |
| **Детерминизм** | Тот же бинарный navmesh, что у клиента + SHA-256 отпечаток пути (`NavigationPathFingerprint`) |
| **Мультиуровневость** | Держит все карты из папки данных, выбирает по `levelId` или по `active.manifest.json` |
| **Hot reload** | Ленивая загрузка + кеш по mtime манифеста → перезапуск не нужен |
| **Строгость сборки** | `TreatWarningsAsErrors=true`, `Nullable=enable`, `ImplicitUsings=enable` |

Сервер — **опциональный** режим: клиент умеет считать пути локально
(`NavigationComputeMode.LocalOnly`). Сервер нужен для `ServerOnly` и `ServerPredicted`.

---

## 1. Карта серверного кода

```
Packages/com.datasakura.custom-navigation/Server~/     ← ИСХОДНИКИ (тильда = Unity игнорирует)
├── Program.cs                       409 стр — точка входа, HttpListener-луп, роутинг
├── Contracts.cs                      92 стр — все DTO (record'ы) запросов/ответов
├── Navigation/
│   ├── ServerNavigation.cs          211 стр — DtNavMeshQuery + FindPath + fingerprint
│   ├── NavigationRegistry.cs        239 стр — выбор карты, кеш, hot reload
│   ├── NavigationArtifactStore.cs   433 стр — загрузка/валидация/сохранение артефактов
│   └── NavigationUploadPolicy.cs    113 стр — авторизация POST /artifacts
├── lib/                             — DotRecast.Core/Detour/Recast .dll
├── DotRecastServer.csproj           — net9.0, Exe
├── run-server.sh                    — dotnet run --configuration Release -- "$@"
├── README.md                        — пользовательская документация
└── DOTRECAST_LICENSE.txt            — zlib

<ProjectRoot>/NavigationServer/                        ← УСТАНОВЛЕННАЯ КОПИЯ (в .gitignore)
├── (копия Server~ без bin/obj)
└── NavigationData/                  ← «диск» сервера: артефакты + active.manifest.json
```

> ⚠️ **`Server~` не компилируется Unity.** Тильда прячет папку от AssetDatabase —
> иначе редактор попытался бы собрать `Program.cs` своим компилятором и упал бы.
> Правите вы **исходники в `Server~`**, а запускается **копия в `NavigationServer/`**.
> После правок нужно заново нажать **Server → Install (overwrite)** или скопировать руками,
> иначе будете отлаживать старый код. Это самая частая потеря времени.

---

## 2. Жизненный цикл процесса

```
run-server.sh / dotnet run
        │
        ├─ ResolveDataDirectory(args)        --data, иначе папка --manifest, иначе ../../../NavigationData
        ├─ ResolvePinnedManifestPath(args)   --manifest
        ├─ new NavigationRegistry(...)       ленивый, ничего не грузит
        ├─ ResolveListenPrefix(args)         --listen, иначе http://127.0.0.1:5079/
        ├─ NavigationUploadPolicy.Resolve()  --upload-token + loopback?
        ├─ listener.Start()
        ├─ TryResolve(null) → лог [ready] или [waiting]   ← отсутствие артефакта НЕ фатально
        └─ while (!cancelled) { GetContextAsync(); await HandleRequest(...); }
```

**Ключевое архитектурное решение:** сервер стартует на **пустой** `NavigationData`.
Раньше он падал с `FileNotFoundException`, и получался тупик: экспортировать из Unity
нельзя, пока сервер не поднят (upload), а поднять сервер нельзя без артефакта.
Теперь артефакт резолвится **лениво, на каждый запрос**, а состояние без карты
отдаётся как `status: "no-artifact"` в `/health` и `success: false` в `/path`.

**Конкурентность:** цикл `await listener.GetContextAsync(); await HandleRequest(...)`
обрабатывает запросы **последовательно, по одному**. Плюс сам `FindPath` держит
`lock(_queryLock)` (DtNavMeshQuery не потокобезопасен). Для дев-инструмента это ок,
для продакшена — узкое место (см. §9).

**Shutdown:** `Console.CancelKeyPress` → `cancel = true` + `listener.Stop()`,
`HttpListenerException`/`ObjectDisposedException` при отмене глотаются.

### Аргументы командной строки

| Аргумент | Значение | Дефолт |
|---|---|---|
| `--listen <prefix>` | HTTP-префикс `HttpListener`. Только `http://` (https отвергается с исключением). Слэш в конце добавляется автоматически | `http://127.0.0.1:5079/` |
| `--data <folder>` | Папка с артефактами | папка `--manifest`, иначе `<bin>/../../../NavigationData` |
| `--manifest <path>` | Пришпилить одну карту как дефолтную вместо `active.manifest.json` | нет |
| `--upload-token <secret>` | Требовать заголовок `X-Navigation-Token` на `POST /artifacts` | нет |

Отсутствие значения после флага → `ArgumentException` и падение на старте (осознанно:
лучше упасть, чем тихо слушать не тот адрес).

> Дефолтный путь к данным `AppContext.BaseDirectory + "../../../NavigationData"` —
> это `bin/Release/net9.0/` → на три уровня вверх. Работает только при запуске
> через `dotnet run` из установленной папки. Для `dotnet publish` / контейнера
> **обязательно передавайте `--data` явно**.

---

## 3. HTTP API

Все ответы — `application/json; charset=utf-8`, camelCase
(`JsonNamingPolicy.CamelCase`, `PropertyNameCaseInsensitive = true` на чтении).
CORS-заголовки (`Allow-Origin: *`, `Allow-Headers: Content-Type`,
`Allow-Methods: GET,POST,OPTIONS`) ставятся **на каждый** ответ.

| Метод | Путь | Коды | Описание |
|---|---|---|---|
| `OPTIONS` | `*` | 204 | CORS preflight |
| `GET` | `/health[?level=<id>]` | 200 | Состояние сервера и загруженной карты |
| `GET` | `/artifacts` | 200 | Все карты в папке данных с их состоянием |
| `POST` | `/artifacts` | 200 / 400 / 403 | Загрузка испечённой карты |
| `POST` | `/path` | 200 / 400 | Авторитетный расчёт пути |
| — | прочее | 404 | `{ "error": "Endpoint not found." }` |
| — | исключение | 500 | `{ "error": "Internal server error." }`, stack trace — в stderr |

### 3.1 `GET /health`

```jsonc
{
  "status": "ok",                  // либо "no-artifact"
  "dotRecastVersion": "2026.1.3",  // ЗАХАРДКОЖЕНА строкой в Program.cs
  "navigationPolygons": 1234,
  "levelId": "local_bots_arena",
  "description": "...",
  "artifactHash": "e35d4e...",
  "message": "",                   // причина, если status != "ok"
  "dataDirectory": "/abs/path/NavigationData",
  "availableLevels": ["a", "b"]
}
```

`?level=<id>` спрашивает про конкретную карту, без параметра — про активную.
Отвечает **200 даже при `no-artifact`** — это состояние, а не ошибка.

### 3.2 `GET /artifacts`

```jsonc
{
  "loadedLevelId": "...", "loadedArtifactHash": "...", "dataDirectory": "...",
  "artifacts": [{
    "levelId": "...", "description": "...", "artifactHash": "...",
    "schemaVersion": "1", "dotRecastVersion": "2026.1.3", "agentProfileId": "...",
    "polygonCount": 0, "sourceMeshCount": 0, "fileName": "lvl.hash.navmesh.bytes",
    "dataPresent": true,      // .navmesh.bytes лежит рядом
    "hashMatchesData": true,  // SHA-256 файла == artifactHash манифеста
    "isActive": true,         // хеш совпал с active.manifest.json
    "isLoaded": true,         // эта карта сейчас в памяти
    "error": ""               // непустой → манифест битый, остальные поля пустые
  }]
}
```

Нужен вкладке **Artifacts** в Unity, чтобы показать разницу между клиентскими и
серверными картами без доступа к ФС сервера. **Считает SHA-256 каждого файла на
каждый вызов** — на большой папке это не бесплатно, не дёргайте в цикле.

### 3.3 `POST /artifacts`

```jsonc
// запрос
{ "manifestJson": "{...}", "dataBase64": "...", "setActive": true }
// ответ
{ "success": true, "levelId": "...", "artifactHash": "...",
  "fileName": "lvl.hash.navmesh.bytes", "setActive": true, "message": "Uploaded and marked active." }
```

Порядок проверок в `NavigationArtifactStore.Save` — **всё до записи на диск**:

1. `manifestJson` и `dataBase64` непустые;
2. манифест парсится и содержит `fileName`;
3. **защита от path traversal:** `Path.GetFileName(fileName) == fileName` и суффикс
   `.navmesh.bytes` — «простое» имя, выйти из папки данных нельзя;
4. base64 декодируется;
5. `Create(...)`: schema `1`, DotRecast `2026.1.3`, SHA-256 совпадает с манифестом,
   navmesh реально парсится `DtMeshSetReader`, `polygonCount` совпадает.

Только после этого пишутся `.navmesh.bytes` → манифест → (если `setActive`)
`active.manifest.json`. **Битая выгрузка не может оставить полуживую карту.**
Явного `reload` нет и не нужно: реестр заметит новый mtime манифеста сам.

> Манифест сохраняется **байт-в-байт как прислал Unity** (`TrimEnd() + "\n"`,
> UTF-8 без BOM) — иначе поехали бы хеши.

### 3.4 `POST /path`

```jsonc
// запрос
{ "requestId": "bot-17", "levelId": "",        // "" = активная карта
  "start": {"x":-11,"y":0,"z":-7}, "destination": {"x":11,"y":0,"z":7},
  "clientArtifactHash": "e35d4e...",           // опционально
  "clientPathFingerprint": "9ab1..." }         // опционально
// ответ
{ "success": true, "points": [{"x":..,"y":..,"z":..}],
  "message": "DotRecast returned 5 straight path points.",
  "requestId": "bot-17", "artifactHash": "e35d4e...", "pathFingerprint": "9ab1...",
  "serverMismatchDetected": false }
```

> ⚠️ **`/path` возвращает 200 даже при неудаче** (нет карты, точка вне navmesh,
> коридор не найден). Ошибка живёт в `success=false` + `message`. Сделано намеренно:
> Unity-клиент показывает `message` только при успешном HTTP-обмене, а именно этот
> текст и содержит действие («сначала экспортируйте из Unity»). 400 отдаётся только
> на битый JSON и на отсутствие `start`/`destination`.

`requestId` необязателен: если пустой — подставляется `Interlocked.Increment` счётчик.

Каждый запрос **подробно логируется в stdout**: вход, время, success, число точек,
хеши, mismatch и **каждая точка маршрута отдельной строкой**. Отлично для отладки,
но на нагрузке консоль становится узким местом (см. §9).

---

## 4. `ServerNavigation` — собственно расчёт пути

```csharp
searchExtents        = (2, 4, 2)
MaxPathPolygons      = 256
MaxStraightPathPoints= 256
filter               = DtQueryDefaultFilter     // без area costs и flags!
straightPathOptions  = DT_STRAIGHTPATH_ALL_CROSSINGS
```

Пайплайн: `FindNearestPoly(start)` + `FindNearestPoly(destination)` →
`FindPath` (коридор полигонов) → `FindStraightPath` (точки) → fingerprint → ответ.

Ошибки → `success=false` с внятным `message`:
- нефинитные координаты → `"Coordinates must be finite numbers."`;
- `startRef == 0 || endRef == 0` → `"Start or destination is outside the navigation mesh."`;
- `pathStatus.Failed()` → `"DotRecast could not find a polygon corridor."`;
- `straightStatus.Failed()` → `"DotRecast could not create a straight path."`.

`pathStatus.IsPartial()` **не** делает ответ неуспешным — просто пишется
`"DotRecast returned a partial path."` в `message`. Клиент обязан это учитывать.

### Детект рассинхронизации

| Проверка | Условие |
|---|---|
| `artifactMismatch` | клиент прислал `clientArtifactHash` и он ≠ серверному (OrdinalIgnoreCase) |
| `pathMismatch` | клиент прислал `clientPathFingerprint` и он ≠ вычисленному сервером |

Любое из двух → `serverMismatchDetected: true` + `[WARNING]` в **stderr**.
Пустые клиентские поля проверку пропускают — старые клиенты работают как раньше.

### Fingerprint

`NavigationPathFingerprint.Compute` — квантование каждой координаты
`(long)Math.Round(v * 1000, MidpointRounding.AwayFromZero)` (то есть до миллиметра),
склейка в `"x,y,z;"`, UTF-8, SHA-256, hex в нижнем регистре.

> ⚠️ **Этот код продублирован** в `Runtime/NavigationPathFingerprint.cs` (Unity).
> Любая правка (квантование, разделители, режим округления) должна вноситься
> в **обе** копии одновременно, иначе клиент и сервер начнут вечно расходиться.

> ⚠️ **Ловушка детерминизма №2.** Серверные `searchExtents (2,4,2)` и лимиты 256/256
> **захардкожены** и не совпадают с клиентскими (`radius*4, height*2, radius*4` и
> `MaximumPathPolygons`/`MaximumStraightPathPoints` из `PerformanceProfile`).
> На больших или узких картах это даёт легальные расхождения путей и ложные
> `serverMismatchDetected`. Если используете `ServerPredicted` — согласуйте параметры.
> Плюс сервер использует `DtQueryDefaultFilter`, то есть **игнорирует area costs и
> flags**, которые клиент может учитывать.

---

## 5. `NavigationRegistry` — какую карту отдавать

Резолв манифеста (`TryResolve(levelId)`):

**Без `levelId`** (пусто/null):
1. `--manifest` (pinned), если файл существует;
2. `active.manifest.json`;
3. если в папке ровно **один** манифест — он;
4. иначе — ошибка с подсказкой и списком доступных уровней.

**С `levelId`:**
1. `active.manifest.json`, если его `levelId` совпадает (чтобы явный `levelId` давал
   ровно то же, что и запрос без него);
2. иначе — среди `*.manifest.json` с таким `levelId` **самый свежий по mtime**
   (после нескольких экспортов одного уровня рядом лежат несколько хешей);
3. иначе — `"Level 'x' is not on the server. Available levels: ..."`.

`active.manifest.json` при перечислении всегда исключается, чтобы активная карта
не задваивалась в `availableLevels`.

**Кеш и hot reload:** `Dictionary<manifestPath, (ServerNavigation, LastWriteTimeUtc)>`
под `lock`. Совпал mtime — отдаём из памяти; изменился — перечитываем navmesh.
Перезапуск сервера после нового экспорта **не нужен**.

> ⚠️ Инвалидация только по **манифесту**. Если подменить `.navmesh.bytes`, не тронув
> манифест, сервер продолжит отдавать старую карту из кеша. Всегда пишите оба файла
> (Unity так и делает).
>
> ⚠️ Записи кеша **никогда не вытесняются**. 50 уровней → 50 navmesh в памяти навсегда.

---

## 6. `NavigationUploadPolicy` — безопасность выгрузки

Загрузка перезаписывает карты, по которым ходят все клиенты, поэтому она не может
быть открыта всей сети.

| Ситуация | Поведение |
|---|---|
| Задан `--upload-token` | Требуется заголовок `X-Navigation-Token`, сравнение `StringComparison.Ordinal` |
| Токена нет, слушаем loopback (`127.0.0.1`, `localhost`, `::1`) | Разрешено |
| Токена нет, слушаем `*` / `+` / реальный интерфейс | **Запрещено**, 403 с объяснением |

То есть `--listen 'http://*:5079/'` без токена = аплоад выключен. Разумный дефолт:
лучше отказать, чем тихо оставить открытым. Итоговый режим печатается на старте
строкой `[upload] ...`.

> ⚠️ Сравнение токена **не constant-time** (`string.Equals`) — теоретически уязвимо
> к timing-атаке. И весь трафик идёт по **HTTP без TLS**: токен ходит открытым текстом.
> Для внешней сети ставьте перед сервером reverse-proxy с TLS.

---

## 7. Формат артефакта (контракт с Unity)

`<levelId>.<первые 12 символов хеша>.navmesh.bytes` + одноимённый `.manifest.json`:

```jsonc
{
  "schemaVersion": "1",
  "dotRecastVersion": "2026.1.3",
  "levelId": "local_bots_arena",
  "description": "...",
  "artifactHash": "<SHA-256 всего .navmesh.bytes, hex lower>",
  "agentProfileId": "...",
  "polygonCount": 1234,
  "sourceMeshCount": 17,
  "fileName": "local_bots_arena.e35d4eaaa6fe.navmesh.bytes"
}
```

Бинарник — стандартный Detour mesh set (`DtMeshSetWriter` в Unity ↔ `DtMeshSetReader`
на сервере). `active.manifest.json` — **точная копия** манифеста активной карты.

> ⚠️ **Версии зашиты в трёх местах:** `NavigationArtifactStore` (сервер),
> `NavigationArtifactLoader` и `NavigationArtifactBuilder` (Unity). Обновляете
> DotRecast — правьте все три, плюс строку `"2026.1.3"` в `Program.cs`
> (`/health` берёт её оттуда, а не из `NavigationArtifactStore`, — рассинхрон
> возможен) и DLL в `Server~/lib/` **и** `Runtime/DotRecast/`.

---

## 8. Интеграция с Unity

### Кто и как обращается к серверу

| Клиент | Файл | Что делает |
|---|---|---|
| Рантайм-игра | `Runtime/NavigationServerPathClient.cs` | `POST /path` корутиной, таймаут из настроек, сверяет fingerprint |
| Редактор | `Editor/NavigationServerEditorClient.cs` | `Get`/`Post` через `EditorApplication.update` (корутин в редакторе нет), abort на `beforeAssemblyReload` |
| Выгрузка | `Editor/NavigationServerUploader.cs` | `POST /artifacts` с base64, токен из `EditorPrefs` |
| Установка/запуск | `Editor/NavigationServerInstaller.cs` | Копирует `Server~` → `NavigationServer/`, стартует `dotnet run`, PID в `SessionState` |

### Настройки

`NavigationServerSettings` (ScriptableObject, грузится через
`Resources.Load("CustomNavigation/NavigationServerSettings")`; в потребляющем проекте
создаётся как `Assets/Resources/CustomNavigation/NavigationServerSettings.asset`,
в этом репозитории лежит в `Assets/DataSakura/CustomNavigation/Resources/CustomNavigation/`):

| Поле | Дефолт | Назначение |
|---|---|---|
| `host` | `127.0.0.1` | |
| `port` | `5079` | clamp 1..65535 |
| `useHttps` | `false` | **сервер https не поддерживает** — флаг заведён на будущее |
| `requestTimeoutSeconds` | `5` | clamp 1..60 |
| `serverArtifactFolder` | `NavigationServer/NavigationData` | куда пишет `Export to Folder` |

`BaseUrl` → адрес для клиента, `ListenPrefix` → аргумент `--listen`
(`0.0.0.0` превращается в `*`). В рантайме адрес можно переопределить через
`PlayerPrefs` (`CustomNavigation.ServerUrl` + `...Baseline`, чтобы устаревший
override сбрасывался при смене адреса в ассете).

Токен выгрузки — **только `EditorPrefs`**
(`CustomNavigation.UploadToken.<hash(dataPath)>`), никогда не в ассете: чтобы не утёк в билд.

### Установка и запуск из редактора

`Server → Install` копирует `Server~` → `<ProjectRoot>/NavigationServer/`, **пропуская**
`bin`, `obj` и `NavigationData` (существующие артефакты переживают переустановку),
и делает `chmod +x run-server.sh` на не-Windows. `Start` запускает
`dotnet run --project DotRecastServer.csproj --configuration Release -- --listen ... --data ...`
и перекидывает stdout/stderr в Unity Console. PID лежит в `SessionState` —
переживает перезагрузку домена, но **не** перезапуск редактора (после него процесс
придётся убивать руками).

---

## 9. Ограничения и техдолг сервера

| Область | Состояние | Что стоит сделать |
|---|---|---|
| **Пропускная способность** | Запросы обрабатываются строго по одному + `lock` на `DtNavMeshQuery` | Пул `DtNavMeshQuery` на карту, `_ = HandleRequest(...)` без ожидания или переход на Kestrel/minimal API |
| **Логирование** | `Console.WriteLine` на каждую точку маршрута, без уровней и без буферизации | Нормальный логгер + уровни; выключать per-point лог по умолчанию |
| **Тесты** | Нет ни одного | Round-trip артефакта, path-traversal, parity fingerprint с Unity |
| **Кеш карт** | Без вытеснения и без лимита памяти | LRU или явный unload |
| **TLS** | Нет, только `http://` (`https` явно отвергается) | Reverse-proxy или Kestrel |
| **Токен** | Сравнение не constant-time | `CryptographicOperations.FixedTimeEquals` |
| **Параметры запроса** | `searchExtents`/лимиты захардкожены и расходятся с клиентом | Брать из манифеста/agent profile |
| **Фильтр** | `DtQueryDefaultFilter` — area costs и flags игнорируются | Прокинуть area costs из `AreaCatalog` |
| **Метрики/graceful shutdown** | Нет | `/metrics`, дренаж соединений |
| **Публикация** | Нет `dotnet publish`/Dockerfile, дефолтный путь данных завязан на `bin/Release/net9.0/..` | Dockerfile + обязательный `--data` |
| **Rate limiting** | Нет | Ограничение на `/path` и `/artifacts` |

---

## 10. Типовые сценарии

### Локальный запуск
```bash
./NavigationServer/run-server.sh
# слушает http://127.0.0.1:5079/, аплоад разрешён (loopback)
```

### Доступ с телефона в той же Wi-Fi
```bash
./NavigationServer/run-server.sh --listen 'http://*:5079/' --upload-token 'секрет'
```
В Unity: тот же секрет в поле `Upload token` вкладки **Server**, адрес — IP компьютера
(`http://192.168.1.10:5079`). `127.0.0.1` на телефоне указывает на сам телефон.
Не проходит `/health` — проверьте firewall для `dotnet` и AP-isolation на роутере.

### Выделенный инстанс на одну карту
```bash
dotnet run --project NavigationServer/DotRecastServer.csproj -c Release -- \
  --data /srv/nav --manifest /srv/nav/arena.e35d4eaaa6fe.manifest.json --listen 'http://*:5079/'
```

### Ручная проверка
```bash
curl -s http://127.0.0.1:5079/health | jq
curl -s http://127.0.0.1:5079/artifacts | jq '.artifacts[] | {levelId, isActive, hashMatchesData}'
curl -s -X POST http://127.0.0.1:5079/path -H 'Content-Type: application/json' -d '{
  "requestId":"manual-1", "levelId":"local_bots_arena",
  "start":{"x":-11,"y":0,"z":-7}, "destination":{"x":11,"y":0,"z":7}
}' | jq
```

### Правка серверного кода
1. Правим **`Packages/com.datasakura.custom-navigation/Server~/...`** (источник истины).
2. `Server → Install` с перезаписью (или `rsync` в `NavigationServer/`).
3. `dotnet build` — помните про `TreatWarningsAsErrors`: любое предупреждение = провал сборки.
4. Меняли DTO/fingerprint/версии — синхронно правьте Unity-сторону (§4, §7).

---

## 11. Чеклист неочевидного

1. **`Server~` ≠ `NavigationServer/`.** Правки без переустановки не применяются.
2. **`/path` и `/health` отвечают 200 при неудаче** — смотрите на `success`/`status`, а не на HTTP-код.
3. **Версия DotRecast в `/health` захардкожена в `Program.cs`** отдельно от `NavigationArtifactStore`.
4. **Fingerprint и константы версий дублируются с Unity** — правьте синхронно.
5. **`searchExtents` и лимиты 256/256 расходятся с клиентом** → ложные mismatch.
6. **`DtQueryDefaultFilter`** — сервер не знает про area costs и flags.
7. **Partial path — это `success: true`.** Проверяйте `message`.
8. **Кеш инвалидируется по mtime манифеста**, не по `.navmesh.bytes`.
9. **Кеш карт не вытесняется.**
10. **Аплоад без токена работает только на loopback**; на `*` — 403.
11. **Токен сравнивается не constant-time и ходит по HTTP** без TLS.
12. **Path traversal закрыт** проверкой `Path.GetFileName` + суффикса `.navmesh.bytes`.
13. **Манифест пишется байт-в-байт** как прислал Unity — не «улучшайте» форматирование.
14. **Дефолтный `--data` завязан на `bin/Release/net9.0/../../..`** — в publish/Docker задавайте явно.
15. **Запросы обрабатываются последовательно** — не нагрузочный сервер.
16. **`TreatWarningsAsErrors=true`** — предупреждение ломает сборку.
17. **`useHttps` в Unity-настройках ничего не даст**: `--listen https://...` отвергается исключением.

---

## 12. С чего начать чтение кода

1. `Server~/Contracts.cs` — все DTO разом, 5 минут, даёт словарь понятий.
2. `Server~/Program.cs` — роутинг и жизненный цикл, читать целиком.
3. `Server~/Navigation/NavigationRegistry.cs` — как выбирается карта и работает hot reload.
4. `Server~/Navigation/ServerNavigation.cs` — сам расчёт пути + fingerprint.
5. `Server~/Navigation/NavigationArtifactStore.cs` — формат, валидация, аплоад.
6. `Server~/Navigation/NavigationUploadPolicy.cs` — 113 строк, безопасность.
7. `Runtime/NavigationServerPathClient.cs` — вторая половина контракта, со стороны клиента.
8. `Editor/NavigationServerInstaller.cs` — установка и управление процессом.
