# Архитектурный обзор Personal Resource Workspace

12 октября 2026 · весь репозиторий · commit `cea865653495c269286984b4bf6a45286e4fb0d6`. До проверки tracked-файлы не изменены; `.archify/` не отслеживается.

Контракт [PROJECT.md](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/PROJECT.md#L424) требует pragmatic layered/clean architecture. Vertical Slices отдельно не заявлены; здесь это дополнительная оценка связности сценариев. README описывает Phase 0, хотя код уже реализует редактор, коллекции, корзину и визуалы; будущий MVP не считается полностью реализованным.

## Оценка

**Clean Architecture — частично:** зависимости проектов направлены внутрь, но защищённое ядро не владеет всеми правилами. **Vertical Slices — частично:** открытие выделено в Application, а создание и удаление распределены между экраном и SQL-адаптерами. Следствие — правила трудно проверять независимо от WinUI и SQLite.

| Проверка | Статус | Основание |
|---|---|---|
| R1: направление зависимостей, DIP, циклы | Соблюдено в выборке | App → адаптеры/Application; адаптеры → Application → Domain; циклов ProjectReference нет |
| R2: данные на границах | Частично | Нет WinUI/SQLite-типов в DTO; `Kind` и `Collection` — строки, связанные с отображением |
| R3: бизнес-правила | Нарушено | Политика корзины в MainPage; запреты удаления в SQLite; filesystem в фабрике Application |
| R4: связность сценариев | Частично | LaunchService выделен; операции коллекций пересекают UI, репозитории и обе таблицы |
| R5: совместные правила | Частично | UI и адаптер дублируют запреты; membership зависит от изменяемого имени |
| R6: контракты SOLID | Частично | Один `address` используется для текста, пути и команды; нарушение LSP не установлено |
| R7: проверка поведения | Частично | Есть outcome-тесты SQLite и Web/File; ошибки пакетного сохранения и сценарий Note ими не покрыты |
| R8: польза абстракций | Соблюдено в выборке | Порты shell, clipboard и persistence изолируют платформу; лишний dispatcher не выявлен |

Сильные стороны: composition root находится в [App](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.App/App.xaml.cs#L33); переименование и удаление коллекции выполняются транзакционно, с rollback, в [SQLite-адаптере](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.Infrastructure/Persistence/SqliteCollectionRepository.cs#L79).

## Рекомендации

### P1 · Текстовая заметка открывается как путь

**Доказательство:** [фабрика](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.Application/Resources/ResourceDraftFactory.cs#L44) кладёт текст в Address; [LaunchService](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.Application/Resources/ResourceLaunchService.cs#L21) направляет TextSnippet в OpenPathAsync; [WindowsShellLauncher](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.Windows/Shell/WindowsShellLauncher.cs#L21) вызывает Process.Start. Это следует из кода, без запуска shell.

**Последствие:** вместо требуемого Copy выполняется shell-open строки. **Изменение:** отдельное действие Copy через clipboard-порт, типизированный payload; не направлять неподдержанные типы в общий path-handler. **Проверка:** открытие Note копирует точный текст и не вызывает shell. R3/R6/R7.

### P2 · Правила коллекций принадлежат UI и SQL

**Доказательство:** [MainPage](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.App/MainPage.xaml.cs#L1475) выбирает fallback и проверяет защиту; [репозиторий](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.Infrastructure/Persistence/SqliteCollectionRepository.cs#L99) повторяет запреты. Перенос в корзину решается в [code-behind](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.App/MainPage.xaml.cs#L1349).

**Последствие:** новый вход должен воспроизводить политику экрана; правило нельзя целиком проверить без адаптеров. **Изменение:** Application-сценарии удаления/переноса, UI собирает подтверждение, persistence сохраняет атомарность. **Проверка:** одинаковые результаты через UI и fake-порты, запрет удаления системных коллекций. R3/R4/R5.

### P2 · Membership зависит от имени коллекции

**Доказательство:** [ResourceRecord](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.Application/Models/ResourceRecord.cs#L9) хранит строку Collection; [schema](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.Infrastructure/Persistence/SqliteMigrationRunner.cs#L88) не задаёт FK; [rename](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.Infrastructure/Persistence/SqliteCollectionRepository.cs#L73) обновляет Resources по имени.

**Последствие:** переименование требует синхронизации нескольких представлений; SaveAsync допускает ссылку на отсутствующую коллекцию. **Изменение:** стабильный CollectionId с FK, имя оставить для отображения; миграцию планировать отдельно. **Проверка:** rename сохраняет membership, неизвестный ID отклоняется. R2/R5.

### P2 · Пакетное добавление неатомарно

**Доказательство:** [AddResourcesAsync](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.App/MainPage.xaml.cs#L470) сначала меняет UI; [SaveResourcesAsync](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.App/MainPage.xaml.cs#L1050) сохраняет весь список отдельными вызовами; [адаптер](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.Infrastructure/Persistence/SqliteResourceRepository.cs#L37) открывает отдельное соединение на ресурс.

**Риск при ошибке:** часть пакета сохранена, экран содержит несохранённые элементы. **Изменение:** сохранять только добавляемый пакет одной транзакцией, публиковать UI-state после успеха. **Проверка:** ошибка на втором элементе не сохраняет первый и не меняет экран. R4/R7.

### P2 · Application самостоятельно обращается к filesystem

**Доказательство:** [TryFromPath](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/src/PersonalResourceWorkspace.Application/Resources/ResourceDraftFactory.cs#L209) вызывает Directory.Exists/File.Exists из сценариев clipboard/drop.

**Последствие:** классификация зависит от реального диска; синхронная проверка сетевого пути может задерживать UI — это гипотеза, время не измерено. **Изменение:** передавать сведения о пути из платформенного адаптера; parsing оставить чистым. **Проверка:** классификация через fake-метаданные без диска. R1/R3.

## Покрытие и ограничения

Прослежены чтение Resources → DTO → карточки, запись редактора/drop → repository → SQL, удаление коллекции и открытие Note/Web/File. Прочитаны архитектурный тест Domain, ApplicationBoundaryTests, тесты запуска и isolated SQLite. Runtime UI, visual pipeline целиком, производительность и будущая иерархия коллекций не проверялись. Пользовательская БД не использовалась; исходники не изменялись.

Выполнены попытки `dotnet test --no-restore` для трёх проектов и отдельный запуск Application с `--no-build --no-restore --blame-hang-timeout 30s`. Первый запуск остановился на старте выполнения Domain; до Application/Infrastructure последовательная команда не дошла. Повторный запуск Application завершился ошибкой инфраструктуры VSTest: vstest.console не подключился к datacollector за 90 секунд. Успешное прохождение тестов не подтверждено; это не свидетельствует о провале проверок поведения. Новые тесты в рамках обзора не добавлялись.

## Источники

[PROJECT.md](https://github.com/web2bear/PersonalResourceWorkspace/blob/cea865653495c269286984b4bf6a45286e4fb0d6/PROJECT.md#L420) — контракт проекта. Проверены 12 октября 2026: [Robert C. Martin, The Clean Architecture](https://blog.cleancoder.com/uncle-bob/2012/08/13/the-clean-architecture.html) — защищённые правила и направление зависимостей; [Jimmy Bogard, Vertical Slice Architecture](https://www.jimmybogard.com/vertical-slice-architecture/) — связность по сценариям изменений. Приоритеты и выводы — оценка данного кода.
