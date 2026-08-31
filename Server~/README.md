# DotRecast Navigation Server

После явной установки standalone .NET 9 HTTP-сервис находится в
`<project>/NavigationServer`, рядом с `Assets`. Он не загружает Unity assemblies или
scene data, не использует Unity Physics и не строит navmesh при запуске. Сервер читает
готовый Detour artifact, созданный в Unity Editor.

> Разработчику, который будет **менять** серверный код: см. [`ONBOARDING.md`](ONBOARDING.md)
> рядом — устройство модулей, полный контракт API, ограничения и чеклист граблей.

## Подготовка данных

1. Откройте уровень в Unity и выберите его `NavigationLevel`.
2. Откройте `Tools > DataSakura > Custom Navigation Window`.
3. На вкладке `Overview` нажмите `Validate` и устраните ошибки.
4. На вкладке `Bake` нажмите `Build for Client`.
5. Для удалённого сервера нажмите `Upload to Server`; для локальной общей папки —
   `Export to Folder`.

`Build for Client` записывает клиентскую копию в:

```text
Assets/DataSakura/CustomNavigation/Generated/Navigation
```

`Upload to Server` отправляет те же байты по HTTP. `Export to Folder` записывает их в
`NavigationServer/NavigationData` по умолчанию либо в выбранный `Server Artifact Folder`.

## Как доставить артефакт на сервер

Есть два пути.

**1. `POST /artifacts` (основной).** Кнопка `Upload to Server` в Unity шлёт испечённый
navmesh прямо на адрес из `NavigationServerSettings`. Работает, когда сервер на другой
машине или в контейнере — общая файловая система не нужна. Сервер проверяет schema,
версию DotRecast, SHA-256 и число полигонов **до** записи, поэтому битая выгрузка не
может оставить полуживую карту. Имя файла берётся из манифеста и принимается, только
если это простое `<level>.navigation.bytes` или legacy `<level>.<hash>.navmesh.bytes` —
выйти за пределы папки данных нельзя.

Загрузка разрешена без токена, только если сервер слушает loopback. Как только он
доступен из сети, нужен `--upload-token`:

```bash
./NavigationServer/run-server.sh --listen 'http://*:5079/' --upload-token 'секрет'
```

Тот же секрет вводится в `Settings` → `Artifact upload` → `Upload token`. Он хранится
в EditorPrefs, а не в ассете, поэтому не попадает в билд игры.

**2. Запись в папку.** Кнопка `Export to Folder` кладёт файлы в
`NavigationServer/NavigationData`. Годится только для сервера на этой же машине.

## Какую карту сервер отдаёт на запрос

Сервер держит **все** экспортированные карты из своей папки `NavigationData` и выбирает
нужную для каждого запроса:

1. Если в теле `POST /path` задан `levelId` — берётся карта с таким `levelId` из манифеста.
   Для legacy-набора с несколькими hash-based экспортами одного уровня выбирается самый свежий
   по времени записи. Текущий формат хранит один стабильный файл уровня в каждой папке сборки.
2. Если `levelId` не задан — берётся `active.manifest.json`. Его обновляет каждый
   `Upload to Server` или `Export to Folder`, поэтому «активной» становится последняя
   доставленная карта.
   Это поведение по умолчанию для игры с одним уровнем.
3. Если `active.manifest.json` нет, но экспортирована ровно одна карта — берётся она.

Карты загружаются лениво и кэшируются. Кэш сбрасывается по времени изменения манифеста,
поэтому после повторного `Upload to Server` или `Export to Folder` сервер подхватывает
новые данные **без перезапуска**. При загрузке проверяются schema version, DotRecast
version, SHA-256 artifact и число полигонов.

Текущая пара называется `<level>.navigation.bytes` +
`<level>.navigation.manifest.json`. Если нужно сохранить несколько экспортов одного уровня,
используйте отдельные понятные папки сборок и одинаковые имена внутри; отдельный address catalog
для этого не нужен.

Отсутствие артефактов — не ошибка. Сервер стартует, слушает порт, возвращает
`status: "no-artifact"` через `GET /health`, а `POST /path` сообщает, что карта ещё не
загружена. Поэтому сервер можно поднять до первого upload/export из Unity.

## Запуск

### Из Unity Editor

1. Выберите `NavigationLevel` и откройте
   `Tools > DataSakura > Custom Navigation Window` → `Settings`.
2. Если settings asset отсутствует, нажмите `Create Navigation Server Settings`.
3. В секции `Local server` нажмите `Install navigation server`.
4. Нажмите `Start server`.
5. В секции `Connection check` нажмите `Check /health`.

Первый запуск выполняет restore/build .NET-проекта и может занять несколько секунд.
Процесс пишет stdout/stderr в Unity Console и останавливается кнопкой `Stop server` или
при выходе из Unity.

### Из терминала

После `Install navigation server` выполните из корня Unity-проекта:

```bash
./NavigationServer/run-server.sh
```

По умолчанию сервис слушает только `127.0.0.1:5079`. Остановить его можно через `Ctrl+C`.

Для клиента на телефоне в той же Wi-Fi сети запустите сервер на всех сетевых интерфейсах:

```bash
./NavigationServer/run-server.sh --listen 'http://*:5079/'
```

В Unity выберите `NavigationLevel`, откройте `DS Navigation` → `Settings`, в секции
`Navigation server` укажите адрес компьютера, например `http://192.168.1.10:5079`,
нажмите `Apply`, затем в секции `Connection check` — `Check /health`. `127.0.0.1` на
телефоне указывает на сам телефон, а не на компьютер. Если проверка не проходит,
разрешите входящие соединения для `dotnet` в firewall и убедитесь, что Wi-Fi не
использует client/AP isolation.

Аргументы:

| Аргумент | Значение |
|---|---|
| `--listen <prefix>` | HTTP prefix, например `http://*:5079/`. По умолчанию `http://127.0.0.1:5079/`. |
| `--data <folder>` | Папка с артефактами. По умолчанию `NavigationData` рядом с проектом сервера. |
| `--manifest <path>` | Жёстко закрепить одну карту: она становится ответом по умолчанию вместо `active.manifest.json`. Полезно для выделенного инстанса на конкретный уровень. |
| `--upload-token <secret>` | Требовать заголовок `X-Navigation-Token` для `POST /artifacts`. Обязателен, если сервер слушает не loopback. |

## API

- `GET /health` — status, версия DotRecast, `levelId`, описание уровня, artifact hash,
  число полигонов, папка данных и список доступных уровней (`availableLevels`).
  Можно спросить про конкретную карту: `GET /health?level=<levelId>`.
- `GET /artifacts` — все карты в папке данных с их состоянием.
- `POST /artifacts` — загрузить карту: `{ "manifestJson": "...", "dataBase64": "...", "setActive": true }`.
- `POST /path` — авторитетный Detour path query.

Сервер не передаёт и не генерирует presentation geometry. Игровая геометрия сохранена в Unity-сцене, а сервер загружает готовый navmesh artifact, экспортированный из тех же `NavigationGeometrySource`.

Пример hybrid-запроса:

```bash
curl -X POST http://127.0.0.1:5079/path \
  -H 'Content-Type: application/json' \
  -d '{
    "requestId":"manual-1",
    "levelId":"local_bots_arena",
    "start":{"x":-11,"y":0,"z":-7},
    "destination":{"x":11,"y":0,"z":7},
    "clientArtifactHash":"e35d4eaaa6febf09e2f39359c2b98290503c4bd4d739e8ae94f74aceceff18d0",
    "clientPathFingerprint":"optional-local-path-sha256"
  }'
```

`levelId` можно не указывать — тогда ответит активная карта.

Для каждого запроса сервер пишет в console:

- входные start/destination и клиентские hashes;
- success, elapsed time, artifact/path hashes и mismatch flag;
- каждую выходную координату маршрута;
- `[WARNING]`, если artifact или local/server path расходятся.

Ответ всегда содержит авторитетные точки, server artifact hash, server path fingerprint и `serverMismatchDetected`. Серверный проект ссылается только на `DotRecast.Core` и `DotRecast.Detour`; Recast bake выполняется заранее в Unity Editor.
