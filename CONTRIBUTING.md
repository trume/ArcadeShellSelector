# Contributing

Thanks for contributing to ArcadeShellSelector.

## Scope

This repository contains two Windows desktop applications:

- ArcadeShellSelector: the main launcher.
- ArcadeShellConfigurator: the configuration editor.

Please keep changes focused, small, and consistent with the existing WinForms/.NET codebase.

## Prerequisites

- Windows
- .NET 10 SDK
- PowerShell 7+
- Git

## Local Setup

1. Clone the repository.
2. Restore dependencies:
   `dotnet restore ArcadeShellSelector.sln`
3. Build the solution:
   `dotnet build ArcadeShellSelector.sln -c Debug`
4. Run the launcher or configurator from Visual Studio or from the build output.

## Packaging

Use the existing deployment script:

- Framework-dependent package:
  `pwsh ./publish.ps1`
- Self-contained package:
  `pwsh ./publish.ps1 -SelfContained`

## Contribution Guidelines

- Keep pull requests limited to one logical change.
- Prefer fixing root causes instead of adding narrow workarounds.
- Do not reformat unrelated files.
- Preserve current UI behavior unless the change explicitly targets it.
- Update documentation when behavior, setup, or deployment changes.
- Add or update tests if automated tests are introduced for the touched area.

## Pull Requests

Before opening a pull request, make sure:

- The solution builds in Release configuration.
- The app starts without breaking the existing launcher flow.
- Configuration changes still round-trip through `config.json`.
- Any packaging changes were validated with `publish.ps1`.
- Documentation was updated when needed.

## Reporting Bugs

When filing an issue, include:

- Windows version
- .NET runtime/SDK version
- Whether the app was run framework-dependent or self-contained
- Steps to reproduce
- Expected behavior
- Actual behavior
- Relevant logs or screenshots

## Questions

For feature ideas or architecture changes, open an issue first so scope and direction can be discussed before implementation.
