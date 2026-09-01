# Quick Start: первый локальный путь за 5–15 минут

Этот сценарий начинается с пустой сцены, создаёт плоскую навигационную поверхность,
запекает клиентский артефакт и выполняет один локальный path query. HTTP-сервер,
Unity Physics и встроенный NavMesh не нужны.

## Перед началом

- отдельно установлен approved canonical Jitter f32 release;
- установлен DataSakura Custom Navigation 0.7.0;
- проект открыт в Unity 6000.3 или новее;
- Console не содержит compile errors;
- сцена сохранена: имена автоматически создаваемых assets зависят от имени сцены.

> **Важно.** Не начинайте в несохранённой сцене: setup получит fallback ID
> `unsavedscene`, а assets — соответствующие имена. Сначала выполните
> `File > Save As`.

## 1. Создайте сцену и authoring setup

1. Выполните `File > New Scene`, выберите обычную пустую сцену и сохраните её как
   `Assets/Scenes/NavigationQuickStart.unity`.
2. Откройте `Tools > DataSakura > Custom Navigation Window`.
3. На вкладке `Overview` нажмите `Create Navigation Level Setup`.

Команда создаёт в Hierarchy объект `Navigation Level` с компонентом
`NavigationLevel` и дочерними группами:

```text
Navigation Level
├── NavigationGeometry
├── NavigationModifiers
├── NavigationLinks
└── NavigationTestPoints
```

Она также создаёт недостающие `NavigationAgentProfile`, `NavigationAreaCatalog` и
`NavigationPerformanceProfile` в
`Assets/DataSakura/CustomNavigation/Generated/Settings`. Если в
`Project Settings > DataSakura > Custom Navigation` уже назначены все shared defaults,
новый уровень использует их вместо дубликатов.

*После setup сохраните сцену ещё раз. Поле `Level ID` должно быть
`navigationquickstart`, а три profile-ссылки — назначены.*

## 2. Добавьте геометрию

1. Выберите `NavigationGeometry` в Hierarchy.
2. Создайте `GameObject > 3D Object > Plane` как его дочерний объект.
3. Оставьте Transform плоскости в `(0, 0, 0)` и Scale `(1, 1, 1)`.
4. Откройте вкладку `Geometry` в окне `DS Navigation`.
5. Нажмите `Add 1 Missing Sources`.

На `Plane` появится `NavigationGeometrySource` со значениями:

- `Mode = Include`;
- `Area = Ground (regular floor)`;
- `Include Children = false`;
- `Include Inactive Children = false`.

Кнопка сканирует только `MeshFilter` под `Geometry Root`. Объекты вне этой ветки не
добавляются в bake автоматически.

> **Ошибка новичка.** `MeshRenderer` или Collider сами по себе не являются source.
> Пакет читает `MeshFilter.sharedMesh`, помеченный `NavigationGeometrySource`.

## 3. Проверьте и запеките

1. Вернитесь на `Overview` и нажмите `Validate`.
2. Убедитесь, что статус показывает `Ready to export` или только warnings, которые вы
   осознанно приняли.
3. Откройте `Bake`.
4. Нажмите `Build for Client`.

`Build for Client` сам повторяет validation. При успехе Unity выбирает созданный
`NavigationArtifactAsset`, а окно показывает `Build summary` с состоянием, SHA-256,
числом полигонов и размером payload.

Для ID `navigationquickstart` появляются файлы:

```text
Assets/DataSakura/CustomNavigation/Generated/Navigation/
├── navigationquickstart.navigation.bytes
├── navigationquickstart.navigation.bytes.meta
├── navigationquickstart.navigation.manifest.json
├── navigationquickstart.navigation.manifest.json.meta
├── navigationquickstart.navigation.asset
└── navigationquickstart.navigation.asset.meta
```

*`Export to Folder` и `Upload to Server` сейчас не нужны. Они доставляют уже
построенный артефакт и не заменяют `Build for Client`.*

## 4. Настройте локальный runtime

1. Создайте пустой объект `Navigation Runtime`.
2. Выберите `Component > DataSakura > Custom Navigation > Query Scheduler`.
3. В компоненте `Navigation Query Scheduler Behaviour` назначьте:

   - `Artifact` — `navigationquickstart.navigation.asset`;
   - `Performance Profile` — профиль уровня;
   - `Agent Profile` — тот же профиль агента, с которым выполнен bake;
   - `Log Budget Warnings` — включён.

*Все три ссылки должны быть назначены до входа в Play Mode. `Awake` загружает
артефакт один раз; поздний вызов `Configure` не пересоздаёт scheduler.*

## 5. Выполните запрос

Создайте `Assets/NavigationQuickStartRequester.cs`:

```csharp
using CustomNavigation.Authoring;
using CustomNavigation.Runtime;
using CustomNavigation.UnityAdapter;
using Jitter2.LinearMath;
using UnityEngine;

public sealed class NavigationQuickStartRequester : MonoBehaviour
{
    [SerializeField] private NavigationQuerySchedulerBehaviour navigation;

    private NavigationPathHandle request;

    private void Start()
    {
        request = navigation.RequestPath(
            NavigationUnityAdapter.ToJitter(new Vector3(-4f, 0f, -4f)),
            NavigationUnityAdapter.ToJitter(new Vector3(4f, 0f, 4f)),
            NavigationQueryPriority.PlayerImmediate,
            OnPathCompleted);
    }

    private void OnDestroy()
    {
        request.Cancel();
    }

    private static void OnPathCompleted(NavigationPathResult result)
    {
        if (!result.Success)
        {
            Debug.LogError($"Quick Start path failed: {result.Message}");
            return;
        }

        Debug.Log($"Quick Start path: {result.Points.Length} points.");
        JVector first = result.Points[0];
        Vector3 firstUnityPoint = NavigationUnityAdapter.ToUnity(first);
    }
}
```

Добавьте компонент `NavigationQuickStartRequester` на `Navigation Runtime` и перетащите
тот же объект в поле `Navigation`.

Этот пример компилируется против `CustomNavigation.Authoring`, `CustomNavigation.Runtime`,
`CustomNavigation.UnityAdapter` и отдельно установленного `Jitter2.Core.dll`. Если gameplay-код
находится в собственной `.asmdef`, добавьте три assembly references и direct
`precompiledReferences` на Jitter; подробности — в [Integration](integration.md).

## 6. Запустите и проверьте результат

1. Сохраните сцену.
2. Включите Play Mode.
3. Откройте Console.

Ожидаются сообщения:

```text
[CustomNavigation] Local artifact ready: level=navigationquickstart, ...
Quick Start path: <N> points.
```

`N` зависит от получившихся Detour polygons. Важен `Success = true`, а не конкретное
число точек.

Если вывод сообщает `Start or destination is outside the navigation artifact`, сначала
вызовите `TryProjectPosition` или перенесите координаты внутрь плоскости. Если компонент
пишет `Local navigation initialization failed`, проверьте три ссылки и откройте полное
исключение в Console.

## Быстрый путь через sample

Чтобы изучить готовые сценарии:

1. Установите `com.unity.inputsystem`.
2. Откройте `Window > Package Management > Package Manager`.
3. Выберите `DataSakura Custom Navigation`.
4. На вкладке `Samples` импортируйте `Navigation Demos & Bots`.

Unity копирует sample в
`Assets/Samples/DataSakura Custom Navigation/0.7.0/Navigation Demos & Bots`.
Его Editor builders создают demo scenes и могут менять Build Settings. Импорт не следует
считать read-only операцией; перед ним сохраните сцены и commit/stash изменения проекта.

Sample показывает:

- локальный scheduler и ботов;
- server-only запрос;
- local prediction + authoritative correction;
- несколько площадок на разных высотах;
- `NavigationEditorApi` без запуска сервера.

Следующий шаг: [Editor Guide](editor-guide.md) или [Runtime API](runtime-api.md).
