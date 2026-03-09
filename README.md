# ArcadeShellSelector

ArcadeShellSelector is a Windows arcade shell launcher built with .NET 10 and WinForms.
It includes a companion tool, ArcadeShellConfigurator, to edit `config.json` visually.

## Table Of Contents

1. [Overview](#overview)
2. [Main Features](#main-features)
3. [Technology Stack](#technology-stack)
4. [Requirements](#requirements)
5. [Build And Run](#build-and-run)
6. [Deploy And Packaging](#deploy-and-packaging)
7. [Shell Replacement (Registry)](#shell-replacement-registry)
8. [Configuration](#configuration)
9. [Architecture](#architecture)
10. [Project Structure](#project-structure)
11. [Troubleshooting](#troubleshooting)
12. [Repository Standards](#repository-standards)

## Overview

The solution contains two desktop applications:

- `ArcadeShellSelector`: full-screen launcher for arcade frontends and tools.
- `ArcadeShellConfigurator`: GUI editor for launcher settings and app options.

Core goals:

- Fast and controller-friendly arcade frontend selection.
- Rich media presentation (video background, tracker music, thumbnail previews).
- Easy operations with a dedicated configurator and one-command packaging script.

## Main Features

### ArcadeShellSelector (main app)

- WinForms full-screen launcher for configured app options.
- Video background playback using LibVLC.
- Tracker music playback (MOD/XM) with configurable volume and output device.
- Real-time spectrum analyzer overlay using WASAPI loopback.
- Gamepad support:
  - XInput polling for Xbox-compatible controllers.
  - DirectInput support for arcade encoders and joysticks.
- Hover/select thumbnail video preview rendered via LibVLC software callbacks.
- Optional logging for diagnostics.
- Network path wait logic for UNC executable paths.
- Shell-friendly behavior (starts `explorer.exe` on exit when needed).
- Optional LedBlinky integration for arcade LED feedback.

### ArcadeShellConfigurator (companion app)

- Tabbed editor for all config sections:
  - General
  - Paths
  - Music
  - App Options
- Editable option grid with browse helpers for executables and images.
- Video thumbnail and background preview support.
- Dirty tracking to avoid accidental loss of unsaved edits.
- Multi-destination `config.json` synchronization.
- Launch button to start the main launcher directly.

## Technology Stack

- .NET: `net10.0-windows`
- UI: WinForms
- Video: `LibVLCSharp`, `LibVLCSharp.WinForms`, `VideoLAN.LibVLC.Windows`
- Audio: `NAudio`
- Input: `SharpDX.XInput`, `SharpDX.DirectInput`

## Requirements

- Windows 10/11
- .NET 10 SDK for development
- PowerShell 7+ for deployment script (`publish.ps1`)

Runtime requirements depend on packaging mode:

- Framework-dependent package: target machine needs .NET 10 runtime.
- Self-contained package: no preinstalled .NET runtime required.

## Build And Run

### Restore

```powershell
dotnet restore ArcadeShellSelector.sln
```

### Build (Debug)

```powershell
dotnet build ArcadeShellSelector.sln -c Debug
```

### Build (Release)

```powershell
dotnet build ArcadeShellSelector.sln -c Release
```

### Run

Use Visual Studio, or run from output folders after build.

- Main app output: `bin\<Configuration>\net10.0-windows\`
- Configurator output is copied to the main app output by project targets.

## Deploy And Packaging

Use `publish.ps1` from repo root.

### Framework-dependent package (default)

```powershell
pwsh .\publish.ps1
```

### Self-contained package

```powershell
pwsh .\publish.ps1 -SelfContained
```

### Useful options

- Skip build and package existing artifacts:

```powershell
pwsh .\publish.ps1 -SkipBuild
```

- Strip `.pdb` symbols from deploy folder:

```powershell
pwsh .\publish.ps1 -StripPdb
```

- Set explicit package version:

```powershell
pwsh .\publish.ps1 -Version "1.0.0"
```

Packaging output:

- Deploy folder: `deploy\ArcadeShell\`
- Zip artifact: `deploy\ArcadeShell-v<version>-win-x64.zip`

## Shell Replacement (Registry)

To replace Windows Explorer with `ArcadeShellSelector.exe`, the shell value in Winlogon must point to your launcher executable.

Important:

- Use an absolute path to `ArcadeShellSelector.exe`.
- Test with a non-admin/service account first.
- Keep a recovery path available (safe mode, remote access, or admin account).

### Mandatory registry value

Set `Shell` under Winlogon:

- Machine-wide: `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`

Command (run in elevated PowerShell):

```powershell
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name Shell -Value "C:\ArcadeShell\ArcadeShellSelector.exe"
```

### Backup before change

```powershell
reg export "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" "C:\ArcadeShell\winlogon-backup.reg" /y
```

### Rollback to default Explorer shell

```powershell
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name Shell -Value "explorer.exe"
```

### Optional per-user shell override

If you want to scope shell replacement to a specific user profile, you can set the same value under:

- `HKCU\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon`

```powershell
Set-ItemProperty -Path "HKCU:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon" -Name Shell -Value "C:\ArcadeShell\ArcadeShellSelector.exe"
```

Note: per-user shell behavior can vary by Windows policy and environment. For dedicated cabinets, machine-wide `HKLM` is the typical setup.

## Configuration

Primary config file:

- `config.json`

Important sections (high-level):

- UI settings (title, top-most, layout ratios)
- Path settings (tools root, images root, video background, network wait)
- Options list (label, exe, image, optional thumb video, wait behavior)
- Music settings (enabled, root, file list, volume, audio device)
- Debug/logging switch

Recommended workflow:

- Use ArcadeShellConfigurator for regular edits.
- Keep paths valid and verify executable availability before deployment.

## Architecture

### High-level component view

```text
+-------------------------+      reads/writes      +----------------+
| ArcadeShellConfigurator | <--------------------> |  config.json   |
+------------+------------+                        +--------+-------+
             |                                              |
             | launch app                                   | load at startup
             v                                              v
+------------+----------------------------------------------+------------+
|                         ArcadeShellSelector                            |
|  - UI and app selection                                                |
|  - Process launch and wait                                             |
|  - Video background / thumbnail preview                                |
|  - Music and spectrum analyzer                                         |
|  - Controller input (XInput + DirectInput)                            |
+-----------+---------------------------+-------------------+------------+
            |                           |                   |
            v                           v                   v
      +-----------+               +-----------+       +------------+
      |  LibVLC   |               |  NAudio   |       |  SharpDX   |
      +-----------+               +-----------+       +------------+
```

### Core runtime flow

```text
App start
  -> Program.cs creates Launcher
  -> Launcher loads AppConfig
  -> UI layout and media services initialize
  -> Input loop polls keyboard/gamepad
  -> User selects option
  -> Process starts (optional wait-for-exit)
  -> On return, launcher state and media resume
  -> App exit disposes media/input resources safely
```

### Key internal modules

- `Launcher.cs`: primary UI lifecycle, selection logic, app launch flow.
- `AppConfig.cs`: config model and file loading/validation.
- `VideoBackground.cs`: background video management.
- `MusicPlayer.cs`: tracker and audio playback logic.
- `SpectrumAnalyzer.cs`, `SpectrumPanel.cs`: audio visualization.
- `LibVlcManager.cs`: LibVLC lifecycle and shared concerns.
- `LedBlinky.cs`: optional hardware LED integration.
- `TrackerMetadata.cs`: tracker file metadata support.

## Project Structure

```text
ArcadeShellSelector.sln
|-- ArcadeShellSelector.csproj
|-- ArcadeShellConfigurator/
|   |-- ArcadeShellConfigurator.csproj
|   |-- ConfigForm.cs
|-- Program.cs
|-- Launcher.cs
|-- AppConfig.cs
|-- MusicPlayer.cs
|-- VideoBackground.cs
|-- SpectrumAnalyzer.cs
|-- SpectrumPanel.cs
|-- LedBlinky.cs
|-- TrackerMetadata.cs
|-- config.json
|-- publish.ps1
|-- Media/
|-- .github/
```

## Troubleshooting

- Configurator does not launch:
  - Verify `ArcadeShellConfigurator.exe`, `.dll`, `.runtimeconfig.json`, and `.deps.json` are present.
- No video playback:
  - Verify `libvlc\win-x64\libvlc.dll` exists in deployment output.
- No music or spectrum:
  - Verify music settings and selected audio output device.
- App option fails to launch:
  - Verify target executable path, permissions, and network availability (if UNC path).

## Repository Standards

Repository governance and collaboration files:

- License: `LICENSE`
- Changelog: `CHANGELOG.md`
- Contributing guide: `CONTRIBUTING.md`
- Security policy: `SECURITY.md`
- Code of conduct: `CODE_OF_CONDUCT.md`
- Issue templates: `.github/ISSUE_TEMPLATE/`
- Pull request template: `.github/PULL_REQUEST_TEMPLATE.md`

CI workflow:

- `.github/workflows/dotnet-desktop.yml`

Feature overview page:

- `features.html`
