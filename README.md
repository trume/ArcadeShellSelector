# ArcadeShellSelector

ArcadeShellSelector is a Windows arcade shell launcher built with .NET 10 and WinForms.
It includes a companion tool, ArcadeShellConfigurator, to edit `config.json` visually.

## Table Of Contents

1. [Overview](#overview)
2. [Screenshots](#screenshots)
3. [Main Features](#main-features)
4. [Technology Stack](#technology-stack)
5. [Requirements](#requirements)
6. [Build And Run](#build-and-run)
7. [Deploy And Packaging](#deploy-and-packaging)
8. [Shell Replacement (Registry)](#shell-replacement-registry)
9. [Configuration](#configuration)
10. [Architecture](#architecture)
11. [Project Structure](#project-structure)
12. [Troubleshooting](#troubleshooting)
13. [Repository Standards](#repository-standards)

## Overview

The solution contains two desktop applications:

- `ArcadeShellSelector`: full-screen launcher for arcade frontends and tools.
- `ArcadeShellConfigurator`: GUI editor for launcher settings and app options.

Core goals:

- Fast and controller-friendly arcade frontend selection.
- Rich media presentation (video background, tracker music, thumbnail previews).
- Easy operations with a dedicated configurator and one-command packaging script.

## Screenshots

### ArcadeShellSelector — Launcher

![ArcadeShellSelector main launcher](Media/Screenshots/launcher.png)

### ArcadeShellConfigurator — Settings Editor

![ArcadeShellConfigurator settings editor](Media/Screenshots/configurator.png)

## Main Features

### ArcadeShellSelector (main app)

- Full-screen terminal boot animation (`BootSplash`) with CRT effects (scanlines, phosphor tint, vignette), typed line-by-line sequence populated with real system and config data, randomised timing pauses, 11-second cursor pre-phase with looped HDD sound via NAudio, and instant skip on keypress.
- Seamless transition from boot splash to launcher — no visible desktop gap.
- WinForms full-screen launcher for configured app options.
- Video background playback using LibVLC with configurable playback rate.
- Tracker music playback (MOD/XM) with configurable volume and output device.
- Real-time spectrum analyzer overlay using WASAPI loopback with configurable band count (2–32).
- Gamepad support:
  - XInput polling for Xbox-compatible controllers.
  - DirectInput support for arcade encoders and joysticks.
- Hover/select thumbnail video preview rendered via LibVLC software callbacks.
- Structured diagnostic logging with Info/Warn/Error levels, component tags, and automatic 2 MB log rotation.
- Network path wait logic with real-time status feedback for UNC executable paths.
- Shell-friendly behavior (starts `explorer.exe` on exit when needed).
- Optional LedBlinky integration for arcade LED feedback.
- `lib\` assembly probing via `[ModuleInitializer]` + `AssemblyLoadContext` (resilient on .NET 10).
- First-run guard: detects unconfigured state (missing `config.json` or empty options list) and prompts the user to open the Configurator or exit before any UI is shown.
- Configurable boot splash bypass (`arranque.bootSplashEnabled`) to skip the terminal animation and go straight to the launcher.
- Config validation at startup with surfaced warnings for missing paths and out-of-range values.
- LibVLC background warm-up for reduced first-video latency.
- PerMonitorV2 DPI awareness for correct rendering on high-resolution arcade monitors.
- Idle CPU reduction: Z-order timer suspended while child processes run.

### ArcadeShellConfigurator (companion app)

- Tabbed editor for all config sections:
  - General
  - Directorios (paths)
  - Media/Led (music, video, LedBlinky)
  - Controles (input)
  - Log
- **Controles tab** — side-by-side DirectInput and XInput panels, each with:
  - Enable toggle and device/slot selector (auto-updates on plug/unplug).
  - Interactive button binding with animated countdown hint (press to assign; Left/Right accept axis or POV).
  - Live test panel using `InputVisualPanel`: DInput shows stick + POV + button grid; XInput shows L Stick + R Stick + trigger bars + button pill-tags.
- Audio device selection with stable GUID persistence (survives device re-enumeration).
- Editable option grid with browse helpers for executables and images.
- Video thumbnail and background preview support.
- Dirty tracking to avoid accidental loss of unsaved edits.
- Multi-destination `config.json` synchronization.
- Launch button to start the main launcher directly.
- PerMonitorV2 DPI awareness with `AutoScaleMode.Dpi`.
- `lib\` assembly probing via `[ModuleInitializer]` + `AssemblyLoadContext`.

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

The publish script also writes a clean first-run `config.json` into the deploy folder (empty options, blank paths, features disabled) so fresh installations always trigger the first-run guard.

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
- Startup settings (`arranque`): `bootSplashEnabled` — skip or show the terminal boot animation.
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
+----------------------------------------------+      reads/writes      +----------------+
|          ArcadeShellConfigurator              | <--------------------> |  config.json   |
|                                              |                        +--------+-------+
|  Tabs: General | Directorios | Media/Led     |                                 |
|        Controles | Log                       |                                 |
|                                              |                                 |
|  Controls tab                                |                                 |
|  +-----------------------------------------+|                                 |
|  | DirectInput panel  | XInput panel        ||                                 |
|  |  device selector   |  slot list          ||                                 |
|  |  button binding    |  button binding     ||                                 |
|  |  test: Stick+POV   |  test: L+R Stick    ||                                 |
|  |  InputVisualPanel  |  InputVisualPanel   ||                                 |
|  +-----------------------------------------+|                                 |
+----------------------------------------------+                                 |
             |                                                                    |
             | launch app                                                         | load at startup
             v                                                                    v
+------------+--------------------------------------------------------------------+------------+
|                                   ArcadeShellSelector                                       |
|                                                                                             |
|  Program.cs ──→ LibProber [ModuleInitializer]  ──→  AssemblyLoadContext (lib\ probing)     |
|       |                                                                                     |
|       ├──→ BootSplash (full-screen terminal boot animation)                                 |
|       |       NAudio HDD sound loop | CRT effects | typing animation | cursor pre-phase     |
|       |                                                                                     |
|       └──→ Launcher (full-screen app selector)                                              |
|               - Option grid UI                                                              |
|               - Process launch and wait                                                     |
|               - Video background / thumbnail preview                                        |
|               - Tracker+audio music with spectrum analyzer                                  |
|               - Controller input (XInput + DirectInput)                                     |
|               - LedBlinky integration                                                       |
|               - DebugLogger                                                                 |
+--------+---------------------------+---------------------------+---------------------------+
         |                           |                           |
         v                           v                           v
   +-----------+               +-----------+             +------------+
   |  LibVLC   |               |  NAudio   |             |  SharpDX   |
   | (video bg,|               | (music,   |             | .XInput    |
   |  thumbs)  |               |  spectrum,|             | .DirectInput|
   +-----------+               |  sounds)  |             +------------+
                               +-----------+
```

### Core runtime flow

```text
App start
  -> LibProber [ModuleInitializer] registers lib\ assembly resolver
  -> Program.cs loads AppConfig
  -> FirstRunGuard checks config state
       -> If unconfigured: prompt to open Configurator or exit (app never launches unconfigured)
  -> Program.cs pre-creates Launcher (hidden)
  -> BootSplash shown (if arranque.bootSplashEnabled; full-screen terminal animation + HDD sound)
       -> 11-second cursor blink pre-phase (skippable)
       -> Typed boot sequence with real system/config data
       -> FormClosing: Launcher.Show() — both windows alive simultaneously (no desktop flash)
  -> BootSplash closes (or skipped); Application.Run(launcher)
  -> Launcher loads AppConfig, initialises media + input
  -> Input loop polls keyboard/gamepad
  -> User selects option
  -> Process starts (optional wait-for-exit)
  -> On return, launcher state and media resume
  -> App exit disposes media/input resources safely
```

### Key internal modules

- `Program.cs`: entry point; `LibProber [ModuleInitializer]` for `lib\` assembly probing, first-run guard, seamless splash→launcher transition.
- `FirstRunGuard.cs`: detects unconfigured state and shows a standard dialog prompting the user to open the Configurator or exit.
- `BootSplash.cs`: full-screen CRT-style terminal boot animation (Courier New, scanlines, vignette, phosphor tint, random pauses, NAudio HDD sound loop, 11 s cursor pre-phase); skippable via `arranque.bootSplashEnabled`.
- `Launcher.cs`: primary UI lifecycle, selection logic, app launch flow.
- `AppConfig.cs`: config model and file loading/validation.
- `VideoBackground.cs`: background video management via LibVLC.
- `MusicPlayer.cs`: tracker (MOD/XM) and audio playback logic.
- `SpectrumAnalyzer.cs`, `SpectrumPanel.cs`: WASAPI loopback audio visualization.
- `LibVlcManager.cs`: LibVLC lifecycle and shared concerns.
- `LedBlinky.cs`: optional hardware LED integration.
- `TrackerMetadata.cs`: tracker file metadata support.
- `DebugLogger.cs`: structured diagnostic logging with Info/Warn/Error levels, component tags, and automatic log rotation.
- `ArcadeShellConfigurator/ConfigForm.cs`: full configurator UI; DInput and XInput panels with interactive button binding and live `InputVisualPanel` test view.
- `ArcadeShellConfigurator/InputVisualPanel.cs`: custom-drawn panel rendering stick positions, POV hat, trigger bars, and button states for both DInput and XInput modes.

## Project Structure

```text
ArcadeShellSelector.sln
|-- ArcadeShellSelector.csproj
|-- ArcadeShellConfigurator/
|   |-- ArcadeShellConfigurator.csproj
|   |-- ConfigForm.cs
|   |-- InputVisualPanel.cs
|-- Program.cs
|-- FirstRunGuard.cs
|-- BootSplash.cs
|-- Launcher.cs
|-- AppConfig.cs
|-- MusicPlayer.cs
|-- VideoBackground.cs
|-- SpectrumAnalyzer.cs
|-- SpectrumPanel.cs
|-- LedBlinky.cs
|-- TrackerMetadata.cs
|-- DebugLogger.cs
|-- config.json
|-- publish.ps1
|-- Media/
|   |-- Screenshots/  (README images)
|   |-- Sounds/   (boot audio)
|   |-- Music/
|   |-- Video/
|   |-- Img/
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
