# Personal Resource Workspace — Project Specification for Codex

> Нормативная спецификация для реализации Windows-приложения на C# + .NET + WinUI 3 + Windows App SDK + SQLite.

## 0. Как Codex должен использовать этот документ

Этот файл является исходным продуктовым и техническим контрактом проекта. При конфликте с предположениями, примерами, шаблонным кодом или неявными решениями приоритет имеют требования этого файла.

Нормативные термины:

- **MUST** — обязательное требование. Работа не считается завершённой без его выполнения.
- **SHOULD** — рекомендуемое требование. Отклонение допустимо только по зафиксированной технической причине.
- **MUST NOT** — запрещённое решение.

Codex MUST:

1. Перед изменениями изучить этот файл, существующую структуру решения и локальные инструкции репозитория.
2. Сначала составить короткий план затрагиваемых компонентов и критериев проверки.
3. Делать небольшие, связные изменения, не переписывая несвязанный пользовательский код.
4. После каждого значимого этапа собирать решение; для startup/UI/lifecycle-изменений также запускать приложение и проверять появление рабочего окна.
5. Добавлять или обновлять тесты вместе с бизнес-логикой.
6. Не объявлять задачу завершённой, пока не проверены относящиеся к ней критерии приёмки.

## 1. Product vision

**Personal Resource Workspace** — local-first Windows utility, объединяющая в едином визуальном пространстве:

- приложения;
- веб-ссылки;
- файлы и папки;
- текстовые и графические фрагменты;
- пользовательские команды;
- коллекции с произвольной вложенностью.

Продукт MUST быть ближе по основной модели взаимодействия к **Raindrop.io** и визуальным закладкам **Vivaldi**, чем к command palette.

Ключевая идея:

> Пользователь организует не отдельные списки сайтов, файлов и программ, а единое визуальное пространство ресурсов, которые можно узнать по месту, изображению, иконке и контексту, а затем быстро активировать.

## 2. Product principles

### 2.1 Visual-first

- Основной сценарий MUST быть визуальным: открыть workspace → выбрать коллекцию → распознать карточку → выполнить действие.
- Пользователь MUST иметь возможность найти ресурс, не помня его точное имя, alias или search prefix.
- Карточка MUST показывать как минимум название и визуальный идентификатор: thumbnail, icon или стабильный type fallback.
- Расположение и ручной порядок карточек SHOULD сохраняться предсказуемо.
- Search MUST быть вторичным ускорителем, а не обязательной точкой входа.

### 2.2 Local-first

- Все основные возможности MUST работать без сети и без учётной записи.
- Пользовательские данные MUST храниться локально.
- MVP MUST NOT требовать облачного сервиса, телеметрии, авторизации или подписки.
- Сбой извлечения внешнего thumbnail MUST NOT блокировать создание или открытие ресурса.

### 2.3 Fast resident utility

- Приложение MUST поддерживать resident process и системный global hotkey для show/hide.
- Повторный вызов hotkey MUST показывать уже запущенный экземпляр, а не создавать новый.
- Закрытие главного окна через обычный пользовательский сценарий SHOULD скрывать окно, оставляя resident process активным; команда Exit MUST завершать процесс явно.
- Показ окна MUST быть быстрым, восстанавливать последний рабочий контекст и переводить окно на передний план.

### 2.4 Windows-native

- UI MUST быть реализован на WinUI 3 и Windows App SDK.
- Приложение MUST использовать стандартные WinUI-контролы, Fluent resources и Windows App SDK API до создания собственных замен.
- Интеграции с shell, clipboard, file picker, drag-and-drop, icons, activation и windowing MUST быть изолированы за интерфейсами platform services.

## 3. Scope и основные пользовательские сценарии

### 3.1 MVP user stories

Пользователь MUST иметь возможность:

1. Запустить приложение и открыть/скрыть его глобальной горячей клавишей.
2. Создать, переименовать, отредактировать и удалить коллекцию.
3. Создать коллекцию внутри любой другой коллекции без искусственного ограничения глубины.
4. Перейти в дочернюю коллекцию и вернуться назад через breadcrumb/back navigation.
5. Добавить Application, Web, File, Folder, TextSnippet, ImageSnippet и Command.
6. Добавить ресурс drag-and-drop там, где Windows предоставляет однозначные данные для его создания.
7. Видеть ресурсы в визуальной сетке с icon/thumbnail и понятным fallback.
8. Изменить название, описание, визуальное представление и параметры ресурса.
9. Выполнить default action кликом/Enter.
10. Открыть отсортированный список дополнительных действий через context menu или доступную с клавиатуры команду.
11. Переместить ресурс между коллекциями и изменить ручной порядок внутри коллекции.
12. Найти ресурс локальным поиском по названию и релевантным метаданным.
13. Скопировать или вставить содержимое snippet через clipboard.
14. Перезапустить приложение без потери структуры, порядка и пользовательских assets.

### 3.2 Navigation model

```text
Workspace
  └── Root collection
       ├── Nested collection
       │    └── Resource or nested collection
       └── Resource
             └── Ordered actions (first enabled action is default)
```

- Коллекция MUST быть одновременно навигационным контейнером и допустимым элементом другой коллекции.
- MVP SHOULD использовать одно главное окно.
- Главный content surface MUST показывать содержимое текущей коллекции.
- Breadcrumb/back stack MUST отражать фактическую иерархию, а не отдельную копию навигационного состояния.
- NavigationView MAY использоваться для небольшого набора стабильных направлений (например, Home, Recent, Settings), но MUST NOT дублировать всё дерево коллекций.

## 4. Ubiquitous language и domain model

В коде MUST использоваться единообразные английские имена доменных сущностей. Термины `Bookmark` и `Resource` MUST NOT хаотично смешиваться. Каноническое имя сущности — `Resource`; `IVisualBookmark` является её публичным доменным контрактом.

### 4.1 IVisualBookmark / Resource

```csharp
public interface IVisualBookmark
{
    Guid Id { get; }
    ResourceType Type { get; }
    string Title { get; }
    string? Description { get; }
    VisualDescriptor Visual { get; }
    IReadOnlyList<IAction> Actions { get; }
}
```

Конкретная форма может уточняться, но модель MUST обеспечивать:

- стабильный `Guid`;
- discriminator `ResourceType`;
- title и optional description;
- типизированные payload-данные;
- visual descriptor;
- timestamps `CreatedAtUtc` и `UpdatedAtUtc`;
- упорядоченные actions;
- безопасное расширение новыми типами ресурсов без изменения UI каждой существующей карточки.

### 4.2 Resource types

`ResourceType` MUST включать:

- `Application` — запускаемый файл или зарегистрированное приложение с параметрами запуска;
- `Web` — HTTP/HTTPS URI, открываемый системным браузером;
- `File` — путь к файлу и shell-open;
- `Folder` — путь к каталогу и shell-open;
- `TextSnippet` — локальный текст, default action обычно Copy;
- `ImageSnippet` — локальный asset изображения, default action определяется UX, Copy SHOULD быть доступен;
- `Command` — явно заданная команда/исполняемый файл с аргументами и рабочей директорией;
- `Collection` — ссылка на дочернюю коллекцию, default action Navigate.

Каждый тип MUST иметь собственный типизированный payload или handler. Код MUST NOT превращать все payload в невалидируемый общий dictionary/string blob на уровне домена.

### 4.3 Collections и membership

Модель MUST разделять:

- сущность `Collection`;
- сущность `Resource`;
- membership/placement, связывающий элемент с родительской коллекцией и содержащий `SortOrder`.

Требования:

- Root collection MUST существовать всегда и MUST NOT удаляться.
- Коллекция MUST NOT становиться собственным предком; перемещения, создающие цикл, MUST отклоняться до записи в БД.
- Удаление непустой коллекции MUST требовать явного выбора: отмена либо каскадное удаление её содержимого. Неявная потеря данных запрещена.
- Manual ordering MUST использовать устойчивое sortable value; реализация SHOULD позволять вставку/перемещение без перенумерации всей большой коллекции на каждом drag.
- Все операции изменения дерева MUST выполняться транзакционно.

### 4.4 IAction и default action

```csharp
public interface IAction
{
    string Id { get; }
    string DisplayName { get; }
    string? IconKey { get; }
    int SortOrder { get; }
    bool IsEnabled(ActionContext context);
    Task<ActionResult> ExecuteAsync(
        ActionContext context,
        CancellationToken cancellationToken);
}
```

- Для каждого ресурса actions MUST возвращаться в детерминированном порядке: `SortOrder`, затем стабильный tie-breaker.
- Первое enabled действие в отсортированном списке MUST считаться default action.
- Карточка по click/double-click/Enter MUST выполнять именно default action согласно выбранной единой interaction policy.
- Context menu MUST показывать тот же порядок действий, визуально обозначая default action.
- UI MUST NOT знать детали запуска каждого resource type; он вызывает action service/dispatcher.
- Выполнение MUST быть async, поддерживать cancellation там, где она содержательно возможна, и возвращать типизированный результат/ошибку.
- Опасные или потенциально разрушительные команды MUST требовать явного подтверждения.
- `Command` MUST NOT по умолчанию интерпретировать произвольную строку через shell. Исполняемый файл и arguments SHOULD храниться раздельно; shell mode допускается только как явная опция с предупреждением.

### 4.5 VisualDescriptor

`VisualDescriptor` описывает не WinUI-объект, а сохраняемый пользовательский выбор и данные, необходимые для разрешения визуала. Domain/Application model MUST NOT хранить `ImageSource`, `BitmapImage`, `IconElement` или абсолютный путь к внутреннему asset.

Модель MUST различать:

- `VisualMode`: `Auto`, `UserAsset` или `TypeFallback`;
- фактически разрешённый `VisualKind`: `Image`, `Icon`, `GeneratedPreview` или `TypeFallback`;
- `AssetId` для app-owned изображения/иконки, если выбран или закэширован локальный asset;
- `Origin`: `UserProvided`, `Payload`, `WindowsShell`, `RemoteMetadata`, `Generated` или `Fallback`;
- fingerprint источника для безопасной инвалидации auto visual после изменения URI, path, payload или исходного файла;
- параметры отображения, если они заданы пользователем: focal point/crop position и optional background/accent; отсутствие этих параметров MUST иметь детерминированный default.

`VisualMode` MUST иметь следующую семантику:

- `Auto` — приложение выбирает лучший доступный type-specific visual и может обновить его после сохранения ресурса;
- `UserAsset` — выбранный пользователем локальный image/icon имеет приоритет и MUST NOT заменяться фоновым автообновлением;
- `TypeFallback` — пользователь явно отключил auto visual; карточка использует стабильный fallback соответствующего типа.

Resolver MUST сначала учитывать режим: `TypeFallback` немедленно выбирает fallback; `Auto` пропускает пользовательский asset и начинает с type-specific источника; `UserAsset` начинает с пользовательского asset и при его повреждении продолжает безопасную цепочку fallback, не меняя сохранённый режим без действия пользователя.

Visual resolution MUST поддерживать приоритет:

1. валидный пользовательский asset при `UserAsset`;
2. type-specific payload preview, если он является содержимым самого ресурса, например изображение `ImageSnippet`;
3. сохранённый или сгенерированный thumbnail;
4. извлечённая icon, включая Windows shell icon или web favicon;
5. theme-aware fallback по типу ресурса.

Если приоритетный visual отсутствует, повреждён или временно недоступен, resolver MUST последовательно перейти к следующему источнику. Ошибка или отсутствие asset MUST приводить к fallback, а не к пустой области, сломанному image control или падению карточки.

`VisualDescriptor` MUST ссылаться на внутренний asset через стабильный логический идентификатор. Пользовательский image/icon MUST копироваться в app-owned storage; ссылка на исходный файл MAY храниться как metadata для команды обновления, но MUST NOT быть единственным способом показать выбранный visual.

### 4.6 Visual policy по типам ресурсов

Для каждого `ResourceType` MUST существовать отдельная policy разрешения visual. Общий generic glyph допустим только как последний fallback, а не как единственный реализованный вид для всех типов.

| ResourceType | Auto visual в порядке приоритета | Стабильный fallback | Требования создания/редактирования |
| --- | --- | --- | --- |
| `Application` | icon выбранного executable или зарегистрированного приложения через Windows shell; для `.lnk` MUST сначала разрешаться target приложения или явно заданный icon location, а не generic shortcut icon; доступный logo/thumbnail приложения | Fluent application glyph | После выбора target редактор MUST показать найденную icon; пользователь MUST иметь возможность заменить её своим image/icon или выбрать fallback. Изменение target MUST инвалидировать только auto visual. Разрешение `.lnk` MUST NOT запускать target. |
| `Web` | локально сохранённая preview image из metadata страницы; favicon; доменный monogram MAY использоваться как промежуточный generated preview | Fluent globe/link glyph | Для валидного HTTP/HTTPS URI приложение MUST уметь асинхронно попытаться получить favicon; Open Graph/social preview SHOULD использоваться как cover, если доступна. Пользователь MUST иметь возможность выбрать локальный image/icon, повторить загрузку или отключить remote lookup. |
| `File` | thumbnail содержимого, если Windows shell предоставляет его; иначе shell icon зарегистрированного file type | Fluent document glyph с optional безопасным extension badge | После выбора файла редактор MUST показать доступный shell visual. Image, video, PDF и другие preview-capable файлы SHOULD использовать thumbnail; извлечение MUST NOT открывать/исполнять файл. |
| `Folder` | Windows shell folder icon; custom folder image/thumbnail, если он однозначно и безопасно предоставлен shell | Fluent folder glyph | После выбора папки редактор MUST показать найденную icon. Сканирование содержимого папки для построения collage MUST NOT выполняться в MVP. |
| `TextSnippet` | локально сгенерированный typographic preview из ограниченного числа первых непустых строк | Fluent quote/text glyph | Редактор MUST показывать preview без записи полного текста в cache key, telemetry или log. Пользователь MUST иметь возможность выбрать image/icon либо принудительный fallback. |
| `ImageSnippet` | preview самого локально импортированного image asset | Fluent image glyph | Preview MUST появляться сразу после выбора изображения. Замена snippet image MUST обновить auto preview; пользовательская отдельная cover MUST сохранять приоритет до reset в `Auto`. |
| `Command` | icon явно выбранного executable через Windows shell | Fluent terminal/command glyph | Icon extraction MUST NOT выполнять command, script или shell expansion. Если executable нельзя определить безопасно, редактор сразу показывает fallback. |
| `Collection` | generated mosaic из дочерних visuals MAY быть post-MVP | Fluent folder/collection glyph | Редактор коллекции MUST позволять выбрать image/icon и вернуться к fallback. Автоматическое чтение всех descendants ради preview MUST NOT блокировать открытие коллекции. |

Общие правила для всех типов:

- создание и редактирование MUST предоставлять `Auto`, `Выбрать изображение/иконку` и `Использовать значок типа`; `Удалить пользовательский визуал` MUST возвращать режим `Auto`, а не удалять payload ресурса;
- editor MUST показывать итоговый preview до сохранения и явно обозначать состояние `Загрузка`, `Не удалось получить` или `Будет использован значок типа`;
- save MUST быть доступен при любой ошибке auto extraction; визуал не является обязательным блокирующим полем;
- пользовательский visual MUST поддерживаться для каждого типа, включая `Collection`;
- shell icon с прозрачными или полупрозрачными полями MUST нормализоваться по видимой области и отображаться в предсказуемом размере с небольшим внутренним отступом; размер исходного bitmap canvas MUST NOT приводить к визуально микроскопической иконке;
- `Icon` MUST отображаться на нейтральной theme-aware поверхности, обеспечивающей различимость цветной иконки в light, dark и high-contrast themes; type accent MUST NOT использоваться как безусловный фон под icon, если он может сливаться с её основным цветом;
- SVG/ICO и другие форматы MAY приниматься через безопасную нормализацию в поддерживаемый внутренний raster format; неподдерживаемый или чрезмерно большой файл MUST давать recoverable validation error без изменения ранее сохранённого visual;
- изменение title/description MUST NOT сбрасывать visual; изменение type или type-specific source MUST запускать переоценку только для `Auto`;
- удаление или замена visual MUST учитывать совместное использование assets и MUST NOT удалять бинарный файл, пока на него ссылается другой ресурс.

## 5. UX и WinUI requirements

### 5.1 Main shell

- Приложение MUST иметь одно основное окно с системно корректным title bar и `AppWindow`-управлением.
- Window placement, size и последний открытый collection SHOULD восстанавливаться с проверкой доступных monitor bounds.
- Empty areas кастомного title bar MUST оставаться draggable; caption buttons MUST сохранять ожидаемое поведение.
- Mica SHOULD использоваться для долгоживущего базового слоя при поддержке системой; fallback MUST быть theme-aware.
- Light, dark и high contrast MUST поддерживаться без жёстко заданных цветов.

### 5.2 Collection surface

- Основное представление MUST быть виртуализируемой responsive grid/list surface.
- Карточка MUST иметь выделенный visual slot и включать visual, title и только необходимую secondary information. Для каждого resource type MUST отображаться разрешённое изображение, thumbnail или icon, если оно доступно; type fallback показывается только когда более приоритетный visual отсутствует, загружается или повреждён.
- В card/grid режиме cover image MUST использовать единое aspect ratio внутри текущего view mode и `UniformToFill` с предсказуемым crop; icon MUST отображаться целиком через `Uniform`, центрироваться и иметь ограниченный максимальный размер. Растягивание с нарушением пропорций MUST NOT использоваться.
- В compact/list и narrow режимах visual slot MAY уменьшаться до квадратной icon/thumbnail области, но MUST NOT исчезать полностью. Один и тот же resolver MUST определять visual во всех view modes, чтобы ресурс не менял идентичность при переключении layout.
- Пока auto visual загружается, карточка MUST сразу показывать стабильный type fallback; появление готового visual MUST обновлять только соответствующую карточку без изменения sort order, размера карточки, scroll position или keyboard focus.
- Type, favorite, missing-target и error indicators MAY накладываться поверх visual, но MUST оставаться читаемыми и не закрывать основной узнаваемый фрагмент изображения.
- Visual MUST NOT быть единственным носителем смысла: title и доступное имя карточки MUST сохраняться, а AutomationProperties.Name SHOULD включать title и локализованное название типа. Декоративный image element SHOULD быть исключён из отдельного чтения Narrator.
- Built-in `GridView`, `ListView` или `ItemsRepeater` SHOULD использоваться исходя из проверенной virtualization и reorder/drop-модели.
- Вложенные конкурирующие ScrollViewer MUST NOT использоваться без явного владельца каждой оси прокрутки.
- UI MUST иметь wide, medium и narrow states: число колонок, padding и command density должны адаптироваться, а не просто сжиматься.
- Touch, mouse и keyboard MUST обеспечивать доступ к одним основным действиям.
- Нельзя строить визуальный язык из лишних `Border` и «карточек вокруг карточек»; стандартные WinUI surfaces имеют приоритет.

### 5.2.1 Resource visual editor

- Create/Edit dialog MUST содержать единый reusable visual editor для всех типов ресурсов, а не отдельную несогласованную реализацию только для `Web`.
- Visual editor MUST содержать preview, текущий режим (`Auto`, пользовательский visual или fallback), выбор локального файла, reset и type-specific команду повторного auto extraction, если источник поддерживает extraction.
- Выбор visual MUST быть обратим до сохранения: Cancel MUST оставить descriptor и assets ресурса без изменений и очистить только временно импортированные файлы.
- При смене type editor MUST предупредить только если выбранный пользовательский visual или его настройки несовместимы; совместимый `UserAsset` SHOULD сохраняться.
- Editor MUST показывать источник auto visual понятным пользователю способом, например «Значок приложения», «Эскиз файла», «Иконка сайта» или «Предпросмотр текста», без показа внутреннего cache path.
- Remote web lookup MUST иметь отмену и timeout, MUST NOT использовать пользовательские browser cookies или выполнять scripts страницы. Результат MUST считаться недоверенным image data и проходить ограничения content type, размера и декодирования.
- File picker SHOULD по умолчанию предлагать распространённые безопасно декодируемые image/icon formats; drag-and-drop изображения на preview MAY дублировать выбор через picker.

### 5.3 Commands and menus

- Сгруппированные page/window commands SHOULD использовать `CommandBar` и стандартный overflow.
- Add, Edit, Move, Delete и View/Sort controls MUST иметь стабильное расположение.
- Icon-only controls MUST иметь accessible name и tooltip.
- Удаление MUST иметь понятное подтверждение и описание фактического scope.

### 5.4 Search

- Search MUST быть локальным и вторичным.
- Поиск MUST обновлять результаты без отдельной кнопки Apply для дешёвого локального запроса.
- MVP MUST искать как минимум по title, description, URI/path и текстовым metadata, но SHOULD исключать секреты и потенциально чувствительный command content из previews.
- Очистка search MUST возвращать пользователя в прежний collection context.
- Search result MUST показывать визуал и путь/родительскую коллекцию, чтобы сохранять spatial context.
- MVP MAY использовать SQLite FTS5; обычный индексированный поиск допустим, если он измеримо достаточен на целевом объёме.

### 5.5 Drag-and-drop

Приложение MUST принимать:

- files;
- folders;
- HTTP/HTTPS links;
- text;
- image/bitmap content, если формат доступен через Windows DataPackage.

Поведение:

- Drop target MUST показывать визуальную обратную связь и предполагаемый результат.
- Перед созданием тип MUST определяться детерминированно; неоднозначный drop SHOULD открывать компактный диалог выбора.
- Внутренний drag MUST поддерживать reorder и move между коллекциями.
- Drop MUST NOT выполнять полученный файл/команду автоматически.
- Импорт assets MUST быть атомарным с созданием записи либо корректно очищать orphaned files после сбоя.

### 5.6 Clipboard

- `TextSnippet` MUST уметь копировать текст в Windows clipboard.
- `ImageSnippet` SHOULD уметь копировать bitmap и/или file reference в совместимых форматах.
- Clipboard service MUST быть platform abstraction и вызываться из action implementation.
- UI MUST давать краткую ненавязчивую обратную связь об успешном копировании или ошибке.

### 5.7 Accessibility and localization readiness

- Основные сценарии MUST полностью выполняться с клавиатуры.
- Tab order, arrow navigation, Enter, Space, Escape и context-menu key MUST соответствовать Windows expectations.
- Focus state MUST быть видимым; focus MUST NOT теряться после удаления/перемещения элемента.
- Meaningful elements MUST иметь AutomationProperties.Name и при необходимости HelpText.
- Все пользовательские строки MUST находиться в локализуемых resources, даже если MVP поставляется на одном языке.
- Layout SHOULD выдерживать увеличение текста и расширение строк; high contrast MUST сохранять границы и состояния.

## 6. Resident process, activation и hotkey

- Приложение MUST быть single-instance.
- Повторная activation MUST перенаправляться в основной экземпляр через Windows App SDK AppLifecycle или эквивалентный поддерживаемый механизм.
- Hotkey registration MUST быть инкапсулирован в `IGlobalHotkeyService`.
- Значение hotkey MUST быть пользовательской настройкой с безопасным default и обработкой конфликта регистрации.
- Show flow MUST: восстановить окно при необходимости, показать его, вывести на foreground поддерживаемым способом и восстановить focus в разумную точку.
- Hide flow MUST не уничтожать loaded domain state без необходимости.
- Explicit Exit MUST снять hotkey, корректно завершить фоновые операции, flush settings и закрыть БД.
- Startup impact MUST быть минимальным: database migrations и загрузка первого экрана не должны блокировать UI thread.
- Автозапуск с Windows MAY быть добавлен после MVP; он MUST быть opt-in.

## 7. Persistence: SQLite + filesystem assets

### 7.1 Storage responsibilities

SQLite MUST хранить:

- collections;
- resources и typed payload data;
- membership/placement и ordering;
- action configuration/overrides, если они пользовательские;
- asset metadata;
- settings, необходимые для согласованности данных;
- schema migration history.

Filesystem MUST хранить:

- thumbnails;
- imported image snippets;
- пользовательские icons/covers;
- другие бинарные assets.

SQLite MUST NOT использоваться как безразмерное хранилище крупных image blobs в MVP.

### 7.2 Data integrity

- Схема MUST иметь foreign keys, indexes и constraints для основных инвариантов.
- Foreign key enforcement MUST явно включаться для каждого connection.
- Все timestamps MUST храниться в UTC.
- Миграции MUST быть версионированы, детерминированы и выполняться до доступа к repository.
- App MUST NOT молча удалять/пересоздавать пользовательскую БД при ошибке migration.
- Writes, затрагивающие несколько таблиц или БД + filesystem, MUST использовать transaction/compensation strategy.
- Asset filenames MUST генерироваться приложением; оригинальное имя MAY храниться только как metadata.
- Paths к пользовательским File/Folder/Application ресурсам MUST храниться как данные пользователя; внутренние assets MUST адресоваться относительно app data root.
- Repository layer MUST поддерживать cancellation и не выполнять I/O на UI thread.
- Backup/export SHOULD проектироваться как возможное расширение, но полноценная синхронизация не входит в MVP.

### 7.3 Suggested logical schema

Минимальная логическая схема SHOULD включать:

```text
Collections(Id, Title, Description, VisualJson, CreatedAtUtc, UpdatedAtUtc)
Resources(Id, Type, Title, Description, PayloadJson, VisualJson, CreatedAtUtc, UpdatedAtUtc)
Placements(Id, ParentCollectionId, ChildCollectionId?, ResourceId?, SortKey, CreatedAtUtc)
Assets(Id, Kind, RelativePath, ContentHash, MimeType, Width?, Height?, CreatedAtUtc)
Settings(Key, ValueJson, UpdatedAtUtc)
SchemaMigrations(Version, AppliedAtUtc)
```

`Placements` MUST гарантировать, что задан ровно один из `ChildCollectionId` или `ResourceId`. Реализация MAY нормализовать typed payload в отдельные таблицы, если это улучшает ограничения или запросы; выбранный подход MUST оставаться мигрируемым и тестируемым.

## 8. Thumbnail and icon pipeline

- Icon/thumbnail extraction MUST происходить асинхронно и вне UI thread.
- Карточка MUST появляться сразу с placeholder/fallback; visual MAY обновиться позднее.
- Pipeline MUST иметь memory cache и bounded disk cache либо стабильное asset storage.
- Cache key SHOULD учитывать источник и сигналы изменения (например, normalized path/URI, size, last-write time или content hash).
- Одновременные запросы одного visual SHOULD дедуплицироваться.
- Отмена загрузки off-screen item SHOULD поддерживаться там, где это практически полезно.
- Повреждённый image MUST обрабатываться как recoverable error.
- Web favicon retrieval MUST поддерживаться в MVP как неблокирующее auto enhancement; web page preview/thumbnail retrieval SHOULD поддерживаться при доступной metadata, но MUST NOT требовать внешнего thumbnail service. Пользовательский image/icon и deterministic fallback MUST работать без сети.
- File/Application/Folder icons MUST извлекаться через Windows shell API за platform abstraction, если target существует и shell предоставляет visual; отсутствие результата MUST считаться штатным fallback case.
- ImageSnippet preview и импортированный user visual MUST храниться как durable app-owned assets, а извлечённые shell/web previews MAY быть воспроизводимым bounded cache.
- Pipeline MUST проверять MIME/signature, предельный размер файла, предельные dimensions и decode result до публикации asset в UI.
- Decoding SHOULD учитывать EXIF orientation; animated images MUST отображаться как статичный первый безопасно декодированный frame в MVP, если отдельная поддержка анимации не принята явно.
- Auto visual MUST инвалидироваться при изменении fingerprint источника; пользовательский `UserAsset` MUST оставаться неизменным, пока пользователь явно не заменит или не сбросит его.
- Negative cache для временных ошибок MUST иметь ограниченный срок, чтобы команда повторной загрузки могла получить появившийся visual.
- Сгенерированные previews MUST иметь ограниченный размер и MUST NOT декодироваться в полном разрешении для маленькой карточки.

## 9. Architecture

### 9.1 Architectural style

Решение MUST следовать pragmatic layered/clean architecture без избыточной церемонии:

```text
WinUI Presentation
       ↓
Application / Use Cases
       ↓
Domain
       ↑
Infrastructure (SQLite, filesystem, Windows adapters)
```

Dependency direction MUST быть направлен к Domain/Application. Domain MUST NOT зависеть от WinUI, SQLite, Windows App SDK или конкретного DI container.

### 9.2 Required boundaries

Минимальные abstractions SHOULD включать:

- `IResourceRepository` / `ICollectionRepository` либо согласованный Unit of Work;
- `IResourceActionProvider` и `IActionDispatcher`;
- `IAssetStore`;
- `IThumbnailService` / `IIconService`;
- `IClipboardService`;
- `IShellLauncher`;
- `IGlobalHotkeyService`;
- `IWindowVisibilityService`;
- `INavigationService`;
- `IClock` для тестируемых timestamps;
- `IDialogService` или presentation-owned confirmation abstraction.

### 9.3 MVVM and UI state

- MVVM SHOULD использоваться для screen state, commands и async loading.
- CommunityToolkit.Mvvm MAY использоваться, если dependency явно зафиксирована и снижает boilerplate.
- ViewModels MUST NOT напрямую открывать SQLite connections, обращаться к filesystem или Windows shell.
- Code-behind MAY содержать только view-specific wiring, lifecycle и взаимодействия, которые естественно зависят от XAML control instance.
- Strongly typed `x:Bind` SHOULD использоваться там, где lifetime и тип известны; dynamic `Binding` допустим для DataTemplate/DataContext scenarios.
- UI state MUST различать loading, empty, content и error states.
- Long-running commands MUST предотвращать accidental double execution и корректно отражать progress/error.

### 9.4 Error handling and observability

- Ожидаемые domain/application ошибки MUST быть типизированы, а не распознаваться по тексту exception.
- Ошибки запуска ресурса, clipboard, missing path и thumbnail MUST отображаться пользователю без падения приложения.
- Unexpected exceptions MUST логироваться локально с sanitization чувствительных payload.
- Логи MUST NOT по умолчанию содержать полный TextSnippet, clipboard content или секретные command arguments.
- App-level unhandled exceptions MUST быть зафиксированы; продолжение после corrupted state запрещено.

### 9.5 Security constraints

- External URI MUST валидироваться; MVP MUST автоматически открывать только явно поддержанные schemes.
- Commands MUST запускаться без elevation по умолчанию.
- Приложение MUST NOT обходить Windows security prompts или помечать скачанные файлы доверенными.
- Imported filenames и paths MUST нормализоваться и проверяться при записи во внутреннее asset storage.
- Отображаемые metadata MUST трактоваться как данные, а не XAML/format instructions.

## 10. Deployment model

- Начальная реализация SHOULD использовать **packaged** WinUI 3 app как стандартный, Store-friendly путь с package identity.
- Если обязательным становится прямой agent-driven запуск `.exe` без deployment, решение MAY перейти на **unpackaged**, но изменение MUST быть оформлено архитектурным решением и учесть Windows App SDK bootstrap/runtime initialization, storage и activation differences.
- Код MUST NOT неявно смешивать packaged и unpackaged assumptions.
- Конкретные версии .NET, Windows App SDK и NuGet packages MUST быть централизованы и закреплены на поддерживаемых стабильных версиях на момент bootstrap проекта.
- Минимальная поддерживаемая Windows version MUST быть явно задана в проекте и README; baseline MUST соответствовать требованиям выбранной версии Windows App SDK.

## 11. Suggested solution structure

```text
PersonalResourceWorkspace.slnx
Directory.Build.props
Directory.Packages.props
src/
  PersonalResourceWorkspace.App/             # WinUI 3 startup, XAML, shell
    App.xaml
    MainWindow.xaml
    Pages/
    Controls/
    ViewModels/
    Styles/
    Converters/
    Resources/
    Assets/
  PersonalResourceWorkspace.Domain/          # Entities, value objects, invariants
    Resources/
    Collections/
    Actions/
  PersonalResourceWorkspace.Application/     # Use cases, ports, DTOs
    Abstractions/
    Resources/
    Collections/
    Search/
  PersonalResourceWorkspace.Infrastructure/  # SQLite, filesystem, migrations
    Persistence/
    Assets/
    Search/
  PersonalResourceWorkspace.Windows/         # Hotkey, shell, clipboard, icons
    Activation/
    Clipboard/
    Hotkeys/
    Shell/
    Thumbnails/
tests/
  PersonalResourceWorkspace.Domain.Tests/
  PersonalResourceWorkspace.Application.Tests/
  PersonalResourceWorkspace.Infrastructure.Tests/
  PersonalResourceWorkspace.App.Tests/       # только реально поддерживаемые UI tests
docs/
  adr/
```

Допускается объединить `Infrastructure` и `Windows` на раннем MVP, если границы namespaces/interfaces сохранены. Domain и Application MUST оставаться тестируемыми без запуска WinUI.

## 12. Coding conventions

- Использовать latest stable C# language version, совместимую с выбранным .NET SDK.
- Nullable reference types MUST быть включены.
- Implicit global usings MAY быть включены; project-specific global usings MUST быть умеренными.
- Public APIs MUST иметь ясные English names; UI strings — через resource files.
- Async methods MUST иметь suffix `Async`, принимать `CancellationToken` на I/O boundaries и не использовать `.Result`/`.Wait()`.
- `async void` MUST использоваться только для UI event handlers.
- `ConfigureAwait(false)` SHOULD использоваться в library/infrastructure code там, где не требуется UI context.
- Records/value objects SHOULD использоваться для immutable domain data; mutable entity state MUST изменяться через методы, сохраняющие invariants.
- Domain IDs MUST быть strong types или последовательно используемые `Guid`; смешение разных IDs запрещено.
- Не использовать service locator, static mutable global state или singleton ViewModels как скрытое хранилище данных.
- DI registrations MUST находиться в одном composition root.
- SQL MUST быть parameterized. String concatenation пользовательских данных в SQL запрещена.
- `IDisposable`/`IAsyncDisposable` resources MUST корректно освобождаться.
- Analyzer warnings SHOULD рассматриваться как ошибки в CI; намеренные suppression MUST иметь объяснение.
- Форматирование MUST обеспечиваться `.editorconfig` и `dotnet format`-совместимыми правилами.
- Комментарии SHOULD объяснять причину или ограничение, а не пересказывать код.

## 13. Performance targets

На reference development machine после warm resident start:

- Hotkey-to-visible-window SHOULD занимать ≤ 200 ms на p95; MUST не иметь заметной многосекундной паузы.
- Первый полезный экран cold start SHOULD появляться ≤ 1.5 s на p95 без ожидания полного thumbnail pipeline.
- Scroll коллекции из 1,000 карточек SHOULD оставаться отзывчивым благодаря virtualization/lazy visual loading.
- Поиск по 10,000 локальным ресурсам SHOULD обновлять результаты ≤ 150 ms после debounce на p95.
- CRUD action SHOULD давать visible feedback ≤ 100 ms, даже если durable write завершается асинхронно; optimistic updates допустимы только с rollback при ошибке.
- UI thread MUST NOT выполнять filesystem scans, image decoding полного размера, network access или SQLite migrations.

Цифры являются целями MVP и MUST измеряться release build; если среда не позволяет подтвердить абсолютные значения, агент MUST предоставить измерения и условия, а не заявлять соответствие без данных.

## 14. Testing strategy

### 14.1 Unit tests MUST cover

- action ordering и выбор first enabled default action;
- validation каждого resource payload;
- запрет collection cycles;
- placement ordering/reorder;
- URI/path normalization rules;
- command execution policy без фактического запуска shell;
- missing/corrupt visual fallback selection.
- приоритеты resolver, семантику `Auto`/`UserAsset`/`TypeFallback` и type-specific policy для всех восьми resource types;
- сохранение `UserAsset` при изменении обычных metadata и инвалидацию auto visual при изменении source fingerprint;
- выбор `UniformToFill` для cover и `Uniform` для icon без зависимости Domain/Application tests от WinUI types.

### 14.2 Integration tests MUST cover

- создание БД с нуля и последовательное применение migrations;
- foreign keys и constraints;
- round-trip всех resource types;
- транзакционное create/move/delete collection tree;
- asset import и cleanup после simulated failure;
- round-trip `VisualDescriptor` и asset metadata для всех visual modes;
- replace/reset visual, reference-safe cleanup shared asset и восстановление fallback после missing/corrupt asset;
- cache invalidation после изменения file/application path, web URI и ImageSnippet payload;
- cache invalidation и повторное извлечение icon после изменения target `.lnk` или версии алгоритма нормализации shell visual;
- search correctness;
- persistence порядка после restart/reopen repository.

Integration tests MUST использовать временную отдельную directory/database и MUST NOT обращаться к реальным пользовательским данным.

### 14.3 UI/manual verification MUST cover

- actual app launch с рабочим top-level window;
- single-instance activation;
- register/show/hide hotkey и conflict handling;
- mouse, keyboard и context-menu execution default/secondary actions;
- drag/drop внешних files, folders, URL, text, image;
- internal reorder/move;
- wide, medium и narrow window states;
- light, dark, high contrast и text scaling;
- empty/loading/error states;
- missing target file и corrupted thumbnail;
- executable с маленькой icon внутри большого прозрачного/полупрозрачного canvas и цветная icon, совпадающая с type accent: icon остаётся крупной и различимой в light/dark/high-contrast themes;
- `.lnk` на обычное desktop-приложение показывает icon целевого приложения без запуска target и без generic shortcut icon;
- создание и редактирование каждого из восьми типов: auto visual, пользовательский image/icon, принудительный fallback и восстановление после restart;
- одинаковую визуальную идентичность ресурса в grid/list и wide/medium/narrow layouts без layout shift после async загрузки;
- отмену create/edit во время visual import или web lookup без orphaned asset и без изменения сохранённой карточки;
- explicit Exit и повторный запуск.

## 15. MVP boundary

### 15.1 Included in MVP

- single local workspace и root collection;
- unlimited nested collections с cycle prevention;
- все восемь resource types;
- visual cards, local icons/user images и fallbacks;
- ordered actions и default action semantics;
- CRUD, move, reorder, drag-and-drop;
- clipboard actions для snippets;
- local search;
- SQLite persistence и filesystem assets;
- single-instance resident process, configurable global hotkey, show/hide и explicit Exit;
- accessible keyboard flow, themes и responsive window layout;
- migrations, logging и automated domain/persistence tests.

### 15.2 Non-goals for MVP

MVP MUST NOT включать:

- cloud sync, accounts или collaboration;
- browser extension;
- mobile/macOS/Linux clients;
- plugin marketplace или public scripting API;
- automatic crawling/indexing всего диска или browser history;
- AI classification/tagging/recommendations;
- multi-user permissions;
- rich text/markdown editor для snippets;
- remote command execution;
- elevated command broker;
- full web-page archiving;
- complex tag taxonomy, smart collections или rule engine;
- multiple detachable windows;
- обязательную web-thumbnail service.

Архитектура SHOULD не блокировать будущие import/export, sync и новые resource/action types, но MUST NOT реализовывать speculative abstractions ради этих non-goals.

## 16. Implementation phases

### Phase 0 — Foundation and decisions

- Проверить toolchain официальным WinUI setup flow.
- Зафиксировать packaged deployment model, target Windows, .NET и Windows App SDK versions.
- Создать решение из актуального официального WinUI template.
- Добавить project references, DI composition root, logging, tests, `.editorconfig` и ADR для storage/deployment.

**Exit:** clean build; приложение реально запускается и показывает пустое главное окно; test runner работает.

### Phase 1 — Domain and persistence vertical slice

- Реализовать Resource, Collection, Placement, typed payloads и action contracts.
- Реализовать SQLite connection factory, migrations, repositories и filesystem asset store.
- Реализовать root collection bootstrap и CRUD use cases.

**Exit:** все типы проходят round-trip tests; invariants и migration tests зелёные; пользовательская БД не пересоздаётся молча.

### Phase 2 — Visual collection workspace

- Создать MainWindow shell, collection navigation, breadcrumb и responsive collection surface.
- Реализовать create/edit/delete dialogs, empty/error/loading states.
- Добавить card templates, единый visual editor с preview, fallback visuals, light/dark/high-contrast resources.

**Exit:** пользователь создаёт вложенные коллекции и ресурсы, перемещается по дереву и после restart видит те же данные.

### Phase 3 — Actions and Windows integrations

- Реализовать action providers/dispatcher для восьми типов.
- Добавить shell launch, browser/file/folder open, clipboard и safe command execution.
- Добавить context menus, default action, result feedback и error mapping.

**Exit:** default/secondary actions детерминированы и доступны мышью/клавиатурой; ошибки не завершают приложение.

### Phase 4 — Drag/drop and visual pipeline

- Реализовать external imports, internal reorder/move и cycle checks.
- Реализовать type-specific visual policies, icon/favicon extraction, image import, thumbnail/generated preview, cache, invalidation и async UI updates.

**Exit:** поддержанные drop formats работают; порядок сохраняется; slow/corrupt visual не блокирует или не ломает surface.

### Phase 5 — Resident lifecycle and search

- Реализовать single-instance activation, global hotkey, show/hide, close-to-resident и explicit Exit.
- Реализовать settings и local secondary search.
- Восстанавливать window placement и last collection.

**Exit:** повторная activation не создаёт второй workspace; hotkey стабильно управляет окном; поиск и restart проходят acceptance scenarios.

### Phase 6 — Hardening and release candidate

- Выполнить accessibility, localization-readiness, responsive и theme passes.
- Измерить startup, hotkey latency, search и large-collection behavior.
- Исправить diagnostics, recovery paths, migration safety и deployment packaging.
- Подготовить README с build/run/data-location/backup guidance.

**Exit:** выполнены все MVP acceptance criteria и Definition of Done.

## 17. Acceptance criteria

MVP принимается только если все следующие утверждения истинны:

1. На чистом профиле приложение создаёт schema и root collection, показывает usable window и не требует сеть/аккаунт.
2. Пользователь создаёт цепочку минимум из пяти вложенных коллекций; навигация и restart сохраняют её без повреждений.
3. Попытка переместить родительскую коллекцию в её descendant отклоняется с понятным сообщением и без частичной записи.
4. Каждый из восьми resource types создаётся и редактируется с preview: при доступном type-specific источнике отображается auto image/icon, пользователь может установить собственный image/icon или принудительный type fallback; выбор сохраняется после restart.
   - Для `Application`, `File`, `Folder` и executable-based `Command` доступный Windows shell visual отображается без запуска target; для `ImageSnippet` отображается preview содержимого; для `TextSnippet` — безопасный generated preview или text fallback.
   - Для `Web` доступный favicon загружается асинхронно, а доступная preview metadata SHOULD давать cover; отсутствие сети или ошибка remote image не блокируют сохранение и приводят к сохранённому visual либо type fallback.
   - Missing/corrupt visual и смена source в режиме `Auto` корректно инвалидируют cache и проходят цепочку fallback; пользовательский visual не заменяется автоматически.
5. Actions сортируются детерминированно; Enter/click выполняет первое enabled действие, а context menu показывает тот же порядок.
6. TextSnippet копируется в clipboard; Application/Web/File/Folder открываются через ожидаемую Windows-интеграцию; Command следует safe execution policy.
7. External drag-and-drop создаёт корректный resource для file, folder, URL, text и поддерживаемого image content; drop никогда сам не запускает resource.
8. Internal drag меняет порядок и перемещает элементы между коллекциями; результат сохраняется после restart.
9. Missing file, invalid URI, failed process launch, occupied hotkey и corrupt image дают recoverable user-facing error и не завершают процесс.
10. Search находит ресурсы по обязательным полям, показывает collection context и после очистки возвращает прежнюю коллекцию.
11. Второй запуск перенаправляет activation первому экземпляру; hotkey показывает/скрывает существующее окно; explicit Exit завершает resident process.
12. UI работает мышью и клавиатурой, имеет видимый focus и usable layout в wide/medium/narrow widths, light/dark/high contrast.
13. SQLite foreign keys включены, migrations протестированы, assets лежат во filesystem по относительным app-owned paths.
14. Domain и integration test suites проходят; solution собирается без ошибок из документированного workflow.
15. Release build реально запускается на заявленной минимальной Windows version/configuration и показывает ожидаемый top-level window.

## 18. Definition of Done for every feature

Фича считается завершённой, когда:

- реализован happy path и ожидаемые error paths;
- соблюдены domain boundaries и нет прямого platform/database access из ViewModel;
- добавлены/обновлены tests пропорционально риску;
- build и относящиеся tests проходят;
- startup-sensitive изменение проверено реальным запуском окна;
- UI проверен keyboard-only, в light/dark и при узкой ширине, если фича визуальная;
- async работа не блокирует UI thread;
- строки локализуемы, действия имеют accessible names;
- persistence change имеет migration и rollback/recovery behavior;
- новые dependencies обоснованы и закреплены централизованно;
- документация обновлена, если изменился build, storage, deployment или пользовательское поведение.

## 19. Mandatory prohibitions summary

Codex MUST NOT:

- превращать приложение в search-first command palette;
- ограничивать коллекции одним уровнем или допускать циклы;
- выбирать default action иначе, чем первое enabled действие в отсортированном списке;
- помещать business logic и SQLite/shell calls в XAML code-behind/ViewModels;
- выполнять I/O, migrations, image decoding или shell work на UI thread;
- автоматически исполнять dropped content;
- запускать commands elevated или через shell без явной настройки/предупреждения;
- хранить большие images в SQLite без отдельного принятого решения;
- падать из-за отсутствующего файла, иконки, thumbnail или clipboard failure;
- добавлять cloud/account/telemetry dependency в core MVP;
- смешивать packaged/unpackaged lifecycle assumptions;
- создавать custom control system, когда стандартный WinUI control решает задачу;
- объявлять успех только по build или PID без проверки рабочего окна и требуемого сценария.
