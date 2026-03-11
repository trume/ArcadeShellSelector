# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.0.4] - 2026-03-11

### Added
- **First-run guard** — New `FirstRunGuard.cs` detects unconfigured state (missing `config.json` or empty `options`) before anything is shown on screen. Presents a standard Windows dialog offering to open the Configurator or exit; the app cannot launch unconfigured.
- **BootSplash bypass setting** — New `arranque.bootSplashEnabled` boolean in `config.json` and `StartupConfig` class. When `false`, the boot animation is skipped entirely and the launcher appears immediately. Exposed in the Configurator under the renamed **"Arranque"** group on the General tab.
- **Clean first-run config in publish** — `publish.ps1` now replaces the deployed `config.json` with a pristine first-run version (empty `options`, blank paths, features disabled) so new installations always trigger the first-run guard.

### Fixed
- **Configurator Launch button fails on target machines** — `BtnLaunch_Click` only searched `bin\Release` and `bin\Debug` dev paths. Now checks alongside the configurator's own executable first (deployed flat layout), then falls back to dev paths.
- **Video background disappears after selecting it** — `BrowseAndDeployVideo` deleted all videos in `Media\Bkg` before copying, destroying the source file when it already lived there. Cleanup now skips the selected file, and self-copies are avoided.

### Changed
- **Behavior group renamed to Arranque** — The General tab's "Behavior" GroupBox is now labelled "Arranque" and includes the BootSplash toggle alongside TopMost and logging checkboxes.

## [1.0.3] - 2026-03-10

### Fixed
- **XInput test panel shows wrong layout when idle** — `InputVisualPanel` defaulted to DInput mode (`_isXInput = false`), drawing a single STICK + POV circle instead of the dual L STICK + R STICK + trigger layout on the XInput panel. Added a `XInputMode` property; `visualXInput` is now initialized with `XInputMode = true` so the correct layout is always visible, even before hitting ▶ Iniciar.
- **XInput Controles tab layout broken** — The `grpXI.Layout` handler only updated `visualXInput.Width`, leaving conflicting `Anchor` flags fighting the manual sizing. Removed `Anchor` from `visualXInput`, `lblXInputButtons`, and `lblXInputAxes`; the Layout handler now bottom-pins both text labels and stretches the test panel to fill the full available height of the GroupBox.
- **Main launcher crash on startup (SharpDX.XInput not found)** — `Program.cs` used `AppDomain.CurrentDomain.AssemblyResolve` which fires after JIT compilation in .NET 10, too late to resolve `SharpDX.XInput.dll` from the `lib\` subfolder. Replaced with a `LibProber` static class using `[ModuleInitializer]` + `AssemblyLoadContext.Default.Resolving`, the same approach already proven in the configurator.

## [1.0.2] - 2026-03-10

### Added
- **Right stick test visualizer** — XInput test panel now shows an **R STICK** circle alongside the existing L STICK, polling `RightThumbX/Y` in real time. Axes status label extended to `LX | LY | RX | RY | LT | RT`.
- **Button assignment flash** — Both DInput and XInput binding labels flash bright neon green `(50, 255, 100)` for 700 ms when a button is successfully captured, giving instant visual confirmation.

### Changed
- **XInput test panel layout** — Sticks and trigger bars are now in a top strip; button pill-tags occupy the full panel width below a separator line, preventing overflow when many buttons are held simultaneously.
- **Audio Output frame order** — "Audio Device" combo is now the first control in the Audio Output group box (above Volume and Thumb Video Volume sliders).

### Fixed
- **Music metadata not shown on startup** — When "Play Random" is unchecked, the metadata panel for the saved music file now populates immediately when the configurator opens. Previously the `_suppressDirty` guard silently skipped the metadata call during config load.

## [1.0.1] - 2026-03-09

### Changed

- Added a dedicated release workflow to automate version bump, tagging, packaging, and GitHub Release publishing.
- Enabled automatic release workflow trigger on pushes to `master` with loop guards for bot-generated release commits.
- Hardened release workflow push logic with `fetch + rebase + retry` to reduce non-fast-forward failures.
- Fixed `publish.ps1` self-contained flow to restore/publish both projects with `win-x64` RID directly into deploy output.

### Documentation

- Rewrote README with structured features, build/deploy instructions, architecture overview, and repository standards.
- Added mandatory Windows shell replacement registry instructions with backup and rollback commands.

## [0.99.0] — 2026-03-09

### Added

- Full-screen WinForms arcade frontend launcher with option grid.
- Video background playback via LibVLC.
- Music playback for tracker modules (MOD/XM) via NAudio and LibVLC.
- Real-time spectrum analyzer overlay (WASAPI loopback, click-through).
- Gamepad navigation: XInput (Xbox controllers) and DirectInput (Xin-Mo, I-PAC arcade encoders).
- Hover/select video thumbnail previews using LibVLC software rendering.
- LedBlinky integration for LED-lit arcade button feedback.
- Network path wait for UNC-path executables.
- Shell mode: launches `explorer.exe` on exit.
- Dirty tracking for unsaved config changes.
- Defaults button to reset configuration.
- ArcadeShellConfigurator companion app with tabbed config editor (General, Paths, Music, App Options).
- Video thumbnail extraction via Windows Shell API in Configurator.
- Multi-config sync: updates all `config.json` copies across projects.
- `publish.ps1` deployment script with framework-dependent and self-contained modes.
- Versioned ZIP packaging with git commit hash tagging.
