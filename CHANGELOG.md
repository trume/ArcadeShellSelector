# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
