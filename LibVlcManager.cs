using System;
using System.IO;
using System.Linq;
using LibVLCSharp.Shared;

namespace ArcadeShellSelector
{
    public static class LibVLCManager
    {
        private static readonly object _lock = new();
        private static LibVLC? _instance;

        public static LibVLC Instance
        {
            get
            {
                if (_instance != null) return _instance;

                lock (_lock)
                {
                    if (_instance != null) return _instance;

                    // Ensure LibVLCSharp core initialized
                    Core.Initialize();

                    // Prefer native lib folder "lib" inside app directory if present
                    try
                    {
                        var baseDir = AppContext.BaseDirectory;
                        var libDir = Path.Combine(baseDir, "lib");

                        if (Directory.Exists(libDir))
                        {
                            // Prepend libDir to PATH so LibVLC finds native libs (libvlc.dll/libvlccore.dll + plugins)
                            var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                            var parts = current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                            if (!parts.Any(p => string.Equals(p, libDir, StringComparison.OrdinalIgnoreCase)))
                            {
                                var newPath = libDir + Path.PathSeparator + current;
                                Environment.SetEnvironmentVariable("PATH", newPath);
                            }

                            // Optionally set VLC_PLUGIN_PATH
                            Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", Path.Combine(libDir, "plugins"));
                        }
                    }
                    catch
                    {
                        // ignore environment modifications if they fail
                    }

                    // Create LibVLC instance (uses PATH to locate native dlls)
                    _instance = new LibVLC();
                    return _instance;
                }
            }
        }

        public static void DisposeInstance()
        {
            lock (_lock)
            {
                try
                {
                    _instance?.Dispose();
                }
                catch { }
                _instance = null;
            }
        }
    }

    // Adapter/alias para evitar romper referencias existentes (corrige diferencias de mayúsculas)
    public static class LibVlcManager
    {
        public static LibVLC Instance => LibVLCManager.Instance;
        public static void DisposeInstance() => LibVLCManager.DisposeInstance();
    }
}
