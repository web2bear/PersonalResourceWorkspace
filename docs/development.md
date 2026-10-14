# Сборка и запуск / Build and run

## Требования / Requirements

- Windows 10 1809 (build 17763) or newer
- .NET SDK 9.0.318 or a compatible 9.0 patch selected by `global.json`
- Windows SDK 10.0.26100 or newer

Готового установщика пока нет. Запускайте приложение из клонированного репозитория на Windows. / There is no installer yet. Run the app from a cloned checkout on Windows.

## Быстрый запуск / Quick start

Запустите `run.bat` из корня репозитория. Он использует существующую Debug x64 сборку или собирает приложение, если исполняемый файл отсутствует. `run.bat --build` принудительно пересобирает проект. / Run `run.bat` from the repository root. It uses the existing Debug x64 build or builds the app if needed. `run.bat --build` forces a rebuild.

## Командная строка / Command line

```powershell
dotnet restore PersonalResourceWorkspace.slnx
dotnet build PersonalResourceWorkspace.slnx -c Debug --no-restore
dotnet test PersonalResourceWorkspace.slnx -c Debug --no-build
dotnet run --project src/PersonalResourceWorkspace.App/PersonalResourceWorkspace.App.csproj -c Debug -r win-x64
```

Данные по умолчанию находятся в `content` рядом с запускаемым `.exe`. Путь можно задать через `--data-dir <path>`, переменную `PERSONAL_RESOURCE_WORKSPACE_DATA_DIR` или поле `dataDirectory` в `portable-config.json` рядом с `.exe`; приоритет именно в этом порядке. Относительные пути считаются от каталога `.exe`. Формой «Настройки» можно скопировать рабочее пространство в другой пустой каталог; для перехода на него перезапустите приложение. Прежние данные сохраняются. / By default, data lives in `content` beside the executable. The CLI argument, environment variable, and JSON config override it in that order. Settings can copy the workspace to another empty directory; restart to use it.

## Диагностика

Приложение пишет структурированные журналы JSONL в `Logs` выбранного каталога данных. Один файл относится к одному процессу и содержит время с часовым поясом, уровень, категорию, идентификатор события, сообщение, параметры, область операции и стек исключения. Файлы ротируются при достижении примерно 5 МБ; сохраняются 20 последних файлов. Запись выполняется сразу, включая ошибки перед завершением процесса. Если запуск прервался до открытия каталога данных, журнал ошибки создаётся в `Logs` рядом с `.exe`.

Уровни выбираются в «Настройки → Диагностика» и применяются после перезапуска. Они хранятся в таблице `Settings` БД: `logging-level` (по умолчанию `Information`) и `logging-preview-level` (по умолчанию `Debug`). `None` отключает соответствующие сообщения.

`Information` фиксирует запуск, выбранные пути и количество загруженных ресурсов. `Debug` для превью фиксирует ResourceId, PreviewAttemptId, режим, кэш, HTTP-статус, MIME-тип, размер ответа, наличие метаданных, результат и длительность. `Warning` объясняет отказ кандидата или переход к запасному отображению; `Error` содержит исключения операций; `Critical` — сбои запуска и необработанные ошибки UI. Содержимое заметок, буфера обмена и HTTP-ответов не записывается; URL запросов записываются без query и fragment.

При исследовании превью найдите ResourceId в журнале: события 200–207 связывают получение изображения с его загрузкой в UI. Декодирование и показ локальных изображений используют поток `System.IO`, чтобы unpackaged-приложение могло читать выбранный каталог данных без разрешений `StorageFile.GetFileFromPathAsync`. Неудачное принудительное обновление сохраняет предыдущую корректную картинку, если её исходные данные не изменились.

Проверка доступа к изображениям на Windows:

```powershell
dotnet test tests/PersonalResourceWorkspace.Windows.Tests/PersonalResourceWorkspace.Windows.Tests.csproj
```

## Выпуск версий / Releases

Процесс подготовки ZIP и GitHub Release описан в [руководстве по выпуску](releasing.md). / See the [release guide](releasing.md) for the ZIP and GitHub Release workflow.
