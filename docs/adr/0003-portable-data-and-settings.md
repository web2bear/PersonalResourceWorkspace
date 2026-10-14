# ADR 0003: Portable data directory and database-backed settings

- Status: Accepted and implemented
- Date: 2026-10-14
- Supersedes: the storage-location decisions in ADR 0001 and ADR 0002

## Context

The current unpackaged app stores its database and visuals under `%LOCALAPPDATA%/PersonalResourceWorkspace`. Its grid/list preference is a separate text file, and the Settings navigation item is a placeholder. A portable archive needs a movable, self-contained default data location and one authoritative store for user preferences.

## Decision

The default data root is `content` beside the executable, resolved from `AppContext.BaseDirectory`. The database and all app-owned content live under this root. A user may select another data root in Settings. The only persistent bootstrap configuration outside the database is `portable-config.json` beside the executable, with `dataDirectory` as its sole user-configurable value.

At startup, the effective data root is resolved in this order: `--data-dir <path>`, `PERSONAL_RESOURCE_WORKSPACE_DATA_DIR`, `portable-config.json`, then the default `content`. Relative paths are resolved against the executable directory. An invalid explicit path is reported and never silently replaced with a different workspace. Settings shows the effective root and its source; it can save a new path to JSON, but cannot alter CLI or environment overrides.

Every other user preference is stored in SQLite and exposed in the Settings form when introduced. The existing grid/list preference is migrated from `Settings/collection-view.txt`; the sort preference becomes durable in SQLite. The database schema and migration process own preference defaults and upgrades.

Changing the data root copies a consistent database snapshot and all app-owned content to a validated destination before switching. The old root is retained. Existing target data is never silently overwritten or merged. On first startup after upgrading, an empty selected root receives an automatic copy from the legacy `%LOCALAPPDATA%` root; a nonempty destination requires an explicit user choice. The source is retained. The app must not open a partially copied database, and failed migration must leave the old workspace usable.

## Consequences

Portable operation requires write access to the selected data root and, when changing the saved location, to the directory containing `portable-config.json`. SQLite connection creation, visual storage, migrations, and startup must use one resolved root. Tests must cover source precedence, relative paths, legacy migration, preference migration, successful relocation, conflicts, and rollback on failure.

ADR 0001 still governs unpackaged WinUI deployment. ADR 0002 still governs SQLite integrity and filesystem assets, except for its former packaged/local-profile storage location. The existing v0.1.1 release predates this decision; current source builds use the portable location.
