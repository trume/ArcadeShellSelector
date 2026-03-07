# ArcadeShellSelector & Configurator

## Features

- ArcadeShellSelector (Main App):
  - WinForms launcher for arcade frontends
  - Video background playback (LibVLC)
  - Music playback (NAudio/LibVLC)
  - Spectrum analyzer overlay (click-through)
  - Gamepad navigation (SharpDX.XInput)
  - Option grid with per-row image, video thumbnail, and browse buttons
  - Hover/select: plays thumb video preview in PictureBox (LibVLC software rendering)
  - Logging toggle, launch button, config sync, app icon
  - Dirty tracking for config changes
  - Deploys video backgrounds and images to output folders
  - Defaults button resets config to AppConfig defaults
  - Author label and close button aligned on bottom line

- ArcadeShellConfigurator (Config Editor):
  - WinForms config editor for all app options
  - Tabs: General, Paths, Music, App Options
  - Option grid: label, exe, image, video thumb, browse buttons
  - Video thumbnail extraction via Windows Shell API
  - Video background thumbnail preview
  - Multi-config sync: updates all config.json copies
  - Save, Cancel, Reload, Defaults, Launch App buttons
  - Dirty tracking for edits
  - Output files auto-copied to main app bin directory after build

## Build & Deploy

- Both apps target net10.0-windows
- All dependencies and output files are auto-copied for easy deployment
- Configurator executable and dependencies are always available in main app bin directory

## Troubleshooting

- If Configurator does not launch, ensure all dependencies (.dll, .runtimeconfig.json, .deps.json, app.ico) are present
- Main app and Configurator can be launched independently from bin/Release/net10.0-windows

# Code Citations

## License: unknown
https://github.com/manitou48/propctrng/blob/6e0ffcb5f79dc41df9cdb7ba776e98149ffe31bc/propctrng.spin


```markdown
# ArcadeShellSelector — Architecture Documentation

## Table of Contents

1. [Overview](#overview)
2. [Class Diagrams](#class-diagrams)
3. [Sequence Diagrams](#sequence-diagrams)

---

## Overview

**ArcadeShellSelector** is a WinForms arcade frontend launcher with video backgrounds, music playback, spectrum visualization, and gamepad navigation. **ArcadeShellConfigurator** is a companion config editor that parametrizes the launcher's settings via a shared `config.json`.

---

## Class Diagrams

### Config Model

```text
┌──────────────────────────────────┐
│         «sealed» AppConfig       │
├──────────────────────────────────┤
│ + Ui : UiConfig                  │
│ + Paths : PathConfig             │
│ + Options : List<OptionConfig>   │
│ + Music : MusicConfig            │
│ + Autor : AutorConfig            │
│ + Activa : DebugConfig           │
├──────────────────────────────────┤
│ + «static» TryLoadFromFile(path) │
│   → (AppConfig?, string?)        │
└──────┬───────────────────────────┘
       │ has-a (composition)
       │
 ┌─────┼───────────────┬──────────────────┬──────────────────┐
 │     │               │                  │                  │
 ▼     ▼               ▼                  ▼                  ▼
┌────────────┐ ┌─────────────┐ ┌──────────────────┐ ┌─────────────┐
│  UiConfig  │ │ PathConfig  │ │  OptionConfig    │ │ MusicConfig │
├────────────┤ ├─────────────┤ ├──────────────────┤ ├─────────────┤
│Title       │ │ToolsRoot    │ │Label             │ │Enabled      │
│TopMost     │ │ImagesRoot   │ │Exe               │ │MusicRoot?   │
│MinImagePx  │ │NetworkWait  │ │Image             │ │Files?       │
│ImgHtRatio  │ │VideoBackgrnd│ │ThumbVideo?       │ │Volume       │
│ImgWdRatio  │ └─────────────┘ │WaitForProcess?   │ │AudioDevice? │
└────────────┘                 └──────────────────┘ └─────────────┘

┌───────────────┐  ┌───────────────┐
│  AutorConfig  │  │  DebugConfig  │
├───────────────┤  ├───────────────┤
│ Quien: string │  │ Activa: bool  │
└───────────────┘  └───────────────┘
```

### Main App — Runtime Components

```text
┌──────────────────────────────────────────────────────────────────┐
│              «partial» Launcher : Form                           │
├──────────────────────────────────────────────────────────────────┤
│ Fields                                                           │
│  - config : AppConfig                                            │
│  - optionUis : List<(PictureBox,Label,string,string?)>          │
│  - selectedPic : PictureBox?                                     │
│  - _childRunning : bool                                          │
│  - xinputController : Controller                                 │
│  - xinputTimer : Timer                                           │
│  - _overlayForm, _spectrumForm : Form?                          │
│  - _thumbLibVlc, _thumbPlayer, _thumbMedia (video preview)      │
│  - _thumbBuffer, _thumbW, _thumbH (software rendering)          │
│  - _originalBounds, _thumbVideoPaths, _thumbOriginalImages       │
├──────────────────────────────────────────────────────────────────┤
│ Lifecycle                                                        │
│  + Launcher()  → load config, init form, init controls           │
│  - MainForm_Load()  → start video, music, spectrum, layout       │
│  - MainForm_Resize() → LayoutControls()                          │
│  # OnFormClosed()  → dispose all media                           │
├──────────────────────────────────────────────────────────────────┤
│ Navigation                                                       │
│  - MoveSelection(direction)                                      │
│  - SelectCurrentOption() → OnOptionClickedAsync()                │
│  - XinputTimer_Tick()  → poll gamepad, dispatch actions          │
│  - MainForm_KeyDown()  → keyboard fallback                       │
├──────────────────────────────────────────────────────────────────┤
│ App Launch                                                       │
│  - OnOptionClickedAsync(pic, exe, wait)                          │
│  - RunSelectedApp(exe, wait?) → Process.Start + WaitForExit      │
│  - EnsureExplorerRunning()                                       │
├──────────────────────────────────────────────────────────────────┤
│ Visual Effects                                                   │
│  - ApplyZoom(pb, scale) / ResetZoom(pb)                          │
│  - RefreshSelectionVisuals()                                     │
│  - StartThumbVideo(pb) / StopThumbVideo(pb) (LibVLC software)   │
│  - ThumbLockCb / ThumbDisplayCb  (video frame callbacks)         │
│  - Pb_Paint()  (selection border)                                │
│  - PicHoverEnter / PicHoverLeave                                 │
├──────────────────────────────────────────────────────────────────┤
│ Background                
```


## License: unknown
https://github.com/manitou48/propctrng/blob/6e0ffcb5f79dc41df9cdb7ba776e98149ffe31bc/propctrng.spin

```


I don't have file editing tools enabled to create the files directly. Here's a comprehensive Markdown document you can save — it renders well in VS Code and can be exported to PDF via the "Markdown PDF" extension or any Markdown-to-PDF converter:

```markdown
# ArcadeShellSelector — Architecture Documentation

## Table of Contents

1. [Overview](#overview)
2. [Class Diagrams](#class-diagrams)
3. [Sequence Diagrams](#sequence-diagrams)

---

## Overview

**ArcadeShellSelector** is a WinForms arcade frontend launcher with video backgrounds, music playback, spectrum visualization, and gamepad navigation. **ArcadeShellConfigurator** is a companion config editor that parametrizes the launcher's settings via a shared `config.json`.

---

## Class Diagrams

### Config Model

```text
┌──────────────────────────────────┐
│         «sealed» AppConfig       │
├──────────────────────────────────┤
│ + Ui : UiConfig                  │
│ + Paths : PathConfig             │
│ + Options : List<OptionConfig>   │
│ + Music : MusicConfig            │
│ + Autor : AutorConfig            │
│ + Activa : DebugConfig           │
├──────────────────────────────────┤
│ + «static» TryLoadFromFile(path) │
│   → (AppConfig?, string?)        │
└──────┬───────────────────────────┘
       │ has-a (composition)
       │
 ┌─────┼───────────────┬──────────────────┬──────────────────┐
 │     │               │                  │                  │
 ▼     ▼               ▼                  ▼                  ▼
┌────────────┐ ┌─────────────┐ ┌──────────────────┐ ┌─────────────┐
│  UiConfig  │ │ PathConfig  │ │  OptionConfig    │ │ MusicConfig │
├────────────┤ ├─────────────┤ ├──────────────────┤ ├─────────────┤
│Title       │ │ToolsRoot    │ │Label             │ │Enabled      │
│TopMost     │ │ImagesRoot   │ │Exe               │ │MusicRoot?   │
│MinImagePx  │ │NetworkWait  │ │Image             │ │Files?       │
│ImgHtRatio  │ │VideoBackgrnd│ │ThumbVideo?       │ │Volume       │
│ImgWdRatio  │ └─────────────┘ │WaitForProcess?   │ │AudioDevice? │
└────────────┘                 └──────────────────┘ └─────────────┘

┌───────────────┐  ┌───────────────┐
│  AutorConfig  │  │  DebugConfig  │
├───────────────┤  ├───────────────┤
│ Quien: string │  │ Activa: bool  │
└───────────────┘  └───────────────┘
```

### Main App — Runtime Components

```text
┌──────────────────────────────────────────────────────────────────┐
│              «partial» Launcher : Form                           │
├──────────────────────────────────────────────────────────────────┤
│ Fields                                                           │
│  - config : AppConfig                                            │
│  - optionUis : List<(PictureBox,Label,string,string?)>          │
│  - selectedPic : PictureBox?                                     │
│  - _childRunning : bool                                          │
│  - xinputController : Controller                                 │
│  - xinputTimer : Timer                                           │
│  - _overlayForm, _spectrumForm : Form?                          │
│  - _thumbLibVlc, _thumbPlayer, _thumbMedia (video preview)      │
│  - _thumbBuffer, _thumbW, _thumbH (software rendering)          │
│  - _originalBounds, _thumbVideoPaths, _thumbOriginalImages       │
├──────────────────────────────────────────────────────────────────┤
│ Lifecycle                                                        │
│  + Launcher()  → load config, init form, init controls           │
│  - MainForm_Load()  → start video, music, spectrum, layout       │
│  - MainForm_Resize() → LayoutControls()                          │
│  # OnFormClosed()  → dispose all media                           │
├──────────────────────────────────────────────────────────────────┤
│ Navigation                                                       │
│  - MoveSelection(direction)                                      │
│  - SelectCurrentOption() → OnOptionClickedAsync()                │
│  - XinputTimer_Tick()  → poll gamepad, dispatch actions          │
│  - MainForm_KeyDown()  → keyboard fallback                       │
├──────────────────────────────────────────────────────────────────┤
│ App Launch                                                       │
│  - OnOptionClickedAsync(pic, exe, wait)                          │
│  - RunSelectedApp(exe, wait?) → Process.Start + WaitForExit      │
│  - EnsureExplorerRunning()                                       │
├──────────────────────────────────────────────────────────────────┤
│ Visual Effects                                                   │
│  - ApplyZoom(pb, scale) / ResetZoom(pb)                          │
│  - RefreshSelectionVisuals()                                     │
│  - StartThumbVideo(pb) / StopThumbVideo(pb) (LibVLC software)   │
│  - ThumbLockCb / ThumbDisplayCb  (video frame callbacks)         │
│  - Pb_Paint()  (selection border)                                │
│  - PicHoverEnter / PicHoverLeave                                 │
├──────────────────────────────────────────────────────────────────┤
│ Background                
```

