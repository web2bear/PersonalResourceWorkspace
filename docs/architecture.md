# Устройство проекта / Architecture

Техническая справка для разработчиков. / Technical reference for contributors.

Проект использует C#, .NET 9, WinUI 3, Windows App SDK и SQLite. Приложение запускается как self-contained unpackaged Windows-приложение. / The app uses C#, .NET 9, WinUI 3, Windows App SDK, and SQLite, with a self-contained unpackaged deployment model.

| Каталог / Directory | Назначение / Purpose |
| --- | --- |
| `src/PersonalResourceWorkspace.App` | WinUI UI, startup, composition root |
| `src/PersonalResourceWorkspace.Domain` | Domain boundary |
| `src/PersonalResourceWorkspace.Application` | Use cases and ports |
| `src/PersonalResourceWorkspace.Infrastructure` | SQLite, files, workspace initialization and backup |
| `src/PersonalResourceWorkspace.Windows` | Windows shell, clipboard, activation, visuals |
| `tests` | Unit and isolated SQLite integration tests |

По умолчанию данные находятся в `content` рядом с исполняемым файлом: БД — `Data\workspace.db`, пользовательские изображения — `Data\Visuals\user`, автоматически полученные превью — `Data\Visuals\cache`. Все пользовательские настройки, кроме пути к каталогу данных, хранятся в таблице `Settings` БД. Исторический каталог `%LOCALAPPDATA%\PersonalResourceWorkspace` копируется при первом запуске с пустым portable-каталогом. / By default, app data lives in `content` beside the executable. The database is `Data\workspace.db`; user images and generated previews are under `Data\Visuals`. User preferences live in SQLite. A legacy profile is copied on first launch into an empty portable data root.

Новая схема хранит ресурсы, тэги, связи ресурса с тэгами, сохранённые поиски и их обязательные тэги в отдельных таблицах с внешними ключами. При обнаружении старой схемы создаётся проверенная резервная копия `workspace.collections-backup-*.db`, затем новая пустая база. Переноса записей коллекций нет. / The new schema separates resources, tags, resource tags, saved searches, and required search tags. A legacy database is backed up before a fresh empty database is created; collection rows are not imported.

Для новой веб-ссылки автоматический визуал сначала пытается получить снимок страницы через установленный Microsoft Edge в headless режиме. Снимок ограничен областью 480×300 пикселей, сохраняется как JPEG размером не более 512 КБ и выполняется в фоне с ограничением времени. Если Edge отсутствует или снимок не получен, используются `og:image`, иконка сайта и затем значок типа. Уже сохранённый автоматический визуал остаётся в кэше до ручного обновления в редакторе. / For a new web link, the automatic visual first tries a headless Microsoft Edge screenshot. The 480×300 viewport is stored as a JPEG capped at 512 KB. If capture fails, the existing Open Graph image, site icon, and type-icon fallbacks apply. Previously cached visuals remain until manually refreshed in the editor.

Подробнее / Further reading: [project specification](../PROJECT.md), [portable storage decision](adr/0003-portable-data-and-settings.md), [architecture review](../architecture-review.md).
