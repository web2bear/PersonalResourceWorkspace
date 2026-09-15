# ADR 0002: SQLite metadata and filesystem assets

- Status: Accepted
- Date: 2026-09-10

## Context

The application is local-first and must preserve structured metadata safely while keeping large binary images outside the database.

## Decision

Store relational metadata and migration history in SQLite under the packaged local-data root. Store imported images, thumbnails, and icons as app-owned filesystem assets referenced by relative logical paths. Enable SQLite foreign keys on every connection. Apply ordered migrations transactionally before repository access, and never repair a failed migration by deleting or recreating the user's database.

The implemented schema stores resource visual mode, resolved kind, origin, source fingerprint, and relative asset path in SQLite. Collection images use the same relative asset-addressing rule. Durable user-selected images are copied to `Data/Visuals/user`; reproducible shell and web visuals are stored under `Data/Visuals/cache`. Migrations 4 and 5 add these fields to existing profiles without recreating the database.

## Consequences

Database and asset changes spanning both stores will require an explicit transaction/compensation strategy. Integration tests use isolated temporary roots. Backup and export can later include one database plus its asset tree without introducing cloud dependencies.

Remote web discovery is an optional, bounded enhancement: it uses no browser cookies, does not execute page scripts, and falls back to the type glyph when metadata, network access, or image decoding fails. User-selected assets always take precedence over automatic refresh.
