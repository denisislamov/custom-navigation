# Обновление, миграция и откат

Эта процедура сохраняет project-owned сцены, profiles, Unity GUID и навигационные
project-owned данные при переходе на DataSakura Custom Navigation `0.7.0`. Навигационные
артефакты schema 1 намеренно не сохраняются как исполняемые: после backup их нужно re-bake.

## Breaking migration 0.6.x → 0.7.0

### 1. Сначала установите canonical Jitter отдельно

До обновления Custom Navigation установите approved f32 release
`jitter-v2.8.9-datasakura.1-rc.1` из Jitter Physics Baker repository. Требуются exact package
commit `508de73d6d82088d58a74fd41d7e09b70f009b1d` и `Jitter2.Core.dll` SHA-256
`944666bbe73dfce5ffc5bfb18569fb0004f50e767dcbb8b471dde15242023ca6`.

Jitter остаётся project-owned prerequisite. Не добавляйте Jitter Physics Baker как runtime
provider, не копируйте Jitter внутрь Custom Navigation и не оставляйте две DLL.

### 2. Обновите compile-time coordinate API

До 0.7.0:

```csharp
Vector3 start = transform.position;
Vector3[] path = result.Points;
navigation.RequestPath(start, destination, priority, completion);
```

В 0.7.0:

```csharp
using CustomNavigation.UnityAdapter;
using Jitter2.LinearMath;

JVector start = NavigationUnityAdapter.ToJitter(transform.position);
JVector destination = NavigationUnityAdapter.ToJitter(destinationTransform.position);
navigation.RequestPath(start, destination, priority, result =>
{
    JVector[] path = result.Points;
    transform.position = NavigationUnityAdapter.ToUnity(path[0]);
});
```

Храните route, correction и authoritative coordinates как `JVector`. Вызывайте
`NavigationUnityAdapter` только рядом с `Transform`, Gizmos, Handles или другим Unity
presentation API. `Real` в approved profile является CLR `System.Single`; f64 profile отклоняется
до narrowing.

### 3. Удалите private coordinate DTO

Удалите consumer-owned `Vector3Dto`, `ServerVector3`, reflection/`JsonUtility` path envelopes и
ручные `x/y/z` conversion helpers. Используйте `NavigationServerPathClient` либо shared
`NavigationWireCodec`; in-memory request/response coordinates имеют тип `JVector`, а canonical
wire shape остаётся `{ "x", "y", "z" }`.

### 4. Re-bake и re-export обязателен

Schema 2 добавляет `precision`, canonical Jitter SHA-256, StableMath compatibility id и
fingerprint algorithm v2. Schema 1 не получает default values и не обновляется in-place.

1. Сохраните старые payload/manifest только как rollback evidence.
2. Откройте каждую source scene в 0.7.0 и выполните `Validate` → `Build for Client`.
3. Проверьте новый manifest: `schemaVersion = 2`, `precision = f32`, fingerprint version `2`.
4. Выполните `Upload to Server` или `Export to Folder` заново.
5. Перезапустите/проверьте server health и сделайте positive query с exact artifact hash.
6. Отдельно проверьте negative wrong-hash request: он должен завершиться до DotRecast route work.

Смена manifest identity не переписывает уже сериализованный DotRecast payload, но P06 изменил
deterministic authoring rounding. Поэтому новый bake может законно получить другие payload bytes и
SHA-256; не переносите старый hash в новый manifest.

## Перед обновлением

1. Выйдите из Play Mode и сохраните открытые сцены.
2. Создайте commit или резервную копию как минимум для:

   - `Packages/manifest.json` и `Packages/packages-lock.json`;
   - `Assets/CustomNavigation`, если остался layout версии `0.6.5` или старше;
   - `Assets/DataSakura/CustomNavigation`;
   - изменённых импортированных samples;
   - `NavigationServer/NavigationData`, если server artifacts нельзя восстановить
     повторным export.

3. Зафиксируйте текущий package tag и успешный baseline: compile, нужные сцены и
   важные artifact SHA-256.
4. Откройте `Tools > DataSakura > Custom Navigation Window` и на вкладке `Settings`
   запишите назначенные shared profiles, server address и `Server Artifact Folder`.

Не удаляйте `Library/PackageCache` и не перемещайте `.meta` вручную: это не является
процедурой обновления и может скрыть проблему с GUID или importer.

## Обновление Git dependency

Замените tag в `Packages/manifest.json`:

```json
"com.datasakura.custom-navigation": "https://github.com/denisislamov/custom-navigation.git#v0.7.0"
```

Вернитесь в Unity и дождитесь окончания resolve/import/compile. Unity сам обновит
`Packages/packages-lock.json`. Если используется local или embedded package, сначала
прочитайте соответствующий раздел [Installation](installation.md), чтобы в проекте
остался только один источник package ID.

После успешной компиляции выполните только применимые миграции ниже.

## Layout версии 0.6.5 и старше

До `0.6.6` project-owned данные находились в `Assets/CustomNavigation`, а generated
scenes — в подпапке `Scene`. Текущий layout:

```text
Assets/DataSakura/CustomNavigation/
├── Generated/
├── Resources/
├── Settings/
└── Scenes/
```

Запустите встроенную GUID-safe миграцию:

1. Откройте `Tools > DataSakura > Custom Navigation Window`.
2. Перейдите на вкладку `Diagnostics`. Выбор `NavigationLevel` для layout migration не
   обязателен.
3. В секции `Project layout migration` нажмите
   `Preview / Run pre-0.6.6 Migration`.
4. В диалоге `Custom Navigation layout migration` нажмите `Run Migration`.
5. Прочитайте результат `[CustomNavigation]` в Console.

Операция использует `AssetDatabase.MoveAsset`:

- `Assets/CustomNavigation` → `Assets/DataSakura/CustomNavigation`;
- `Assets/DataSakura/CustomNavigation/Scene` →
  `Assets/DataSakura/CustomNavigation/Scenes`.

GUID папок и дочерних assets сохраняются. Повторный запуск является no-op. Если
существуют одновременно старый и новый root либо одновременно `Scene` и `Scenes`,
миграция ничего не объединяет и не перезаписывает. Разберите или архивируйте один из
конфликтующих путей, затем запустите команду снова. Если root уже перемещён, а rename
`Scene` не завершился, повторный запуск продолжит с текущего состояния.

Подробный контракт: [package folder unification](package-folder-unification.md).

## Имена generated artifacts

Legacy build мог использовать hash-based имена `*.navmesh.bytes`. Версия `0.7.0`
использует стабильную тройку в том же generated root:

```text
<levelId>.navigation.bytes
<levelId>.navigation.manifest.json
<levelId>.navigation.asset
```

Текущий filename layer распознаёт legacy имена только для GUID-safe перемещения. Schema-1
payload не становится совместимым от переименования и не загружается runtime/server. Для явной
миграции имён schema-2 artifact:

1. Сделайте backup `Assets/DataSakura/CustomNavigation/Generated/Navigation`.
2. Откройте `Tools > DataSakura > Custom Navigation Window` → `Diagnostics`.
3. В секции `Artifact filename migration` нажмите
   `Preview / Run Artifact Filename Migration`.
4. В диалоге `Navigation artifact filename migration` нажмите `Run Migration`.
5. Проверьте сообщение в Console и `Build summary` на вкладке `Bake`.

Перед перемещением команда проверяет payload SHA-256, manifest, level ID, schema,
DotRecast version и отсутствие destination conflicts. Перемещение выполняется через
`AssetDatabase.MoveAsset`: GUID, payload bytes и сериализованные ссылки сохраняются.
Повторный запуск является no-op.

Команда работает только с клиентским generated root и не переименовывает server copy.
После неё используйте `Bake` → `Upload to Server` либо `Export to Folder`, чтобы
доставить проверенный текущий артефакт серверу.

Подробнее: [имена и миграция артефактов](artifact-filenames.md).

## Импортированный sample

UPM импортирует sample как обычную project-owned копию с версией в пути:

```text
Assets/Samples/DataSakura Custom Navigation/<version>/Navigation Demos & Bots
```

Обновление package не изменяет и не удаляет старую копию.

1. Сохраните свои правки старого sample отдельно.
2. Убедитесь, что установлен `com.unity.inputsystem`.
3. В Package Manager откройте версию `0.7.0` и снова импортируйте
   `Navigation Demos & Bots`.
4. Перенесите только осознанные пользовательские изменения в новую копию или, лучше,
   в собственную project assembly.
5. После проверки удалите старую версию либо переместите архив за пределы `Assets`.

Не переименовывайте version folder вручную. Две активные копии sample содержат asmdef
с одинаковыми именами `CustomNavigation.Client` и `CustomNavigation.Client.Editor`, что
может привести к duplicate assembly errors. Editor builders новой версии создают сцены
в `Assets/DataSakura/CustomNavigation/Scenes` и могут обновлять Build Settings; перед
импортом и генерацией сохраните текущие сцены.

## Reference server после обновления

Если установлен локальный `NavigationServer`:

1. Выберите `NavigationLevel` и откройте окно `DS Navigation` → `Settings`.
2. В секции `Local server` остановите процесс кнопкой `Stop server`, если он запущен.
3. Нажмите `Reinstall from package` и подтвердите `Reinstall`.
4. Запустите сервер кнопкой `Start server`.
5. В секции `Connection check` нажмите `Check /health`.

Переустановка заменяет server sources, но сохраняет `NavigationServer/NavigationData`.
После этого всё равно сравните `levelId` и полный artifact hash клиента и сервера.

## Проверка после обновления

1. Убедитесь, что Console не содержит compile errors.
2. Откройте `Tools > DataSakura > Custom Navigation Window`.
3. Для каждого важного уровня нажмите `Overview` → `Validate`.
4. На вкладке `Bake` проверьте `Build summary`. Если scene/source/build settings
   изменились, выполните новый `Build for Client`.
5. Проверьте local runtime, затем отдельно server/hybrid сценарий, если он используется.
6. Запустите затронутые EditMode/PlayMode tests и целевой Player build.
7. Проверьте сцены, prefab references и Build Settings после обновления sample.

Package compile или успешная миграция сами по себе не подтверждают Player/IL2CPP,
server и consumer acceptance.

## Откат

Для отката package dependency верните предыдущий tag, например:

```json
"com.datasakura.custom-navigation": "https://github.com/denisislamov/custom-navigation.git#v0.6.15"
```

Затем дождитесь resolve/import/compile и повторите baseline-проверку. Не редактируйте
`Packages/packages-lock.json` вручную.

Смена tag не откатывает project-owned изменения:

- после layout migration восстановите старую структуру из commit/backup, если выбранная
  старая версия ожидает `Assets/CustomNavigation/Scene`;
- после artifact filename migration восстановите прежнюю тройку из backup либо
  пересоберите её старой версией;
- новую импортированную sample-копию удаляйте только после сохранения своих правок;
- для rollback server sources переустановите server уже из откатанного package и при
  необходимости восстановите совместимый `NavigationData`.

Если rollback требуется из-за ошибки миграции, предпочтителен полный restore
зафиксированного baseline, а не обратное ручное переименование `.meta` и assets.

Вернуться к [оглавлению](index.md).
