# Personal Resource Workspace

**All your resources. One space.** A local Windows app that brings links, files, folders, applications, and snippets together in a visual catalog.

[Русский](../README.md) · [Build and run](development.md) · [Architecture](architecture.md)

![Personal Resource Workspace app overview](../assets/personal-resource-workspace.png)

## What works today

- **One place for your resources:** save web links, files, folders, applications, text and image snippets, and commands. Your catalog persists locally across restarts.
- **Visual browsing:** recognize cards by their covers, icons, and previews, or switch to a compact list.
- **Collections and favorites:** create collections, choose their color and icon, mark resources as favorites, and see everything in one view.
- **Find and open quickly:** search the catalog, sort by title or modification date, open resources from their cards, and edit them through a context menu.
- **Easy capture:** drop files, folders, or web links into the window. Creating a resource can also prefill it from the clipboard.
- **Trash:** deleted resources first go to Trash, which you can empty separately.

The app is under active development. The image shows one example workspace; your content will differ.

## Try it

Download the [v0.1.1 prerelease for Windows x64](https://github.com/web2bear/PersonalResourceWorkspace/releases/tag/v0.1.1), extract the entire ZIP, and run `PersonalResourceWorkspace.App.exe`. This is a portable build without an installer or digital signature. You can also [build from source](development.md).

## Ideas for the future

These are **possibilities, not shipped features or delivery commitments**:

- nested collections and easier movement between them;
- manual card ordering and more navigation options;
- a global keyboard shortcut to bring the workspace forward;
- richer workflows for text and image snippets;
- more appearance and launch settings.

You can share ideas and bug reports in [Issues](https://github.com/web2bear/PersonalResourceWorkspace/issues), if enabled for this repository.

## About the project

The goal is a personal, local space for resources. Core workflows do not require an account or cloud service. For current limitations and implementation details, see [development](development.md), [architecture](architecture.md), and the [long-term specification](../PROJECT.md).
