# Personal Resource Workspace

Foundation for a local-first Windows application built with C#, .NET 9, WinUI 3, Windows App SDK 2.4.0, and SQLite.

[`PROJECT.md`](PROJECT.md) is the normative product and technical contract. This repository currently implements Phase 0 only: official WinUI scaffold, architectural boundaries, dependency injection, logging, tests, and a safe migration foundation. Product features belong to later phases.

## Requirements

- Windows 10 version 1809 (build 17763) or newer
- .NET SDK 9.0.318 or a compatible 9.0 patch selected by `global.json`
- Windows SDK 10.0.26100 or newer

The application uses self-contained unpackaged deployment. The audited execution environment cannot register a loose-layout MSIX because its sandbox token has no deployable user-profile type. This model enables a repeatable CLI launch without a machine-wide Windows App Runtime prerequisite; see ADR 0001.

## Build and test

```powershell
dotnet restore PersonalResourceWorkspace.slnx
dotnet build PersonalResourceWorkspace.slnx -c Debug --no-restore
dotnet test PersonalResourceWorkspace.slnx -c Debug --no-build
```

## Run

Double-click `run.bat` in the repository root. It starts the existing Debug x64 build and builds it first when the executable is missing. Use `run.bat --build` to force a rebuild, or use the CLI:

```powershell
dotnet run --project src/PersonalResourceWorkspace.App/PersonalResourceWorkspace.App.csproj -c Debug -r win-x64
```

`dotnet run` launches the self-contained executable directly.

## Structure

- `src/PersonalResourceWorkspace.App` — WinUI startup, XAML, and the DI composition root.
- `src/PersonalResourceWorkspace.Domain` — dependency-free domain boundary.
- `src/PersonalResourceWorkspace.Application` — use-case contracts and ports.
- `src/PersonalResourceWorkspace.Infrastructure` — SQLite, filesystem, migrations, and search adapters.
- `src/PersonalResourceWorkspace.Windows` — Windows-only activation, clipboard, hotkey, shell, and visual adapters.
- `tests` — unit and isolated SQLite integration tests.
- `docs/adr` — durable architecture decisions.

## Local data and migrations

Local data is rooted at `%LOCALAPPDATA%/PersonalResourceWorkspace`. The SQLite database is stored at `Data/workspace.db` below that app-owned root. User-selected resource and collection images are copied to `Data/Visuals/user`; reproducible Windows shell and web previews are cached under `Data/Visuals/cache`. SQLite stores only their relative logical paths and visual-selection metadata.

Startup creates only `SchemaMigrations` and records migration version `1`. Each connection explicitly enables foreign keys. Migrations run transactionally; an error is rolled back and the database is never silently deleted or recreated. Tests use a unique temporary directory and never touch user data.
