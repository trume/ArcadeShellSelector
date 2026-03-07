using System;
using System.Reflection;
using System.IO;
using System.Windows.Forms;

namespace ArcadeShellConfigurator
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new ConfigForm());
        }

        static Program()
        {
            // Managed dependencies (e.g. LibVLCSharp.dll) are stored in lib\ by the main app's
            // MoveDllsToLib build target. Teach the runtime to probe that subfolder.
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var name = new AssemblyName(args.Name!).Name + ".dll";
                var path = Path.Combine(AppContext.BaseDirectory, "lib", name);
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };

            Application.ThreadException += (s, e) =>
                MessageBox.Show($"Unhandled thread exception:\n{e.Exception}", "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show($"Unhandled domain exception:\n{(e.ExceptionObject is Exception ex ? ex.ToString() : e.ExceptionObject)}", "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
