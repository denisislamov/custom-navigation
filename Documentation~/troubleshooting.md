# Troubleshooting

Начинайте с полного Console stack trace, `Overview > Validate` и `Bake > Copy
Diagnostics`. Не удаляйте generated assets и `.meta` до того, как зафиксировали точную
ошибку и проверили путь.

| Симптом | Вероятная причина | Как проверить | Решение |
| --- | --- | --- | --- |
| Пакет не компилируется, `precompiledReferences` не найдены | DotRecast DLL пришли как Git LFS pointers или с повреждёнными `.meta` | Откройте DLL как binary/проверьте, что файл не начинается `version https://git-lfs.github.com`; запустите package preflight в source repo | Установите опубликованный tag заново; publisher должен хранить package binaries как обычные Git blobs |
| После Import sample появляется ошибка assembly `Unity.InputSystem` | В проекте нет `com.unity.inputsystem` | Проверьте `Packages/manifest.json` и reference в imported `CustomNavigation.Client.asmdef` | Установите Input System до импорта sample либо удалите reference и весь код sample, который его использует |
| После Import sample duplicate assembly name | В проекте уже есть legacy `CustomNavigation.Client` / `.Editor` | Найдите все `.asmdef` с этими `name` | Не держите legacy mirror и versioned imported sample одновременно; сделайте backup и мигрируйте осознанно |
| В `Tools` нет нескольких старых пунктов Custom Navigation | UI был упрощён | Найдите единственный `Tools > DataSakura > Custom Navigation Window` | Используйте вкладки `Overview`, `Geometry`, `Bake`, `Settings`, `Diagnostics`; старые отдельные menu entries намеренно удалены |
| Окно показывает `[ ] No level selected` | В сцене нет выбранного `NavigationLevel` | Проверьте Hierarchy и поле `Navigation Level` | Нажмите `Create Navigation Level Setup` или выберите существующий component |
| Уровень получает ID `unsavedscene` | Setup создан до сохранения сцены | Проверьте Scene name и `Level ID` | Сохраните сцену, задайте канонический ID вручную либо пересоздайте setup до первого production bake |
| `There is no Geometry Source with an Include mesh under the Geometry Root.` | Нет usable Include source | Откройте `Geometry`, проверьте root, `MeshFilter.sharedMesh` и Mode | Переместите geometry под root, нажмите `Add N Missing Sources`, выберите `Include` |
| Geometry tab не находит объект | `MeshFilter` находится вне `Geometry Root`, отсутствует mesh или source настроен не обходить children | Сравните Hierarchy с полем `Geometry Root`; проверьте `sharedMesh` | Переместите объект под root либо добавьте source на нужный объект и настройте `Include Children` |
| Bake сообщает, что source mesh unreadable | Importer отключил Read/Write для mesh | Выберите mesh asset и посмотрите Import Settings; Validate может предложить Fix | Включите Read/Write для source mesh и Apply, затем повторите Validate |
| `Build stopped` и окно возвращается на Overview | Validation содержит Error | Раскройте categories и первый error | Используйте `Select`/доступный safe `Fix`; warning сам по себе bake не блокирует |
| Build создаёт неожиданно грубый или тяжёлый navmesh | Не тот `Bake Quality`, agent radius/climb или manual Custom values | `Settings > Bake quality`, agent Inspector, build summary | Начните с `Balanced`; помните: меньше cell size/height — больше точность, время и размер |
| В `Custom` не пересчитываются cell values | `ApplyQualityPreset` для `Custom` намеренно ничего не меняет | Сравните поля до/после переключения | Настройте значения вручную или выберите Fast/Balanced/High Detail |
| После изменения performance profile artifact стал «старым» или потребовали rebake | Эти поля не должны менять geometry | Сравните payload SHA-256 до/после; profile не сериализуется в bytes | Не запускайте bake только из-за runtime budget; расследуйте другой scene/profile change |
| `Local navigation initialization failed` | Artifact/performance/agent не назначены либо artifact не прошёл loader | Раскройте следующее exception в Console; проверьте три Inspector references | Назначьте ссылки до Play Mode, rebuild corrupt/stale artifact, используйте тот же agent profile |
| `Unsupported navigation schema ...` | Artifact создан несовместимой версией | Сравните `Schema` в Build Details с `NavigationArtifactLoader.SupportedSchemaVersion` | Обновите package обеих сторон и выполните новый `Build for Client` |
| `Navigation artifact uses DotRecast ...` | Artifact и Runtime собраны разными DotRecast versions | Сравните manifest/asset и Runtime supported version | Перезапеките exact package version; не смешивайте bytes разных tags |
| `Navigation artifact hash mismatch: expected ..., got ...` | Payload изменён/повреждён или asset metadata не соответствует bytes | Вычислите SHA-256 файла, откройте manifest и Build Details | Восстановите из source control или выполните новый bake; не редактируйте `.bytes` |
| `Navigation artifact contains no polygons.` | Payload валиден как файл, но navmesh пуст | Validate geometry и build logs | Исправьте Include geometry/agent/bake settings и rebuild |
| `Navigation scheduler is not ready.` | `RequestPath` вызван до успешного `Awake` | Проверьте `IsReady` и предыдущие Console errors | Используйте serialized setup, вызывайте из `Start`/после ready; не делайте active `AddComponent` + поздний `Configure` |
| Scheduler завис после disable и callback не приходит | `OnDisable` не вызывает Tick или CancelAll | Проверьте, disabled ли component и существует ли owner | Отменяйте handles в caller-е; перед disable вызовите `Scheduler.CancelAll` либо уничтожьте owner |
| `NavigationQueryScheduler must be requested and ticked from its owner thread.` | Scheduler вызван с другого managed thread | Сравните место constructor и вызовов | Создавайте и используйте scheduler на Unity main thread; background workers не поддерживаются |
| `Navigation request expired in the queue.` | Request ожидал admission дольше `Query Deadline Seconds` | Смотрите `QueuedQueries`, `ActiveQueries`, budget и частоту `Tick` | Уменьшите producer rate, повысите осознанно budget/admission/backlog lifetime, используйте priorities |
| `Navigation queue is full for the current mobile performance profile.` | Backlog достиг `Maximum Queued Queries` | Смотрите metrics и профиль | Снижайте replan rate/AI LOD, отменяйте obsolete requests; не скрывайте overload бесконечной очередью |
| `Start or destination is outside the navigation artifact.` | Nearest-poly search не нашёл обе точки | Вызовите `TryProjectPosition`, визуализируйте baked layer | Перенесите точки ближе к navmesh, исправьте bake или agent extents |
| Успешный result имеет `IsPartial = true` | Corridor ограничен или destination недостижима полностью | Проверьте message и `Maximum Path Polygons` | Обрабатывайте partial отдельно; исправьте connectivity или обоснованно увеличьте buffer |
| После runtime смены profile scheduler падает/ведёт себя странно | Workspace pool/filter/buffers созданы в constructor | Сравните момент изменения с lifecycle scheduler | Не мутируйте profiles после создания; отмените requests и пересоздайте scheduler |
| Клиент и сервер дают разные path fingerprint при одинаковом artifact hash | Query filters/extents/buffer limits различаются | Сравните agent flags/costs и server log | Считайте server result authoritative для выбранной модели и отдельно синхронизируйте query contract в своём продукте |
| `Navigation server unavailable: ...` | URL/port/network policy/server lifecycle | Откройте `Settings > Connection check > Check /health`; проверьте server console | Запустите server, примените корректный address, firewall/ATS/cleartext/CORS policy |
| Телефон не видит server на `127.0.0.1` | Loopback относится к телефону | Проверьте LAN IP компьютера | Запустите server на доступном interface, настройте token и используйте LAN IP; проверьте firewall/AP isolation |
| Upload запрещён при network bind | Не задан `--upload-token` или Editor token не совпадает | Проверьте server startup args и `Settings > Artifact upload` | Задайте одинаковый token; не сохраняйте секрет в ScriptableObject |
| `Export to Folder` выполнен, но удалённый server не обновился | Folder export работает только с общей filesystem | Сравните configured folder с реальной server data folder | Используйте `Upload to Server` для remote/container server |
| `GET /health` возвращает `no-artifact` | Server запущен до первой доставки | `Diagnostics > Navigation maps` и server data folder | Выполните `Build for Client`, затем Upload или Export; restart обычно не нужен |
| Scene View не показывает sources/baked/runtime | Layer выключен, scope не включает level или artifact отсутствует | Откройте Overlay `Custom Navigation`; проверьте status | Включите layer, выберите правильный Scope/Visibility, выполните bake для Baked |
| Baked preview показывает `Out of date` | Scene/source state изменился после bake | Нажмите Validate, проверьте dirty scene/source count | Сохраните осознанные изменения и выполните новый bake |
| После update есть старый imported sample | UPM Samples — versioned copies и не обновляются автоматически | Проверьте `Assets/Samples/DataSakura Custom Navigation/<version>` | Импортируйте новый version отдельно, сравните локальные изменения, затем удалите старую копию вручную только после backup |
| Migration отказывается продолжать | Одновременно существуют source и destination folders/files | Прочитайте точные paths в Console | Разрешите конфликт вручную после backup; migration намеренно не merge/overwrite |
| Ошибка проявляется только в Player/IL2CPP | Editor test не покрывает AOT/stripping/network policy | Откройте Player build log; воспроизведите loader + query на устройстве | Добавляйте platform-specific preservation/settings только по конкретной ошибке и повторяйте acceptance |

## Диагностический порядок

1. Зафиксируйте package tag, Unity version, platform и полный stack trace.
2. Нажмите `Overview > Validate` и сохраните issue list.
3. В `Bake` раскройте `Details` и выполните `Copy Diagnostics`.
4. Включите `Baked` в Scene View Overlay и проверьте scope.
5. Для runtime запишите `IsReady` и `Metrics`.
6. Для server выполните `Settings > Connection check > Check /health`.
7. Сравните `levelId`, полный artifact SHA-256 и agent profile ID.
8. Только после этого rebuild/reimport/migrate.

## Когда нужен bug report

Приложите:

- exact Git URL/tag и Unity version;
- Console stack trace текстом;
- `Copy Diagnostics` output;
- manifest без секретов;
- screenshot соответствующего Inspector/window;
- минимальную сцену или список authoring components;
- platform/backend и Player build log, если Editor работает;
- server `/health` и request log, если проблема с HTTP.

Не прикладывайте upload tokens и приватные network credentials.
