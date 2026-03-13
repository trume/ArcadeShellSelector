using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArcadeShellSelector
{
    // Root config - public so other classes (Launcher, MusicPlayer) can reference it
    public sealed class AppConfig
    {
        [JsonPropertyName("ui")]
        public UiConfig Ui { get; set; } = new();

        [JsonPropertyName("paths")]
        public PathConfig Paths { get; set; } = new();

        [JsonPropertyName("options")]
        public List<OptionConfig> Options { get; set; } = new();

        [JsonPropertyName("music")]
        public MusicConfig Music { get; set; } = new();

        [JsonPropertyName("Autor")]
        public AutorConfig Autor { get; set; } = new();

        [JsonPropertyName("Depuracion")]
        public DebugConfig Activa { get; set; } = new();

        [JsonPropertyName("input")]
        public InputConfig Input { get; set; } = new();

        [JsonPropertyName("ledblinky")]
        public LedBlinkyConfig LedBlinky { get; set; } = new();

        [JsonPropertyName("arranque")]
        public StartupConfig Arranque { get; set; } = new();

        [JsonPropertyName("theme")]
        public ThemeConfig Theme { get; set; } = new();

        [JsonPropertyName("remoteAccess")]
        public RemoteAccessConfig RemoteAccess { get; set; } = new();

        public static (AppConfig? cfg, string? err) TryLoadFromFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return (null, $"Config file not found: {path}");

                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                };
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, opts);
                if (cfg == null) return (null, "Deserialization returned null.");

                // Validate structural integrity
                var warnings = new List<string>();

                if (cfg.Options.Count == 0)
                    warnings.Add("No options defined — the launcher will have nothing to show.");

                foreach (var (opt, i) in cfg.Options.Select((o, i) => (o, i)))
                {
                    if (string.IsNullOrWhiteSpace(opt.Exe))
                        warnings.Add($"Option #{i + 1} (\"{opt.Label}\"): exe path is empty.");
                    if (string.IsNullOrWhiteSpace(opt.Label))
                        warnings.Add($"Option #{i + 1}: label is empty.");
                }

                if (cfg.Music.Enabled && string.IsNullOrWhiteSpace(cfg.Music.MusicRoot) && string.IsNullOrWhiteSpace(cfg.Music.SelectedFile))
                    warnings.Add("Music is enabled but no musicRoot or selectedFile is set.");

                string? warn = warnings.Count > 0 ? string.Join("\n", warnings) : null;
                return (cfg, warn);
            }
            catch (JsonException ex)
            {
                return (null, $"Invalid JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }
    }

    public sealed class UiConfig
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = "Select your environment";

        [JsonPropertyName("topMost")]
        public bool TopMost { get; set; } = true;

        [JsonPropertyName("minImageSizePx")]
        public int MinImageSizePx { get; set; } = 180;

        [JsonPropertyName("imageHeightRatio")]
        public double ImageHeightRatio { get; set; } = 0.42;

        [JsonPropertyName("imageWidthRatioPerOption")]
        public double ImageWidthRatioPerOption { get; set; } = 0.26;

        [JsonPropertyName("spectrumBands")]
        public int SpectrumBands { get; set; } = 6;

        [JsonPropertyName("fadeTransition")]
        public bool FadeTransition { get; set; } = true;

        [JsonPropertyName("fadeTransitionMs")]
        public int FadeTransitionMs { get; set; } = 400;
    }

    public sealed class PathConfig
    {
        [JsonPropertyName("toolsRoot")]
        public string ToolsRoot { get; set; } = string.Empty;

        [JsonPropertyName("imagesRoot")]
        public string ImagesRoot { get; set; } = string.Empty;

        [JsonPropertyName("networkWaitSeconds")]
        public int NetworkWaitSeconds { get; set; } = 15;

        [JsonPropertyName("videoBackground")]
        public string VideoBackground { get; set; } = string.Empty;

        [JsonPropertyName("videoPlaybackRate")]
        public float VideoPlaybackRate { get; set; } = 1.0f;
    }

    // NEW: Option model includes optional WaitForProcessName
    public sealed class OptionConfig
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("exe")]
        public string Exe { get; set; } = string.Empty;

        [JsonPropertyName("image")]
        public string Image { get; set; } = string.Empty;

        [JsonPropertyName("thumbVideo")]
        public string? ThumbVideo { get; set; }

        // optional: can be "CoinOPS.exe" or full "CoinOPS.exe" — RunSelectedApp normaliza.
        [JsonPropertyName("waitForProcessName")]
        public string? WaitForProcessName { get; set; }
    }

    public sealed class MusicConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("musicRoot")]
        public string? MusicRoot { get; set; }

        [JsonPropertyName("playRandom")]
        public bool PlayRandom { get; set; } = true;

        [JsonPropertyName("selectedFile")]
        public string? SelectedFile { get; set; }

        [JsonPropertyName("volume")]
        public int Volume { get; set; } = 100;

        [JsonPropertyName("audioDevice")]
        public string? AudioDevice { get; set; }

        [JsonPropertyName("audioDeviceId")]
        public string? AudioDeviceId { get; set; }

        [JsonPropertyName("thumbVideoVolume")]
        public int ThumbVideoVolume { get; set; } = 0;
    }

    public sealed class AutorConfig
    {
        [JsonPropertyName("Quien")]
        public string Quien { get; set; } = string.Empty;
    }

    public sealed class DebugConfig
    {
        // matches your JSON naming "Activa"
        [JsonPropertyName("Activa")]
        public bool Activa { get; set; } = false;
    }

    public sealed class InputConfig
    {
        /// <summary>Enable XInput polling (Xbox / compatible gamepads)</summary>
        [JsonPropertyName("xinputEnabled")]
        public bool XInputEnabled { get; set; } = false;

        /// <summary>Enable DirectInput polling (for arcade encoders, Xin-Mo, etc.)</summary>
        [JsonPropertyName("dinputEnabled")]
        public bool DInputEnabled { get; set; } = true;

        /// <summary>1-based button number for "select / confirm". Default = 1.</summary>
        [JsonPropertyName("dinputButtonSelect")]
        public int DInputButtonSelect { get; set; } = 1;

        /// <summary>1-based button number for "back / close". Default = 2.</summary>
        [JsonPropertyName("dinputButtonBack")]
        public int DInputButtonBack { get; set; } = 2;

        /// <summary>1-based button number for "move left". 0 = use joystick axis / POV hat.</summary>
        [JsonPropertyName("dinputButtonLeft")]
        public int DInputButtonLeft { get; set; } = 0;

        /// <summary>1-based button number for "move right". 0 = use joystick axis / POV hat.</summary>
        [JsonPropertyName("dinputButtonRight")]
        public int DInputButtonRight { get; set; } = 0;

        /// <summary>
        /// Product name of the preferred DirectInput device.
        /// Empty = use the first non-XInput device found (original behaviour).
        /// Set this when the arcade machine has more than one encoder board connected
        /// and you need to pick a specific one.
        /// </summary>
        [JsonPropertyName("dinputDeviceName")]
        public string DInputDeviceName { get; set; } = string.Empty;

        /// <summary>
        /// Minimum milliseconds between any two navigation moves (left/right).
        /// Increase this value on arcade machines with noisy encoders to prevent
        /// the selection from looping erratically. Default = 300 ms.
        /// </summary>
        [JsonPropertyName("navCooldownMs")]
        public int NavCooldownMs { get; set; } = 300;

        // ── XInput ──────────────────────────────────────────────────────────

        /// <summary>
        /// XInput slot index to use (0-3 = specific slot, -1 = first connected).
        /// Slot numbers here are zero-based: 0 = Player 1, 1 = Player 2, etc.
        /// </summary>
        [JsonPropertyName("xinputSlot")]
        public int XInputSlot { get; set; } = -1;

        /// <summary>GamepadButtonFlags value for "select / confirm". Default = A (4096).</summary>
        [JsonPropertyName("xinputButtonSelect")]
        public int XInputButtonSelect { get; set; } = 4096; // GamepadButtonFlags.A

        /// <summary>GamepadButtonFlags value for "back / close". Default = B (8192).</summary>
        [JsonPropertyName("xinputButtonBack")]
        public int XInputButtonBack { get; set; } = 8192; // GamepadButtonFlags.B

        /// <summary>GamepadButtonFlags value for "move left". 0 = DPad Left + left stick axis.</summary>
        [JsonPropertyName("xinputButtonLeft")]
        public int XInputButtonLeft { get; set; } = 0;

        /// <summary>GamepadButtonFlags value for "move right". 0 = DPad Right + left stick axis.</summary>
        [JsonPropertyName("xinputButtonRight")]
        public int XInputButtonRight { get; set; } = 0;
    }

    public sealed class LedBlinkyConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("exePath")]
        public string ExePath { get; set; } = string.Empty;
    }

    public sealed class StartupConfig
    {
        /// <summary>
        /// When false the BootSplash animation is skipped entirely and the
        /// launcher appears immediately on startup.
        /// </summary>
        [JsonPropertyName("bootSplashEnabled")]
        public bool BootSplashEnabled { get; set; } = true;
    }

    public sealed class ThemeConfig
    {
        /// <summary>Named preset: neon-green, amber-crt, synthwave, ice-blue, minimal-dark, custom.</summary>
        [JsonPropertyName("preset")]
        public string Preset { get; set; } = "neon-green";

        // ── Fonts ───────────────────────────────────────────────────────
        /// <summary>Font family for launcher UI (title, buttons, labels). Empty = Segoe UI.</summary>
        [JsonPropertyName("launcherFont")]
        public string? LauncherFont { get; set; }

        /// <summary>Font family for boot splash terminal text. Empty = Courier New.</summary>
        [JsonPropertyName("bootSplashFont")]
        public string? BootSplashFont { get; set; }

        // ── Launcher colors (hex "#RRGGBB" or "#AARRGGBB", empty = from preset) ──
        [JsonPropertyName("selectionBorderColor")]
        public string? SelectionBorderColor { get; set; }

        [JsonPropertyName("hoverOutlineColor")]
        public string? HoverOutlineColor { get; set; }

        [JsonPropertyName("titleColor")]
        public string? TitleColor { get; set; }

        [JsonPropertyName("buttonTextColor")]
        public string? ButtonTextColor { get; set; }

        [JsonPropertyName("buttonHighlightBg")]
        public string? ButtonHighlightBg { get; set; }

        [JsonPropertyName("buttonHighlightFg")]
        public string? ButtonHighlightFg { get; set; }

        [JsonPropertyName("buttonBorderColor")]
        public string? ButtonBorderColor { get; set; }

        [JsonPropertyName("spectrumBarColor")]
        public string? SpectrumBarColor { get; set; }

        [JsonPropertyName("authorTextColor")]
        public string? AuthorTextColor { get; set; }

        [JsonPropertyName("networkStatusColor")]
        public string? NetworkStatusColor { get; set; }

        // ── Boot splash colors ──────────────────────────────────────────
        /// <summary>Boot splash sub-preset. Empty = inherits from main preset.</summary>
        [JsonPropertyName("bootSplashPreset")]
        public string? BootSplashPreset { get; set; }

        [JsonPropertyName("bootSplashBg")]
        public string? BootSplashBg { get; set; }

        [JsonPropertyName("bootSplashPrimary")]
        public string? BootSplashPrimary { get; set; }

        [JsonPropertyName("bootSplashDim")]
        public string? BootSplashDim { get; set; }

        [JsonPropertyName("bootSplashBright")]
        public string? BootSplashBright { get; set; }

        [JsonPropertyName("bootSplashTag")]
        public string? BootSplashTag { get; set; }

        [JsonPropertyName("bootSplashInit")]
        public string? BootSplashInit { get; set; }

        [JsonPropertyName("bootSplashWarn")]
        public string? BootSplashWarn { get; set; }

        [JsonPropertyName("bootSplashPhosphorTint")]
        public string? BootSplashPhosphorTint { get; set; }

        [JsonPropertyName("bootSplashScanlineAlpha")]
        public int? BootSplashScanlineAlpha { get; set; }

        [JsonPropertyName("bootSplashVignetteAlpha")]
        public int? BootSplashVignetteAlpha { get; set; }

        [JsonPropertyName("bootSplashCrtEffects")]
        public bool BootSplashCrtEffects { get; set; } = true;
    }

    public sealed class RemoteAccessConfig
    {
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        [JsonPropertyName("port")]
        public int Port { get; set; } = 8484;

        [JsonPropertyName("pin")]
        public string Pin { get; set; } = "0000";

        [JsonPropertyName("verbose")]
        public bool Verbose { get; set; } = false;
    }
}

