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

