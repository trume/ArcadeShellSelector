# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
