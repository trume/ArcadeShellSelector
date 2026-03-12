using System;
using System.Diagnostics;
using System.IO;

namespace ArcadeShellSelector
{
    /// <summary>
    /// Fire-and-forget helper for LEDBlinky arcade LED controller.
    /// Commands: 1 = FE animation, 3 = game start, 6 = FE start, 7 = FE quit.
    /// </summary>
    internal sealed class LedBlinky
    {
        private readonly string _exePath;
        private readonly bool _enabled;

        public LedBlinky(LedBlinkyConfig config)
        {
            _enabled = config.Enabled && !string.IsNullOrWhiteSpace(config.ExePath);
            _exePath = config.ExePath;
        }

        /// <summary>Signal front-end started (command 6).</summary>
        public void FrontEndStart() => Send("6");

        /// <summary>Start LED animation / attract mode (command 1).</summary>
        public void StartAnimation() => Send("1");

        /// <summary>Signal game launch with optional ROM name (command 3).</summary>
        public void GameStart(string? romName = null)
        {
            if (string.IsNullOrWhiteSpace(romName))
                Send("3");
            else
                Send($"3 {romName}");
        }

        /// <summary>Signal front-end quit (command 7).</summary>
        public void FrontEndQuit() => Send("7");

        private void Send(string args)
        {
            if (!_enabled) return;
            try
            {
                DebugLogger.Info("LEDBLINKY", $"Sending: {_exePath} {args}");
                var psi = new ProcessStartInfo
                {
                    FileName = _exePath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_exePath) ?? "."
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                DebugLogger.Error("LEDBLINKY", $"Send failed: {ex.Message}");
            }
        }
    }
}
