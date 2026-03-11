using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Windows.Forms;

namespace ArcadeShellSelector
{
    /// <summary>
    /// Registers lib\ assembly probing via ModuleInitializer so it runs before
    /// any type initializer or JIT compilation that references those assemblies.
    /// </summary>
    internal static class LibProber
    {
        [ModuleInitializer]
        internal static void Init()
        {
            var libDir = Path.Combine(AppContext.BaseDirectory, "lib");
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                var path = Path.Combine(libDir, (name.Name ?? "") + ".dll");
                return File.Exists(path) ? ctx.LoadFromAssemblyPath(path) : null;
            };
        }
    }

    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            RunApp();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunApp()
        {
            ApplicationConfiguration.Initialize();

            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            var (cfg, _) = AppConfig.TryLoadFromFile(configPath);

            // ── First-run guard — must run before anything is shown on screen ────────
            bool isFirstRun = FirstRunGuard.IsFirstRun(configPath, cfg);
            if (FirstRunGuard.Evaluate(configPath, cfg))
                return; // configurator launched or user closed — abort startup
            // ────────────────────────────────────────────────────────────────────────

            var launcher = new Launcher(cfg);

            // BootSplash is skipped on first-run (not yet configured) regardless of the setting.
            if (!isFirstRun && (cfg?.Arranque.BootSplashEnabled ?? true))
            {
                using (var splash = new BootSplash())
                {
                    splash.BuildSequence(cfg);
                    splash.FormClosing += (_, _) =>
                    {
                        launcher.Show();
                        launcher.Activate();
                    };
                    splash.ShowDialog();
                }
            }
            else
            {
                launcher.Show();
                launcher.Activate();
            }

            Application.Run(launcher);
        }
    }
}