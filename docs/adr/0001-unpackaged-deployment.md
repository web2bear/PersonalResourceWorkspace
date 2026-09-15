# ADR 0001: Self-contained unpackaged deployment

- Status: Accepted
- Date: 2026-09-10

## Context

The contract prefers packaged deployment but explicitly allows unpackaged deployment when direct agent-driven executable launch is required. The official packaged template built successfully, but the audited sandbox cannot register a loose-layout MSIX: Windows deployment reports that the current token has no resolvable user-profile type (`0x80073D19`, inner `0x80070002`). Installing the signed Windows App Runtime also failed under the sandbox deployment API (`0x80070570`). Granting access to the package directories did not change the profile-token failure.

## Decision

Retain the current official WinUI template structure but set `WindowsPackageType=None`, disable package-aware WinApp launch support, and use self-contained Windows App SDK deployment. Target `net9.0-windows10.0.26100.0`, set `TargetPlatformMinVersion` to Windows 10 build 17763, and verify locally on x64.

Keep package-identity assumptions out of startup and persistence. The Windows adapter resolves app-owned storage under `%LOCALAPPDATA%/PersonalResourceWorkspace` rather than `ApplicationData.Current`.

## Consequences

The build output is larger, but `dotnet run` and the generated executable are repeatable without MSIX registration or a machine-wide Windows App Runtime. APIs that require package identity must be guarded or replaced. Moving back to packaged deployment requires a superseding ADR plus verification on a normal interactive user token.
