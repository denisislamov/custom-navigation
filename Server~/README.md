# DotRecast navigation server

Standalone .NET 9 HTTP-сервис находится рядом с `Assets`. Он не загружает Unity assemblies/scene data, не использует Unity Physics и не строит navmesh при запуске. Сервер читает готовый Detour artifact, экспортированный из Unity Editor.

## Подготовка данных

1. Откройте уровень в Unity.
2. Откройте `Tools > Custom Navigation > Navigation Editor`.
3. Проверьте `NavigationLevel`, sources, agent и performance profile.
4. Нажмите `Build for Client`, затем `Export for Server`.

Один и тот же бинарный navmesh будет записан в:

- `Assets/CustomNavigation/Generated/Navigation` для локального клиента;
- `NavigationServer/NavigationData` для сервера.

## Какую карту сервер отдаёт на запрос

Сервер держит **все** экспортированные карты из своей папки `NavigationData` и выбирает
нужную для каждого запроса:

1. Если в теле `POST /path` задан `levelId` — берётся карта с таким `levelId` из манифеста.
   Если для одного уровня накопилось несколько экспортов (`<level>.<hash>.manifest.json`),
   выбирается самый свежий по времени записи.
2. Если `levelId` не задан — берётся `active.manifest.json`. Его перезаписывает каждый
   `Export for Server`, поэтому «активной» становится последняя выгруженная карта.
   Это поведение по умолчанию для игры с одним уровнем.
3. Если `active.manifest.json` нет, но экспортирована ровно одна карта — берётся она.

Карты загружаются лениво и кэшируются. Кэш сбрасывается по времени изменения манифеста,
поэтому после повторного `Export for Server` сервер подхватывает новые данные **без
перезапуска**. При загрузке проверяются schema version, DotRecast version, SHA-256
artifact и число полигонов.

Отсутствие артефактов — не ошибка: сервер стартует, слушает порт и сообщает об этом в
`GET /health` (`status: "no-artifact"`) и в ответе `POST /path`. Это нужно, чтобы можно
было поднять сервер до первого экспорта из Unity.

## Запуск

Из корня Unity-проекта:

```bash
./NavigationServer/run-server.sh
```

По умолчанию сервис слушает только `127.0.0.1:5079`. Остановить его можно через `Ctrl+C`.

Для клиента на телефоне в той же Wi-Fi сети запустите сервер на всех сетевых интерфейсах:

```bash
./NavigationServer/run-server.sh --listen 'http://*:5079/'
```

В стартовом уровне Unity введите адрес компьютера, например
`http://192.168.1.10:5079`, и нажмите `Проверить /health`. `127.0.0.1` на телефоне
указывает на сам телефон, а не на компьютер. Если проверка не проходит, разрешите
входящие соединения для `dotnet` в firewall и убедитесь, что Wi-Fi не использует
client/AP isolation.

Аргументы:

| Аргумент | Значение |
|---|---|
| `--listen <prefix>` | HTTP prefix, например `http://*:5079/`. По умолчанию `http://127.0.0.1:5079/`. |
| `--data <folder>` | Папка с артефактами. По умолчанию `NavigationData` рядом с проектом сервера. |
| `--manifest <path>` | Жёстко закрепить одну карту: она становится ответом по умолчанию вместо `active.manifest.json`. Полезно для выделенного инстанса на конкретный уровень. |

## API

- `GET /health` — status, версия DotRecast, `levelId`, описание уровня, artifact hash,
  число полигонов, папка данных и список доступных уровней (`availableLevels`).
  Можно спросить про конкретную карту: `GET /health?level=<levelId>`.
- `GET /artifacts` — все карты в папке данных с их состоянием.
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
