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

        [JsonPropertyName("files")]
        public List<string>? Files { get; set; }

        [JsonPropertyName("volume")]
        public int Volume { get; set; } = 100;

        [JsonPropertyName("audioDevice")]
        public string? AudioDevice { get; set; }
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
}

