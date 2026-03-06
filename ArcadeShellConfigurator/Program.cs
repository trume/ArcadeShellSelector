using System;
using System.Windows.Forms;
using System.IO;

namespace ArcadeShellConfigurator
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            try {
                File.AppendAllText("configurator_startup.log", $"Configurator Main started: {DateTime.Now}\n");
            } catch {}
            MessageBox.Show("Configurator starting", "ArcadeShellConfigurator", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
