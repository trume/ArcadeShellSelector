using System;
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
    }
}
