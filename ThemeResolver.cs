using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace ArcadeShellSelector
{
    /// <summary>
    /// Resolves theme colors and fonts from config.
    /// Merges: explicit override → preset defaults → hardcoded fallbacks.
    /// Call <see cref="Init"/> once at startup before any forms are created.
    /// </summary>
    internal static class ThemeResolver
    {
        // ── Private font collection (for Media/Fonts/*.ttf) ─────────────
        private static readonly PrivateFontCollection _privateFonts = new();

        // ── Resolved palettes (set by Init) ─────────────────────────────
        public static LauncherPalette Launcher { get; private set; } = new();
        public static BootSplashPalette Boot { get; private set; } = new();

        /// <summary>
        /// Load private fonts from Media/Fonts/ and resolve all theme colors.
        /// Must be called before any form that uses theme colors is created.
        /// </summary>
        public static void Init(AppConfig config)
        {
            LoadPrivateFonts();

            var theme = config.Theme;
            var presetName = (theme.Preset ?? "neon-green").ToLowerInvariant().Trim();
            if (!Presets.TryGetValue(presetName, out var preset))
                preset = Presets["neon-green"];

            // Resolve boot splash sub-preset
            var bootPresetName = theme.BootSplashPreset?.ToLowerInvariant().Trim();
            BootSplashPresetData? bootPreset = null;
            if (!string.IsNullOrEmpty(bootPresetName) && BootPresets.TryGetValue(bootPresetName, out var bp))
                bootPreset = bp;

            // ── Launcher palette ────────────────────────────────────────
            Launcher = new LauncherPalette
            {
                SelectionBorder = Resolve(theme.SelectionBorderColor, preset.SelectionBorder),
                HoverOutline    = Resolve(theme.HoverOutlineColor, preset.HoverOutline),
                Title           = Resolve(theme.TitleColor, preset.Title),
                ButtonText      = Resolve(theme.ButtonTextColor, preset.ButtonText),
                ButtonHighlightBg = Resolve(theme.ButtonHighlightBg, preset.ButtonHighlightBg),
                ButtonHighlightFg = Resolve(theme.ButtonHighlightFg, preset.ButtonHighlightFg),
                ButtonBorder    = Resolve(theme.ButtonBorderColor, preset.ButtonBorder),
                SpectrumBar     = Resolve(theme.SpectrumBarColor, preset.SpectrumBar),
                AuthorText      = Resolve(theme.AuthorTextColor, preset.AuthorText),
                NetworkStatus   = Resolve(theme.NetworkStatusColor, preset.NetworkStatus),
                Font            = ResolveFont(theme.LauncherFont, "Segoe UI"),
            };

            // ── Boot splash palette ─────────────────────────────────────
            // Priority: explicit config field > boot sub-preset > main preset boot colors
            var bpData = bootPreset ?? preset.Boot;

            Boot = new BootSplashPalette
            {
                Bg       = Resolve(theme.BootSplashBg, bpData.Bg),
                Primary  = Resolve(theme.BootSplashPrimary, bpData.Primary),
                Dim      = Resolve(theme.BootSplashDim, bpData.Dim),
                Bright   = Resolve(theme.BootSplashBright, bpData.Bright),
                Tag      = Resolve(theme.BootSplashTag, bpData.Tag),
                Init     = Resolve(theme.BootSplashInit, bpData.Init),
                Warn     = Resolve(theme.BootSplashWarn, bpData.Warn),
                PhosphorTint = Resolve(theme.BootSplashPhosphorTint, bpData.PhosphorTint),
                ScanlineAlpha  = theme.BootSplashScanlineAlpha ?? bpData.ScanlineAlpha,
                VignetteAlpha  = theme.BootSplashVignetteAlpha ?? bpData.VignetteAlpha,
                CrtEffects     = theme.BootSplashCrtEffects,
                Font           = ResolveFont(theme.BootSplashFont, "Courier New"),
            };
        }

        /// <summary>All known preset names, for populating UI dropdowns.</summary>
        public static IReadOnlyList<string> PresetNames { get; } =
            new[] { "neon-green", "amber-crt", "synthwave", "ice-blue", "minimal-dark" };

        /// <summary>All known boot splash preset names.</summary>
        public static IReadOnlyList<string> BootPresetNames { get; } =
            new[] { "green-crt", "amber-crt", "blue-crt", "purple-crt", "clean-white", "matrix" };

        /// <summary>Returns all available font family names (system + private).</summary>
        public static string[] GetAvailableFonts()
        {
            var system = FontFamily.Families.Select(f => f.Name);
            var priv   = _privateFonts.Families.Select(f => f.Name);
            return system.Union(priv, StringComparer.OrdinalIgnoreCase)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                         .ToArray();
        }

        /// <summary>
        /// Returns the preset launcher+boot defaults for a given preset name.
        /// Used by the Configurator to show preset colors in the UI.
        /// </summary>
        public static (LauncherPalette launcher, BootSplashPalette boot) GetPresetPalettes(string presetName)
        {
            if (!Presets.TryGetValue(presetName.ToLowerInvariant(), out var preset))
                preset = Presets["neon-green"];

            var launcher = new LauncherPalette
            {
                SelectionBorder = preset.SelectionBorder,
                HoverOutline    = preset.HoverOutline,
                Title           = preset.Title,
                ButtonText      = preset.ButtonText,
                ButtonHighlightBg = preset.ButtonHighlightBg,
                ButtonHighlightFg = preset.ButtonHighlightFg,
                ButtonBorder    = preset.ButtonBorder,
                SpectrumBar     = preset.SpectrumBar,
                AuthorText      = preset.AuthorText,
                NetworkStatus   = preset.NetworkStatus,
                Font            = "Segoe UI",
            };
            var boot = new BootSplashPalette
            {
                Bg       = preset.Boot.Bg,
                Primary  = preset.Boot.Primary,
                Dim      = preset.Boot.Dim,
                Bright   = preset.Boot.Bright,
                Tag      = preset.Boot.Tag,
                Init     = preset.Boot.Init,
                Warn     = preset.Boot.Warn,
                PhosphorTint = preset.Boot.PhosphorTint,
                ScanlineAlpha  = preset.Boot.ScanlineAlpha,
                VignetteAlpha  = preset.Boot.VignetteAlpha,
                CrtEffects     = true,
                Font           = "Courier New",
            };
            return (launcher, boot);
        }

        // ── Resolved palette types ──────────────────────────────────────

        public sealed class LauncherPalette
        {
            public Color SelectionBorder { get; init; } = Color.CornflowerBlue;
            public Color HoverOutline    { get; init; } = Color.FromArgb(200, 200, 200);
            public Color Title           { get; init; } = Color.White;
            public Color ButtonText      { get; init; } = Color.White;
            public Color ButtonHighlightBg { get; init; } = Color.White;
            public Color ButtonHighlightFg { get; init; } = Color.Black;
            public Color ButtonBorder    { get; init; } = Color.Gray;
            public Color SpectrumBar     { get; init; } = Color.White;
            public Color AuthorText      { get; init; } = Color.FromArgb(140, 255, 255, 255);
            public Color NetworkStatus   { get; init; } = Color.FromArgb(220, 180, 255, 180);
            public string Font           { get; init; } = "Segoe UI";
        }

        public sealed class BootSplashPalette
        {
            public Color Bg       { get; init; } = Color.Black;
            public Color Primary  { get; init; } = Color.FromArgb(0, 210, 70);
            public Color Dim      { get; init; } = Color.FromArgb(0, 110, 35);
            public Color Bright   { get; init; } = Color.FromArgb(120, 255, 140);
            public Color Tag      { get; init; } = Color.FromArgb(0, 200, 220);
            public Color Init     { get; init; } = Color.FromArgb(210, 195, 0);
            public Color Warn     { get; init; } = Color.FromArgb(255, 140, 0);
            public Color PhosphorTint { get; init; } = Color.FromArgb(12, 0, 60, 10);
            public int   ScanlineAlpha  { get; init; } = 55;
            public int   VignetteAlpha  { get; init; } = 120;
            public bool  CrtEffects     { get; init; } = true;
            public string Font          { get; init; } = "Courier New";
        }

        // ── Private helpers ─────────────────────────────────────────────

        private static void LoadPrivateFonts()
        {
            var fontsDir = Path.Combine(AppContext.BaseDirectory, "Media", "Fonts");
            if (!Directory.Exists(fontsDir)) return;
            foreach (var file in Directory.GetFiles(fontsDir, "*.*", SearchOption.TopDirectoryOnly))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is ".ttf" or ".otf")
                {
                    try { _privateFonts.AddFontFile(file); }
                    catch { /* skip invalid font files */ }
                }
            }
        }

        private static Color Resolve(string? configValue, Color presetDefault)
        {
            if (string.IsNullOrWhiteSpace(configValue)) return presetDefault;
            try { return ColorTranslator.FromHtml(configValue); }
            catch { return presetDefault; }
        }

        private static string ResolveFont(string? configValue, string fallback)
        {
            if (string.IsNullOrWhiteSpace(configValue)) return fallback;
            // Check system fonts
            if (FontFamily.Families.Any(f => f.Name.Equals(configValue, StringComparison.OrdinalIgnoreCase)))
                return configValue;
            // Check private fonts
            if (_privateFonts.Families.Any(f => f.Name.Equals(configValue, StringComparison.OrdinalIgnoreCase)))
                return configValue;
            DebugLogger.Warn("THEME", $"Font '{configValue}' not found, using '{fallback}'");
            return fallback;
        }

        // ── Preset data ─────────────────────────────────────────────────

        private sealed class BootSplashPresetData
        {
            public Color Bg       { get; init; } = Color.Black;
            public Color Primary  { get; init; } = Color.FromArgb(0, 210, 70);
            public Color Dim      { get; init; } = Color.FromArgb(0, 110, 35);
            public Color Bright   { get; init; } = Color.FromArgb(120, 255, 140);
            public Color Tag      { get; init; } = Color.FromArgb(0, 200, 220);
            public Color Init     { get; init; } = Color.FromArgb(210, 195, 0);
            public Color Warn     { get; init; } = Color.FromArgb(255, 140, 0);
            public Color PhosphorTint { get; init; } = Color.FromArgb(12, 0, 60, 10);
            public int   ScanlineAlpha  { get; init; } = 55;
            public int   VignetteAlpha  { get; init; } = 120;
        }

        private sealed class PresetData
        {
            public Color SelectionBorder { get; init; } = Color.CornflowerBlue;
            public Color HoverOutline    { get; init; } = Color.FromArgb(200, 200, 200);
            public Color Title           { get; init; } = Color.White;
            public Color ButtonText      { get; init; } = Color.White;
            public Color ButtonHighlightBg { get; init; } = Color.White;
            public Color ButtonHighlightFg { get; init; } = Color.Black;
            public Color ButtonBorder    { get; init; } = Color.Gray;
            public Color SpectrumBar     { get; init; } = Color.White;
            public Color AuthorText      { get; init; } = Color.FromArgb(140, 255, 255, 255);
            public Color NetworkStatus   { get; init; } = Color.FromArgb(220, 180, 255, 180);
            public BootSplashPresetData Boot { get; init; } = new();
        }

        // ── Main presets ────────────────────────────────────────────────
        private static readonly Dictionary<string, PresetData> Presets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["neon-green"] = new PresetData
            {
                // The original hardcoded look
            },
            ["amber-crt"] = new PresetData
            {
                SelectionBorder = Color.FromArgb(255, 176, 0),       // warm amber
                HoverOutline    = Color.FromArgb(180, 150, 100),
                Title           = Color.FromArgb(255, 220, 160),
                ButtonText      = Color.FromArgb(255, 220, 160),
                ButtonHighlightBg = Color.FromArgb(255, 176, 0),
                ButtonHighlightFg = Color.Black,
                ButtonBorder    = Color.FromArgb(180, 140, 80),
                SpectrumBar     = Color.FromArgb(255, 200, 80),
                AuthorText      = Color.FromArgb(140, 255, 210, 140),
                NetworkStatus   = Color.FromArgb(220, 255, 200, 120),
                Boot = new BootSplashPresetData
                {
                    Primary  = Color.FromArgb(255, 176, 0),
                    Dim      = Color.FromArgb(140, 100, 0),
                    Bright   = Color.FromArgb(255, 230, 140),
                    Tag      = Color.FromArgb(255, 255, 255),
                    Init     = Color.FromArgb(255, 220, 80),
                    Warn     = Color.FromArgb(255, 80, 0),
                    PhosphorTint = Color.FromArgb(12, 60, 40, 0),
                },
            },
            ["synthwave"] = new PresetData
            {
                SelectionBorder = Color.FromArgb(255, 0, 128),       // hot pink
                HoverOutline    = Color.FromArgb(180, 0, 255),
                Title           = Color.FromArgb(0, 255, 255),       // cyan
                ButtonText      = Color.FromArgb(255, 100, 200),
                ButtonHighlightBg = Color.FromArgb(255, 0, 128),
                ButtonHighlightFg = Color.White,
                ButtonBorder    = Color.FromArgb(180, 0, 255),
                SpectrumBar     = Color.FromArgb(255, 0, 200),
                AuthorText      = Color.FromArgb(140, 200, 100, 255),
                NetworkStatus   = Color.FromArgb(220, 0, 255, 200),
                Boot = new BootSplashPresetData
                {
                    Bg       = Color.FromArgb(10, 0, 18),
                    Primary  = Color.FromArgb(180, 0, 255),
                    Dim      = Color.FromArgb(80, 0, 120),
                    Bright   = Color.FromArgb(255, 100, 255),
                    Tag      = Color.FromArgb(0, 255, 255),
                    Init     = Color.FromArgb(255, 0, 180),
                    Warn     = Color.FromArgb(255, 200, 0),
                    PhosphorTint = Color.FromArgb(12, 40, 0, 60),
                },
            },
            ["ice-blue"] = new PresetData
            {
                SelectionBorder = Color.FromArgb(100, 180, 255),
                HoverOutline    = Color.FromArgb(150, 180, 210),
                Title           = Color.FromArgb(200, 220, 255),
                ButtonText      = Color.FromArgb(200, 220, 255),
                ButtonHighlightBg = Color.FromArgb(100, 180, 255),
                ButtonHighlightFg = Color.Black,
                ButtonBorder    = Color.FromArgb(100, 140, 180),
                SpectrumBar     = Color.FromArgb(140, 200, 255),
                AuthorText      = Color.FromArgb(140, 180, 210, 255),
                NetworkStatus   = Color.FromArgb(220, 160, 200, 255),
                Boot = new BootSplashPresetData
                {
                    Primary  = Color.FromArgb(0, 160, 255),
                    Dim      = Color.FromArgb(0, 80, 140),
                    Bright   = Color.FromArgb(140, 210, 255),
                    Tag      = Color.FromArgb(255, 255, 255),
                    Init     = Color.FromArgb(0, 200, 220),
                    Warn     = Color.FromArgb(255, 180, 0),
                    PhosphorTint = Color.FromArgb(12, 0, 20, 60),
                },
            },
            ["minimal-dark"] = new PresetData
            {
                SelectionBorder = Color.White,
                HoverOutline    = Color.FromArgb(100, 100, 100),
                Title           = Color.FromArgb(220, 220, 220),
                ButtonText      = Color.FromArgb(200, 200, 200),
                ButtonHighlightBg = Color.White,
                ButtonHighlightFg = Color.Black,
                ButtonBorder    = Color.FromArgb(80, 80, 80),
                SpectrumBar     = Color.FromArgb(180, 180, 180),
                AuthorText      = Color.FromArgb(100, 180, 180, 180),
                NetworkStatus   = Color.FromArgb(180, 200, 200, 200),
                Boot = new BootSplashPresetData
                {
                    Primary  = Color.FromArgb(220, 220, 220),
                    Dim      = Color.FromArgb(100, 100, 100),
                    Bright   = Color.White,
                    Tag      = Color.FromArgb(160, 160, 160),
                    Init     = Color.FromArgb(200, 200, 200),
                    Warn     = Color.FromArgb(255, 200, 80),
                    PhosphorTint = Color.FromArgb(0, 0, 0, 0),  // no tint
                    ScanlineAlpha = 0,                           // no scanlines
                    VignetteAlpha = 0,                           // no vignette
                },
            },
        };

        // ── Boot splash sub-presets (standalone) ────────────────────────
        private static readonly Dictionary<string, BootSplashPresetData> BootPresets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["green-crt"] = new BootSplashPresetData
            {
                // Same as neon-green boot defaults (original look)
            },
            ["amber-crt"] = Presets["amber-crt"].Boot,
            ["blue-crt"] = Presets["ice-blue"].Boot,
            ["purple-crt"] = new BootSplashPresetData
            {
                Bg       = Color.FromArgb(10, 0, 18),
                Primary  = Color.FromArgb(176, 96, 255),
                Dim      = Color.FromArgb(80, 40, 140),
                Bright   = Color.FromArgb(220, 180, 255),
                Tag      = Color.FromArgb(0, 220, 220),
                Init     = Color.FromArgb(255, 100, 200),
                Warn     = Color.FromArgb(255, 200, 0),
                PhosphorTint = Color.FromArgb(12, 30, 0, 50),
            },
            ["clean-white"] = new BootSplashPresetData
            {
                Primary  = Color.FromArgb(224, 224, 224),
                Dim      = Color.FromArgb(100, 100, 100),
                Bright   = Color.White,
                Tag      = Color.FromArgb(160, 160, 160),
                Init     = Color.FromArgb(200, 200, 200),
                Warn     = Color.FromArgb(255, 200, 80),
                PhosphorTint = Color.FromArgb(0, 0, 0, 0),
                ScanlineAlpha = 0,
                VignetteAlpha = 0,
            },
            ["matrix"] = new BootSplashPresetData
            {
                Primary  = Color.FromArgb(0, 255, 65),
                Dim      = Color.FromArgb(0, 140, 30),
                Bright   = Color.FromArgb(0, 255, 65),
                Tag      = Color.FromArgb(0, 200, 50),
                Init     = Color.FromArgb(100, 255, 100),
                Warn     = Color.FromArgb(255, 255, 0),
                PhosphorTint = Color.FromArgb(18, 0, 80, 10),
                ScanlineAlpha = 70,
                VignetteAlpha = 0,
            },
        };
    }
}
