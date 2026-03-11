using System;
using System.Collections.Generic;
using System.IO;
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
                return (cfg, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }
    }

    public sealed class UiConfig
    {
        public string Title { get; set; } = "Select your environment";
        public bool TopMost { get; set; } = true;
        public int MinImageSizePx { get; set; } = 180;
        public double ImageHeightRatio { get; set; } = 0.42;
        public double ImageWidthRatioPerOption { get; set; } = 0.26;
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
}

