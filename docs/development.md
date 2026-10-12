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

Данные приложения находятся в `%LOCALAPPDATA%\PersonalResourceWorkspace`; при повторном запуске каталог сохраняется. / App data lives in `%LOCALAPPDATA%\PersonalResourceWorkspace` and persists across launches.
