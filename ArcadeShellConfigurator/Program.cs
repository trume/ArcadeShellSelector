using System;
using System.Reflection;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Threading;
using System.Windows.Forms;

namespace ArcadeShellConfigurator
{
    /// <summary>
    /// Module initializer — guaranteed to run before any type initializer or Main().
    /// Registers the lib\ probing hook so SharpDX / NAudio / LibVLCSharp are found
    /// even before the Program static constructor fires.
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
            using var mutex = new Mutex(true, "ArcadeShellConfigurator_SingleInstance", out bool isNew);
            if (!isNew)
                return; // another instance is already running

            ApplicationConfiguration.Initialize();
            Application.Run(new ConfigForm());
        }

        static Program()
        {
            Application.ThreadException += (s, e) =>
                MessageBox.Show($"Unhandled thread exception:\n{e.Exception}", "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show($"Unhandled domain exception:\n{(e.ExceptionObject is Exception ex ? ex.ToString() : e.ExceptionObject)}", "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
