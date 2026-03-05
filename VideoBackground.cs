using System;
using System.IO;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace ArcadeShellSelector
{
    internal sealed class VideoBackground : IDisposable
    {
        private readonly LibVLC _libVlc;
        private readonly MediaPlayer _player;
        private Media? _currentMedia;
        public VideoView View { get; } = null!;
        private bool _disposed;
        private string? _currentPath;
        public bool Available { get; private set; } = true;
        public string? LastError { get; private set; }

        public VideoBackground()
        {
            try
            {
                _libVlc = LibVlcManager.Instance;
                _player = new MediaPlayer(_libVlc);
                View = new VideoView { MediaPlayer = _player };

                _player.EndReached += (_, __) =>
                {
                    // Restart playback to loop by replaying the current path.
                    if (string.IsNullOrWhiteSpace(_currentPath)) return;
                    if (View.IsHandleCreated && View.InvokeRequired)
                    {
                        try { View.BeginInvoke(new Action(() => PlayLoop(_currentPath!))); } catch { }
                    }
                    else
                    {
                        try { PlayLoop(_currentPath!); } catch { }
                    }
                };

                _player.Playing += (_, __) => { try { LogVideoDebug("Player event: Playing"); } catch { } };
                _player.Paused += (_, __) => { try { LogVideoDebug("Player event: Paused"); } catch { } };
                _player.EncounteredError += (_, __) => { try { LogVideoDebug("Player event: EncounteredError"); } catch { } };
                _player.Buffering += (_, a) => { try { LogVideoDebug($"Player event: Buffering {a}%"); } catch { } };

                // Default to muted (video player) by setting volume to 0; prefer also disabling audio on media via option.
                try { _player.Volume = 0; } catch { }
            }
            catch (Exception ex)
            {
                // Preserve instance but mark unavailable and keep error for diagnostics.
                Available = false;
                LastError = ex.Message;
                try
                {
                    // Provide a fallback VideoView so UI code can still add it (but don't assign MediaPlayer).
                    View = new VideoView();
                }
                catch { }
            }
        }

        public bool IsPlaying
        {
            get
            {
                try { return _player.IsPlaying; } catch { return false; }
            }
        }

        public void PlayLoop(string path)
        {
            if (!Available) return;
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!Path.IsPathRooted(path)) path = Path.Combine(AppContext.BaseDirectory, path);
            if (!File.Exists(path)) return;

            _currentPath = path;

            try
            {
                // Create media and disable audio for the video so it doesn't interfere with background music.
                var media = new Media(_libVlc, path, FromType.FromPath);
                try { media.AddOption(":no-audio"); } catch { }
                try { _currentMedia?.Dispose(); } catch { }
                _currentMedia = media; // keep reference alive while playing
                _player.Media = _currentMedia;

                // Ensure VideoView control is created so the native video surface can initialize.
                try { if (View != null) { View.CreateControl(); View.Visible = true; } } catch { }

                var started = false;
                try { started = _player.Play(); } catch (Exception ex) { LastError = ex.Message; try { LogVideoDebug("Play exception: " + ex.Message); } catch { } }
                try { LogVideoDebug($"PlayLoop path={path} started={started} isPlaying={_player.IsPlaying} state={_player.State}"); } catch { }
            }
            catch
            {
                // swallow playback errors; caller should handle logging if desired
            }
        }

        private void LogVideoDebug(string msg) => DebugLogger.Log("VIDEO", msg);

        public void Stop()
        {
            try { _player.Stop(); } catch { }
        }

        public void Pause()
        {
            try { if (_player.IsPlaying) _player.SetPause(true); } catch { }
        }

        public void Resume()
        {
            try { if (!_player.IsPlaying) _player.SetPause(false); } catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _player.Stop(); } catch { }
            try { _player.Dispose(); } catch { }
            // Do not dispose shared LibVLC instance here; it's owned by LibVlcManager for app lifetime.
            try { View.Dispose(); } catch { }
        }
    }
}
