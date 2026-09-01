# Установка DataSakura Custom Navigation 0.7.0

Рекомендуемый способ для команды и CI — Git URL с фиксированным tag `v0.7.0`.
Local и embedded варианты предназначены прежде всего для разработки пакета. Релиз не
поставляется и не проверяется как `.unitypackage` или `.tgz`.

## Требования

| Компонент | Требование |
| --- | --- |
| Unity | `6000.3` или новее по `package.json` |
| Git | Доступ к `https://github.com/denisislamov/custom-navigation.git` |
| Canonical Jitter | Отдельно установлен approved f32 release; не является UPM dependency |
| Основной package | DotRecast DLL включены; Jitter не включён |
| Sample | `com.unity.inputsystem` |
| Reference server | .NET 9 SDK; для локальной навигации не нужен |

## Обязательный prerequisite: сначала canonical Jitter

Custom Navigation намеренно **не** устанавливает Jitter автоматически и не получает его
транзитивно из Jitter Physics Baker. До добавления Custom Navigation:

1. Скачайте `DataSakura.Jitter2.Core-2.8.9-datasakura.1-rc.1.zip` и detached checksum из
   [approved Jitter release](https://github.com/denisislamov/jitter-physics-baker/releases/tag/jitter-v2.8.9-datasakura.1-rc.1).
2. Проверьте SHA-256 ZIP:

   ```text
   61896c9d63e6262c113c9c353773b36b1825b10a3630d1f9b4eb05af07977bab
   ```

3. Скопируйте из архива `Jitter2.Core.dll`, `Jitter2.Core.xml` и
   `System.Runtime.CompilerServices.Unsafe.dll` в один project-owned каталог, например
   `Assets/Plugins/DataSakura/Jitter2/`.
4. Убедитесь, что во всём проекте ровно один `Jitter2.Core.dll`, а его SHA-256 равен:

   ```text
   944666bbe73dfce5ffc5bfb18569fb0004f50e767dcbb8b471dde15242023ca6
   ```

Approved coordinates: tag `jitter-v2.8.9-datasakura.1-rc.1`, package commit
`508de73d6d82088d58a74fd41d7e09b70f009b1d`, precision `f32`. Полная identity и server
setup находятся в [Canonical Jitter dependency](canonical-jitter-dependency.md).

Только после этого сохраните сцены и убедитесь, что Console не содержит посторонних compile
errors: иначе результат импорта нельзя будет отличить от уже существующей проблемы.

## Вариант 1: Package Manager и фиксированный Git tag

1. Откройте `Window > Package Management > Package Manager`.
2. Нажмите `+` в левом верхнем углу.
3. Выберите `Add package from git URL...`.
4. Вставьте:

   ```text
   https://github.com/denisislamov/custom-navigation.git#v0.7.0
   ```

5. Нажмите `Add` и дождитесь окончания resolve/import/compile.

Ожидаемый результат:

- Package Manager показывает `DataSakura Custom Navigation` версии `0.7.0`;
- Console не содержит новых compile errors;
- доступен пункт `Tools > DataSakura > Custom Navigation Window`;
- на странице package доступна вкладка `Samples` с `Navigation Demos & Bots`.

URL без `#v0.7.0` отслеживает изменяемую ветку `main`. Он допустим для тестирования
последнего состояния, но не даёт воспроизводимой установки и не рекомендуется для
production-проекта.

## Вариант 2: `Packages/manifest.json`

Добавьте зависимость в объект `dependencies`:

```json
{
  "dependencies": {
    "com.datasakura.custom-navigation": "https://github.com/denisislamov/custom-navigation.git#v0.7.0"
  }
}
```

Сохраните файл и вернитесь в Unity. Editor сам обновит `Packages/packages-lock.json`;
не копируйте lock entry из другого проекта вручную. Проверка результата совпадает с
вариантом через Package Manager.

## Вариант 3: local package с диска

Этот вариант удобен, когда package разрабатывается в отдельном checkout.

1. Получите исходники пакета на локальный диск.
2. В Package Manager выберите `+` → `Add package from disk...`.
3. Выберите файл
   `<checkout>/Packages/com.datasakura.custom-navigation/package.json`. Если используется
   standalone package repository, выберите его корневой `package.json`.
4. Дождитесь resolve/import/compile и откройте
   `Tools > DataSakura > Custom Navigation Window`.

Unity запишет local `file:` dependency в manifest. Изменения в checkout становятся
видны потребляющему проекту без нового Git tag, поэтому путь должен быть доступен всем
разработчикам и CI. Для обычного проекта перед релизом замените local dependency на
фиксированный Git tag.

## Вариант 4: embedded package

Embedded-вариант позволяет редактировать package прямо внутри Unity-проекта.

1. Удалите Git/local dependency `com.datasakura.custom-navigation` из Package Manager
   или `Packages/manifest.json`, чтобы не оставлять два источника одного package ID.
2. Скопируйте только содержимое package в:

   ```text
   <project>/Packages/com.datasakura.custom-navigation/
   ```

3. Проверьте, что файл находится именно по пути
   `Packages/com.datasakura.custom-navigation/package.json`, а не во вложенной второй
   папке.
4. Вернитесь в Unity и дождитесь компиляции.

Embedded package является частью репозитория потребителя. Unity не обновляет его по Git
tag автоматически; синхронизация и merge изменений остаются ответственностью команды.

## Импорт sample

Package core не зависит от Input System, но sample assembly содержит прямую ссылку на
`Unity.InputSystem`.

1. Установите `com.unity.inputsystem`.
2. В Package Manager выберите `DataSakura Custom Navigation`.
3. Откройте вкладку `Samples`.
4. Нажмите `Import` у `Navigation Demos & Bots`.

Unity создаст отдельную project-owned копию:

```text
Assets/Samples/DataSakura Custom Navigation/0.7.0/Navigation Demos & Bots
```

Импортированные sample-файлы не обновляются вместе с package и не удаляются при его
удалении. Правила перехода между версиями описаны в
[Migration and upgrading](migration-and-upgrading.md#импортированный-sample).

## Неподдерживаемые форматы

- `.unitypackage` не производится и не тестируется;
- `.tgz`/tarball не является релизным каналом этого package;
- ручное копирование отдельных DLL или подпапок `Runtime` не поддерживается.

Исключение — отдельная prerequisite-установка **полного approved Jitter release set** выше;
она выполняется до установки Custom Navigation и не является копированием файлов из этого package.

Используйте Git tag, local disk или полный embedded package. Это сохраняет package
metadata, assembly definitions, лицензии и importer-настройки DotRecast DLL.

## Удаление

Перед удалением найдите собственный код и `.asmdef`, которые ссылаются на
`CustomNavigation.Authoring`, `CustomNavigation.Runtime` или
`CustomNavigation.NavigationEditor`. Сначала удалите или замените эти зависимости,
иначе проект закономерно перестанет компилироваться.

### Git или local dependency

Выберите package в Package Manager и нажмите `Remove` либо удалите
`com.datasakura.custom-navigation` из `Packages/manifest.json`. Unity обновит lock file.

### Embedded package

Удалите каталог `Packages/com.datasakura.custom-navigation` только после commit/backup.
Не оставляйте одновременно embedded-копию и manifest dependency с тем же package ID.

### Что не удаляется автоматически

UPM удаляет package, но сохраняет project-owned данные:

- `Assets/DataSakura/CustomNavigation`;
- импортированные версии под `Assets/Samples/DataSakura Custom Navigation`;
- установленный `NavigationServer` и его `NavigationData`;
- `ProjectSettings/DataSakuraCustomNavigationSettings.asset`, если он был создан.

Удаляйте эти пути отдельно только после проверки ссылок в сценах, prefab и build
settings. Generated navigation можно пересоздать, но source scenes, profiles и
пользовательские изменения следует предварительно сохранить.

## Следующий шаг

Пройдите [Quick Start](quick-start.md) или вернитесь к
[оглавлению](index.md).
