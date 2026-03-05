using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace ArcadeShellSelector
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Probe the lib\ subfolder for dependency assemblies.
            // This MUST be registered before any method that references
            // types from those assemblies is JIT-compiled.
            var libDir = Path.Combine(AppContext.BaseDirectory, "lib");
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var name = new AssemblyName(args.Name).Name;
                var dll = Path.Combine(libDir, name + ".dll");
                return File.Exists(dll) ? Assembly.LoadFrom(dll) : null;
            };

            RunApp();
        }

        // Separate method so the JIT doesn't resolve Form1's dependency
        // assemblies until after AssemblyResolve is registered.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RunApp()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}