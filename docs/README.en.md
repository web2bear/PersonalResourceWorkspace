# Personal Resource Workspace

**All your resources. One space.** A local Windows app that brings links, files, folders, applications, and snippets together in a visual catalog.

[Русский](../README.md) · [Build and run](development.md) · [Architecture](architecture.md)

## What works today

- **One place for your resources:** save web links, files, folders, applications, text and image snippets, and commands. Your catalog persists locally across restarts.
- **Visual browsing:** recognize cards by their covers, icons, and previews, or switch to a compact list.
- **Tags and saved searches:** assign several tags to a resource, combine them with AND, and organize named searches in the sidebar tree. The All Resources view shows active resources without extra conditions.
- **Find and open quickly:** search the catalog, sort by title or modification date, open resources from their cards, and edit them through a context menu.
- **Easy capture:** drop files, folders, or web links into the window. Creating a resource can also prefill it from the clipboard.
- **Inbox and Trash:** untagged new resources enter Inbox. Deletion adds the Trash tag while preserving other tags; resources can be restored or permanently removed.

The app is under active development.

## Try it

Download the [v0.1.1 prerelease for Windows x64](https://github.com/web2bear/PersonalResourceWorkspace/releases/tag/v0.1.1), extract the entire ZIP, and run `PersonalResourceWorkspace.App.exe`. This is a portable build without an installer or digital signature. You can also [build from source](development.md).

The release predates this source implementation. On first launch with the new schema, the previous collections database is backed up as `Data/workspace.collections-backup-*.db`, then a new empty workspace is created. Existing collection entries are not imported.

## Ideas for the future

These are **possibilities, not shipped features or delivery commitments**:

- optional visual attributes for tags and a configurable startup search;
- manual card ordering and more navigation options;
- a global keyboard shortcut to bring the workspace forward;
- richer workflows for text and image snippets;
- more appearance and launch settings.

You can share ideas and bug reports in [Issues](https://github.com/web2bear/PersonalResourceWorkspace/issues), if enabled for this repository.

## About the project

The goal is a personal, local space for resources. Core workflows do not require an account or cloud service. For current limitations and implementation details, see [development](development.md), [architecture](architecture.md), and the [long-term specification](../PROJECT.md).
