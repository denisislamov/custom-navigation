# DotRecast navigation server

Standalone .NET 9 HTTP-сервис находится рядом с `Assets`. Он не загружает Unity assemblies/scene data, не использует Unity Physics и не строит navmesh при запуске. Сервер читает готовый Detour artifact, экспортированный из Unity Editor.

## Подготовка данных

1. Откройте уровень в Unity.
2. Откройте `Tools > Custom Navigation > Navigation Editor`.
3. Проверьте `NavigationLevel`, sources, agent и performance profile.
4. Нажмите `Build for Client`, затем `Export for Server`.

Один и тот же бинарный navmesh будет записан в:

- `Assets/CustomNavigation/Generated/Navigation` для локального клиента;
- `DotRecastServer/NavigationData` для сервера.

`NavigationData/active.manifest.json` указывает на активный уровень. При старте сервер проверяет schema version, DotRecast version, SHA-256 artifact и число полигонов, затем загружает его через `DtMeshSetReader`.

## Запуск

Из корня Unity-проекта:

```bash
./DotRecastServer/run-server.sh
```

По умолчанию сервис слушает только `127.0.0.1:5079`. Остановить его можно через `Ctrl+C`.

Для клиента на телефоне в той же Wi-Fi сети запустите сервер на всех сетевых интерфейсах:

```bash
./DotRecastServer/run-server.sh --listen 'http://*:5079/'
```

В стартовом уровне Unity введите адрес компьютера, например
`http://192.168.1.10:5079`, и нажмите `Проверить /health`. `127.0.0.1` на телефоне
указывает на сам телефон, а не на компьютер. Если проверка не проходит, разрешите
входящие соединения для `dotnet` в firewall и убедитесь, что Wi-Fi не использует
client/AP isolation.

Другой manifest можно выбрать явно:

```bash
./DotRecastServer/run-server.sh \
  --listen 'http://*:5079/' \
  --manifest DotRecastServer/NavigationData/active.manifest.json
```

## API

- `GET /health` — status, версия DotRecast, `levelId`, описание уровня, artifact hash и число полигонов.
- `POST /path` — авторитетный Detour path query.

Сервер не передаёт и не генерирует presentation geometry. Игровая геометрия сохранена в Unity-сцене, а сервер загружает готовый navmesh artifact, экспортированный из тех же `NavigationGeometrySource`.

Пример hybrid-запроса:

```bash
curl -X POST http://127.0.0.1:5079/path \
  -H 'Content-Type: application/json' \
  -d '{
    "requestId":"manual-1",
    "start":{"x":-11,"y":0,"z":-7},
    "destination":{"x":11,"y":0,"z":7},
    "clientArtifactHash":"e35d4eaaa6febf09e2f39359c2b98290503c4bd4d739e8ae94f74aceceff18d0",
    "clientPathFingerprint":"optional-local-path-sha256"
  }'
```

Для каждого запроса сервер пишет в console:

- входные start/destination и клиентские hashes;
- success, elapsed time, artifact/path hashes и mismatch flag;
- каждую выходную координату маршрута;
- `[WARNING]`, если artifact или local/server path расходятся.

Ответ всегда содержит авторитетные точки, server artifact hash, server path fingerprint и `serverMismatchDetected`. Серверный проект ссылается только на `DotRecast.Core` и `DotRecast.Detour`; Recast bake выполняется заранее в Unity Editor.
