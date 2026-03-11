using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibVLCSharp.Shared;

namespace ArcadeShellSelector
{
    public class MusicPlayer : IDisposable
    {
        private readonly object _lock = new();
        private LibVLC? _libVlc;
        private MediaPlayer? _mediaPlayer;
        private bool _disposed;
        private volatile bool _stopped;

        public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;
        public bool HasTracks => _tracks.Count > 0;
        public string? LastError { get; private set; }
        public string? CurrentTrackPath { get; private set; }
        public int ConfiguredVolume => _configuredVolume;

        public void SetVolume(int volume)
        {
            try { if (_mediaPlayer != null) _mediaPlayer.Volume = Math.Clamp(volume, 0, 200); } catch { }
        }

        private readonly List<string> _tracks;
        private Media? _currentMedia;
        private string? _lastError;
        private readonly string? _audioDevice;
        private readonly int _configuredVolume;
        private readonly bool _playRandom;
        private readonly string? _selectedFile;

        private void LogDebug(string msg) => DebugLogger.Log("MUSIC", msg);

        public MusicPlayer(string baseDirectory, MusicConfig musicConfig)
        {
            _libVlc = LibVLCManager.Instance;
            _mediaPlayer = new MediaPlayer(_libVlc);

            // Resolve music root
            string root;
            if (string.IsNullOrWhiteSpace(musicConfig.MusicRoot))
                root = baseDirectory;
            else if (Path.IsPathRooted(musicConfig.MusicRoot) || musicConfig.MusicRoot.StartsWith("\\\\"))
                root = musicConfig.MusicRoot;
            else
                root = Path.Combine(baseDirectory, musicConfig.MusicRoot);

            var exts = new[] { ".ogg", ".mod", ".xm" };

            _tracks = Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(p => exts.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase))
                    .ToList()
                : new List<string>();

            // When a track ends: pick a new random track in random mode, otherwise loop the current one.
            _mediaPlayer.EndReached += (_, __) =>
            {
                if (_stopped) return;
                try
                {
                    if (_playRandom && _tracks.Count > 0)
                    {
                        // Avoid repeating the same track when there are alternatives
                        var current = CurrentTrackPath;
                        var candidates = _tracks.Count > 1
                            ? _tracks.Where(t => !string.Equals(t, current, StringComparison.OrdinalIgnoreCase)).ToList()
                            : _tracks;
                        var next = candidates[new Random().Next(candidates.Count)];
                        PlayPath(next);
                    }
                    else
                    {
                        PlayCurrent();
                    }
                }
                catch { }
            };
            _mediaPlayer.EncounteredError += (_, __) => { _lastError = "Playback encountered an error."; try { LogDebug("Player event: EncounteredError"); } catch { } };
            _mediaPlayer.Playing += (_, __) => { _lastError = null; try { LogDebug("Player event: Playing"); } catch { } };
            _mediaPlayer.Paused += (_, __) => { try { LogDebug("Player event: Paused"); } catch { } };
            _mediaPlayer.Buffering += (_, a) => { try { LogDebug($"Player event: Buffering {a}%"); } catch { } };

            // volume from config (remember for enforcement after play)
            _configuredVolume = Math.Clamp(musicConfig.Volume, 0, 100);
            try
            {
                if (_mediaPlayer != null)
                    _mediaPlayer.Volume = _configuredVolume;
            }
            catch { }

            // preferred audio device (optional)
            _audioDevice = string.IsNullOrWhiteSpace(musicConfig.AudioDevice) ? null : musicConfig.AudioDevice;

            // Route to the configured audio device using VLC's own device enumeration.
            // VLC's :audio-device= option requires the Windows endpoint GUID, NOT the friendly name.
            // SetAudioOutput + SetOutputDevice is the correct LibVLCSharp API.
            try
            {
                _mediaPlayer.SetAudioOutput("mmdevice");
                if (!string.IsNullOrWhiteSpace(_audioDevice))
                {
                    var vlcDevs = _libVlc?.AudioOutputDevices("mmdevice");
                    if (vlcDevs != null)
                    {
                        foreach (var d in vlcDevs)
                        {
                            if (string.Equals(d.Description, _audioDevice, StringComparison.OrdinalIgnoreCase))
                            {
                                _mediaPlayer.SetOutputDevice(d.DeviceIdentifier);
                                LogDebug($"Audio device set: {d.Description} ({d.DeviceIdentifier})");
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { try { LogDebug("Audio device setup failed: " + ex.Message); } catch { } }

            // playback mode
            _playRandom = musicConfig.PlayRandom;
            _selectedFile = musicConfig.SelectedFile;

            // log discovered tracks
            try { LogDebug($"MusicPlayer initialized; tracks={_tracks.Count}"); } catch { }

            // We'll set audio output/device via media options when playing.
        }

        public void Start()
        {
            if (!HasTracks || _disposed) return;

            lock (_lock)
            {
                if (_disposed) return;

                string path;
                if (!_playRandom && !string.IsNullOrWhiteSpace(_selectedFile))
                {
                    // Try to find the selected file in the track list
                    path = _tracks.FirstOrDefault(t =>
                        string.Equals(Path.GetFileName(t), _selectedFile, StringComparison.OrdinalIgnoreCase))
                        ?? _tracks[new Random().Next(0, _tracks.Count)];
                }
                else
                {
                    path = _tracks[new Random().Next(0, _tracks.Count)];
                }

                try { LogDebug($"Start selected track: {path}"); } catch { }
                PlayPath(path);
            }
        }

        // Stop playback fully (tracker formats like .mod/.xm don't support pause)
        public void Stop()
        {
            if (_disposed) return;

            lock (_lock)
            {
                try
                {
                    _stopped = true;
                    if (_mediaPlayer != null)
                    {
                        _mediaPlayer.Stop();
                        return;
                    }
                }
                catch { /* swallow to avoid bubbling to UI */ }
            }
        }

        // Resume playback
        public void Resume()
        {
            if (_disposed) return;

            lock (_lock)
            {
                try
                {
                    _stopped = false;
                    if (_mediaPlayer != null)
                    {
                        // Re-apply configured volume — LibVLC may have lost it across Stop/Play
                        _mediaPlayer.Volume = _configuredVolume;
                        if (!_mediaPlayer.IsPlaying)
                            _mediaPlayer.Play();
                        return;
                    }
                }
                catch { /* ignore errors */ }
            }
        }

        private void LogAudioInfo()
        {
            try
            {
                if (_mediaPlayer != null)
                {
                    LogDebug("Player volume: " + _mediaPlayer.Volume);
                }
            }
            catch { }

            try
            {
                // enumerate libVLC audio outputs/devices if available
                try
                {
                    var outputs = _libVlc?.AudioOutputs;
                    if (outputs != null)
                    {
                        foreach (var o in outputs)
                        {
                            try
                            {
                                LogDebug("AudioOutput: " + o.Name + " (" + o.Description + ")");
                            }
                            catch { }
                            try
                            {
                                var devs = _libVlc?.AudioOutputDevices(o.Name);
                                if (devs != null)
                                {
                                    foreach (var d in devs)
                                    {
                                        try { LogDebug("  Device: " + (d.Description ?? d.ToString())); } catch { }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogDebug("AudioOutputs enumeration failed: " + ex.Message);
                }
            }
            catch { }
        }

        private void PlayPath(string path)
        {
            try
            {
                if (!Path.IsPathRooted(path)) path = Path.Combine(AppContext.BaseDirectory, path);
                if (!File.Exists(path)) return;

                CurrentTrackPath = path;
                var media = new Media(_libVlc, path, FromType.FromPath);
                try { _currentMedia?.Dispose(); } catch { }
                _currentMedia = media;
                try { _currentMedia.AddOption(":no-video"); } catch { }

                _mediaPlayer!.Media = _currentMedia;
                bool started = false;
                try { started = _mediaPlayer?.Play() ?? false; } catch (Exception ex) { _lastError = ex.Message; try { LogDebug("Play exception: " + ex.Message); } catch { } }
                if (!started)
                {
                    _lastError ??= "Failed to start playback (LibVLC returned false).";
                    try { LogDebug("Play returned false for media: " + path); } catch { }
                }
                else
                {
                    try { LogDebug("Play started for media: " + path); } catch { }
                    try
                    {
                        if (_mediaPlayer != null)
                        {
                            _mediaPlayer.Volume = _configuredVolume;
                            LogDebug("Enforced player volume=" + _mediaPlayer.Volume);
                        }
                    }
                    catch { }
                    try { LogDebug("Post-play state=" + _mediaPlayer?.State + " volume=" + (_mediaPlayer?.Volume.ToString() ?? "n/a")); } catch { }
                }
            }
            catch (Exception ex)
            {
                _lastError ??= "Exception while attempting to play media.";
                try { LogDebug("PlayPath exception: " + ex.Message); } catch { }
            }
        }

        private void PlayCurrent()
        {
            if (_currentMedia == null) return;
            try { _mediaPlayer?.Stop(); } catch { }
            try { _mediaPlayer?.Play(); } catch { }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                try { _mediaPlayer?.Stop(); } catch { }
                try { _mediaPlayer?.Dispose(); } catch { }
                try { _libVlc?.Dispose(); } catch { }
            }
        }
    }
}
