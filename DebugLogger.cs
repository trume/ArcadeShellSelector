using System;
using System.IO;

namespace ArcadeShellSelector
{
    internal static class DebugLogger
    {
        private static bool _enabled;
        private static string? _logPath;

        public static void Init(bool enabled)
        {
            _enabled = enabled;
            if (enabled)
                _logPath = Path.Combine(AppContext.BaseDirectory, "debug.log");
        }

        public static void Log(string component, string message)
        {
            if (!_enabled) return;
            try
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{component}] {message}";
                File.AppendAllText(_logPath!, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
