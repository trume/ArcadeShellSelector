# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.2.9] - 2026-03-13

### Added
- **Server startup guards** — `ArcadeShellServer.exe` now shows a MessageBox when launched directly if the app is not configured (first run) or remote access is explicitly disabled, instead of silently exiting.

### Fixed
- **Launcher server launch safety** — `StartRemoteServer()` now checks both `remoteAccess.enabled` and first-run state before spawning the server process.

## [1.2.0] - 2026-03-13

### Added
- **Remote mobile server (ArcadeShellServer)** — New embedded Kestrel HTTP server lets you manage your arcade cabinet from any phone or tablet on the local network. PIN-protected, auto-starts with the launcher, serves a responsive mobile web UI via embedded resources.
- **Mobile web UI** — Three-tab mobile dashboard (Estado → Ajustes → Lanzadores) to view system status, change settings (title, music, theme, boot splash, remote access), and manage launcher options from your phone.
- **Theme presets** — Full color theming system with five built-in presets (Neon Green, Amber CRT, Synthwave, Ice Blue, Minimal Dark) plus custom overrides for every color in both the launcher and boot splash.
- **Boot splash CRT effects** — Configurable scanline alpha, vignette alpha, and phosphor tint on the terminal boot animation. Exposed in the Configurator and persisted in `config.json`.
- **Fade transition** — Smooth cross-fade between option selections with configurable duration (`ui.fadeTransition`, `ui.fadeTransitionMs`).
- **ThemeConfig & ThemeResolver** — Centralized theme resolution with preset inheritance and per-color overrides for launcher and boot splash palettes.
- **Remote access config** — `remoteAccess` section in config.json (enabled, port, PIN, verbose logging) with full Configurator support.
- **Arranque per-option hints** — Inline Spanish hint labels for each startup option in the Configurator.
- **Server-side thumbVideo preservation** — PUT handler merges `thumbVideo` and `waitForProcessName` from disk, so the mobile app can never accidentally wipe fields it doesn't display.

### Fixed
- **Server process orphan on exit** — `StopRemoteServer()` moved to the very first action in `OnFormClosed`, before VLC dispose (which can deadlock). The server child process is now reliably killed on every exit path.
- **ThumbVideo wiped by mobile saves** — Root cause: mobile PUT sent full config with null thumbVideo. Fixed with server-side merge from existing config on disk.
- **Config overwritten by every build** — `config.json` changed from `CopyToOutputDirectory: PreserveNewest` to `Never` with a `SeedConfigIfMissing` target that only copies when the output file is missing.
- **UiConfig serialization casing** — Added `[JsonPropertyName]` attributes to all `UiConfig` properties to prevent PascalCase/camelCase mismatch during server roundtrips.
- **Timer/joystick dispose safety** — All timer and DirectInput dispose calls in `OnFormClosed` wrapped in individual try-catch blocks.

### Changed
- **Configurator General tab layout** — Replaced `Dock.Top` with `FlowLayoutPanel` wrapper so `Margin` properties actually work between GroupBoxes.
- **Mobile tab order** — Tabs reordered to Estado, Ajustes, Lanzadores (previously Estado, Opciones, Ajustes).
- **Grid thumbnail columns** — Image column widened to 80px, Video column to 85px for better preview.
- **Config dual-write** — Server writes config to both the runtime output directory and the project source directory so changes survive `dotnet clean`.
- **Reload button hidden** — Bottom bar simplified to Autor + Configurar + Salir.

## [1.1.0] - 2026-03-12

### Added
- **Structured log levels** — `DebugLogger` now supports `Info`, `Warn`, and `Error` levels with timestamped `[INF|WRN|ERR]` prefixes and component tags. All existing log calls upgraded across the codebase.
- **Log rotation** — Log files are automatically rotated when they exceed 2 MB; the previous log is kept as a `.bak` file.
- **Config validation** — `AppConfig.TryLoadFromFile` performs structural validation on load and returns a list of warnings (missing paths, out-of-range values) surfaced at startup.
- **Network path feedback** — A status label on the launcher shows real-time connection status when waiting for UNC paths, so users on networked cabinets see progress instead of a frozen UI.
- **LibVLC warm-up** — LibVLC core is pre-initialized on a background thread at startup, reducing first-video-play latency.
- **Audio device GUID persistence** — `MusicConfig.AudioDeviceId` stores the device GUID alongside the friendly name. Device matching now tries the stable GUID first, falling back to the display name if the GUID is missing or stale.
- **Binding countdown UI** — Gamepad button binding in the Configurator now shows an animated countdown hint (`🎮 Press a button… 5s`) that ticks down in real time, replacing the previous silent wait.
- **Configurable spectrum bands** — `SpectrumAnalyzer` band count is now a constructor parameter (2–32) driven by `ui.spectrumBands` in `config.json`. Band frequency edges and reference levels are generated dynamically with logarithmic spacing.
- **Video playback rate config** — Background video speed is now configurable via `paths.videoPlaybackRate` in `config.json` (clamped 0.25–4.0), replacing the hardcoded 1.15× multiplier.

### Fixed
- **ConfigForm resource leaks** — Five disposable resources (`_diJoystick`, `_diBindTimer`, `_xiBindTimer`, `_refreshTimer`, `_dirtyDebounce`) are now properly disposed in `OnFormClosed`, preventing handle leaks.
- **Timer tick re-entrancy** — All timer tick handlers in the Launcher are guarded against overlapping execution, preventing rare race conditions during rapid UI events.
- **Exception swallowing** — 30+ bare `catch { }` blocks across the Launcher now log the caught exception via `DebugLogger.Error`, making silent failures diagnosable.
- **Idle CPU usage during child process** — The Z-order enforcement timer is now stopped while a child application is running and restarted on return, eliminating unnecessary CPU wake-ups.

### Changed
- **DPI awareness** — Both apps now declare `PerMonitorV2` high-DPI mode. `ConfigForm` uses `AutoScaleMode.Dpi` for correct scaling on high-resolution arcade monitors.
- **Z-order timer interval** — Reduced from 250 ms to 1000 ms, cutting background overhead by 75% with no visible impact on window stacking behavior.

## [1.0.6] - 2026-03-11

### Added
- **Input indicator on bottom bar** — A subtle label at the bottom of the launcher shows the active input method (🎮 XInput, 🎮 DInput, 🎮 XInput + DInput, or ⌨ Keyboard) so the user always knows which controller input is enabled.
- **Configuración button** — New button on the launcher bottom bar launches ArcadeShellConfigurator directly. Smooth async handoff polls the configurator window before closing the launcher, avoiding a visible gap or hourglass cursor.
- **Controller navigation for buttons** — Gamepad navigation (DInput/XInput) now cycles through option cards, Configuración, and Salir/Exit using a unified `_navIndex`.
- **Single-instance configurator** — ArcadeShellConfigurator uses a named Mutex to prevent multiple instances from running simultaneously.

### Fixed
- **Music volume drops after hover-out** — Thumb video player and main music shared the same Windows audio session; stopping the thumb player left the process-wide mixer at the thumb volume. Now restores `musicPlayer.ConfiguredVolume` when the thumb video stops.
- **Thumb video volume too loud after returning from child process** — Volume is now explicitly set before `Play()` and re-applied after the `EndReached` loop restart.
- **Magenta text antialiasing fringe** — Labels on overlay/spectrum forms had magenta-tinted edges because `TransparencyKey` was `Color.Magenta`. Changed to `Color.FromArgb(1, 1, 1)` (near-black).
- **FirstRunGuard button top border clipped** — Label bottom edge overlapped buttons. Adjusted button Y-position and form height.

### Changed
- **FirstRunGuard UI polish** — Custom image from `Media\Img\firstrun.png` (fallback to system icon), centered buttons with distinct sizes, no blue AcceptButton highlight.
- **Option label font** — Increased from 14pt Regular to 18pt Bold for better readability on arcade monitors.
- **Launcher bottom bar layout** — Author icon + text, Configuración button, Salir/Exit button, and input indicator are all centered on a single line.

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
