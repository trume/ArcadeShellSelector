using System;
using System.IO;

namespace ArcadeShellSelector
{
    internal static class DebugLogger
    {
        private static bool _enabled;
        private static string? _logPath;

        private const long MaxLogSize = 2 * 1024 * 1024; // 2 MB

        public static void Init(bool enabled)
        {
            _enabled = enabled;
            if (enabled)
            {
                _logPath = Path.Combine(AppContext.BaseDirectory, "debug.log");
                RotateIfNeeded();
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (_logPath == null || !File.Exists(_logPath)) return;
                if (new FileInfo(_logPath).Length < MaxLogSize) return;
                var backup = _logPath + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(_logPath, backup);
            }
            catch { }
        }

        /// <summary>Informational message — normal operation traces.</summary>
        public static void Info(string component, string message) => Write("INF", component, message);

        /// <summary>Warning — degraded operation, fallback used, or unexpected but non-fatal condition.</summary>
        public static void Warn(string component, string message) => Write("WRN", component, message);

        /// <summary>Error — operation failed.</summary>
        public static void Error(string component, string message) => Write("ERR", component, message);

        /// <summary>Backward-compatible alias — maps to Info level.</summary>
        public static void Log(string component, string message) => Info(component, message);

        private static void Write(string level, string component, string message)
        {
            if (!_enabled) return;
            try
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] [{component}] {message}";
                File.AppendAllText(_logPath!, line + Environment.NewLine);
            }
            catch { }
        }
    }
}
