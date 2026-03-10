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

            // Load config once here so BootSplash can display real data
            // and Launcher can skip re-reading the file.
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            var (cfg, _) = AppConfig.TryLoadFromFile(configPath);

            // Pre-create the launcher (hidden) while the splash is running so there is
            // no gap between the two windows — the launcher becomes visible the instant
            // the splash closes, without the desktop ever appearing.
            var launcher = new Launcher(cfg);

            using (var splash = new BootSplash())
            {
                splash.BuildSequence(cfg);

                // When the splash is about to close, show the launcher first so both
                // windows exist simultaneously for one paint cycle.
                splash.FormClosing += (_, _) =>
                {
                    launcher.Show();
                    launcher.Activate();
                };

                splash.ShowDialog();
            }

            // Launcher is already visible; hand control to its message loop.
            Application.Run(launcher);
        }
    }
}