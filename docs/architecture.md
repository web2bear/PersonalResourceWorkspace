# Устройство проекта / Architecture

Техническая справка для разработчиков. / Technical reference for contributors.

Проект использует C#, .NET 9, WinUI 3, Windows App SDK и SQLite. Приложение запускается как self-contained unpackaged Windows-приложение. / The app uses C#, .NET 9, WinUI 3, Windows App SDK, and SQLite, with a self-contained unpackaged deployment model.

| Каталог / Directory | Назначение / Purpose |
| --- | --- |
| `src/PersonalResourceWorkspace.App` | WinUI UI, startup, composition root |
| `src/PersonalResourceWorkspace.Domain` | Domain boundary |
| `src/PersonalResourceWorkspace.Application` | Use cases and ports |
| `src/PersonalResourceWorkspace.Infrastructure` | SQLite, files, migrations, search |
| `src/PersonalResourceWorkspace.Windows` | Windows shell, clipboard, activation, visuals |
| `tests` | Unit and isolated SQLite integration tests |

Локальная база данных хранится в `%LOCALAPPDATA%\PersonalResourceWorkspace\Data\workspace.db`. Пользовательские изображения — в `Data\Visuals\user`, автоматически полученные превью — в `Data\Visuals\cache`. Миграции БД выполняются транзакционно. / The local database is stored at `%LOCALAPPDATA%\PersonalResourceWorkspace\Data\workspace.db`. User images live in `Data\Visuals\user`; generated previews are cached in `Data\Visuals\cache`. Database migrations are transactional.

Подробнее / Further reading: [project specification](../PROJECT.md), [deployment decision](adr/0001-unpackaged-deployment.md), [storage decision](adr/0002-sqlite-and-filesystem-storage.md), [architecture review](../architecture-review.md).
