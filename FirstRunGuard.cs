using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ArcadeShellSelector
{
    /// <summary>
    /// Runs before anything else in the application.
    /// Detects a first-run / unconfigured state and shows a full-screen prompt
    /// asking the user to open the Configurator.
    /// </summary>
    internal static class FirstRunGuard
    {
        /// <summary>
        /// Returns true if the app is not yet configured (config missing or no options).
        /// </summary>
        public static bool IsFirstRun(string configPath, AppConfig? cfg) =>
            !File.Exists(configPath) || cfg == null || cfg.Options.Count == 0;

        /// <summary>
        /// Shows the first-run prompt and acts on the user's choice.
        /// Returns true if the caller should abort startup (configurator was launched or user closed).
        /// Returns false if the user chose to continue anyway.
        /// </summary>
        public static bool Evaluate(string configPath, AppConfig? cfg)
        {
            if (!IsFirstRun(configPath, cfg))
                return false; // nothing to do — app is properly configured

            bool openConfigurator = ShowPrompt();

            if (openConfigurator)
            {
                var cfgExe = Path.Combine(AppContext.BaseDirectory, "ArcadeShellConfigurator.exe");
                if (File.Exists(cfgExe))
                    Process.Start(new ProcessStartInfo(cfgExe) { UseShellExecute = true });
                else
                    MessageBox.Show(
                        $"No se encontró ArcadeShellConfigurator.exe en:\n{AppContext.BaseDirectory}",
                        "Configurador no encontrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return true; // always abort — app cannot run unconfigured
        }

        // ── Standard Windows dialog prompt ────────────────────────────────────────

        private static bool ShowPrompt()
        {
            var result = false;

            using var form = new Form
            {
                Text            = "ArcadeShell — Primera ejecución",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition   = FormStartPosition.CenterScreen,
                MaximizeBox     = false,
                MinimizeBox     = false,
                TopMost         = true,
                ClientSize      = new Size(500, 210),
            };

            // App icon
            var icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(icoPath))
                form.Icon = new Icon(icoPath);

            var imgPath = Path.Combine(AppContext.BaseDirectory, "Media", "Img", "firstrun.png");
            var icon = new PictureBox
            {
                Image    = File.Exists(imgPath) ? Image.FromFile(imgPath) : SystemIcons.Information.ToBitmap(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(20, 20),
                Size     = new Size(32, 32),
            };

            var lblMessage = new Label
            {
                Text     = "Primera ejecución detectada.\n\n"
                         + "No se encontraron frontends configurados.\n"
                         + "Es necesario configurar la aplicación antes de poder usarla.\n\n"
                         + "¿Deseas abrir el Configurador ahora?",
                Location = new Point(68, 20),
                AutoSize = false,
                Size     = new Size(390, 130),
            };

            var btnConfigure = new Button
            {
                Text         = "Abrir Configuración",
                Size         = new Size(180, 36),
                Location     = new Point(80, 155),
                DialogResult = DialogResult.Yes,
                TabIndex     = 1,
            };

            var btnExit = new Button
            {
                Text         = "Salir",
                Size         = new Size(100, 36),
                Location     = new Point(300, 155),
                DialogResult = DialogResult.No,
                TabIndex     = 0,
            };

            form.CancelButton = btnExit;
            form.Controls.AddRange(new Control[] { icon, lblMessage, btnConfigure, btnExit });
            form.Shown += (_, _) => btnConfigure.Focus();

            result = form.ShowDialog() == DialogResult.Yes;
            return result;
        }
    }
}
