using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SharpDX.XInput;

namespace ArcadeShellSelector
{
   
    
    public partial class Launcher : Form
    {
        private Controller xinputController;
        private System.Windows.Forms.Timer xinputTimer;
        private Form? _overlayForm;
        private GamepadButtonFlags lastButtons;
        private readonly AppConfig config;
        private readonly List<(PictureBox Pic, Label Label, string ExePath, string? WaitForProcessName)> optionUis = new();
        private readonly Dictionary<PictureBox, Rectangle> _originalBounds = new();

        private MusicPlayer? musicPlayer;
        private VideoBackground? videoBackground;
        private SpectrumAnalyzer? spectrumAnalyzer;
        private SpectrumPanel? spectrumPanel;
        private Form? _spectrumForm;
        private Button closeButton = null!;
        private Label titleLabel = null!;
        private Label AutorApp = null!;
        private PictureBox? autorIcon;
        private PictureBox? selectedPic;
        private bool _childRunning;
        private Task? _resumeTask;
        private CancellationTokenSource? _musicDiagCts;

        public Launcher()
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            var (cfg, err) = AppConfig.TryLoadFromFile(configPath);
            if (cfg == null)
            {
                MessageBox.Show(err ?? "Unknown config error.", "Config error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Environment.Exit(1);
            }
            config = cfg;
            DebugLogger.Init(config.Activa.Activa);
            // Initialize music (random track from Music folder) if enabled in config.
            try
            {
                if (config.Music != null && config.Music.Enabled)
                {
                    var mp = new MusicPlayer(AppContext.BaseDirectory, config.Music);
                    if (mp.HasTracks)
                    {
                        musicPlayer = mp;
                    }
                    else
                    {
                        mp.Dispose();
                    }
                }
            }
            catch { }

            InitializeForm();
            InitializeControls();

            // Initialize XInput
            xinputController = new Controller(UserIndex.One);
            xinputTimer = new System.Windows.Forms.Timer();
            xinputTimer.Interval = 100; // Poll every 100ms
            xinputTimer.Tick += XinputTimer_Tick;
            xinputTimer.Start();

        }

        private void XinputTimer_Tick(object? sender, EventArgs e)
        {
            if (!xinputController.IsConnected)
                return;

            var state = xinputController.GetState();
            var buttons = state.Gamepad.Buttons;

            // Navigation: DPad Left/Right
            if ((buttons & GamepadButtonFlags.DPadLeft) != 0 && (lastButtons & GamepadButtonFlags.DPadLeft) == 0)
                MoveSelection(-1);
            if ((buttons & GamepadButtonFlags.DPadRight) != 0 && (lastButtons & GamepadButtonFlags.DPadRight) == 0)
                MoveSelection(1);

            // Select: A button
            if ((buttons & GamepadButtonFlags.A) != 0 && (lastButtons & GamepadButtonFlags.A) == 0)
                SelectCurrentOption();

            // Close: B button or Start
            if ((buttons & GamepadButtonFlags.B) != 0 && (lastButtons & GamepadButtonFlags.B) == 0)
                Close();

            if ((buttons & GamepadButtonFlags.Start) != 0 && (lastButtons & GamepadButtonFlags.Start) == 0)
                Close();

            lastButtons = buttons;
        }

        private void MoveSelection(int direction)
        {
            if (optionUis.Count == 0) return;
            int idx = selectedPic == null ? 0 : optionUis.FindIndex(x => x.Pic == selectedPic);
            idx = (idx + direction + optionUis.Count) % optionUis.Count;
            selectedPic = optionUis[idx].Pic;
            RefreshSelectionVisuals();
        }

        private void SelectCurrentOption()
        {
            if (_childRunning) return;
            if (selectedPic == null) return;
            var opt = optionUis.FirstOrDefault(x => x.Pic == selectedPic);
            if (opt.Pic != null)
                _ = OnOptionClickedAsync(opt.Pic, opt.ExePath, opt.WaitForProcessName);
        }

        private void InitializeForm()
        {
            Text = "ArcadeLauncher";
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            TopMost = config.Ui.TopMost;
            BackColor = Color.FromArgb(30, 30, 30);
            KeyPreview = true;

            KeyDown += MainForm_KeyDown;
            Load += MainForm_Load;
            Resize += MainForm_Resize;
            Move += (_, __) => SyncOverlayBounds();
        }

        private void InitializeControls()
        {
            // Initialize video background (resilient - exposes diagnostics)
            videoBackground = new VideoBackground();

            // Add the video surface first so overlays can be layered above it.
            try
            {
                var vbView = videoBackground.View;
                vbView.Dock = DockStyle.Fill;
                vbView.Visible = true;
                Controls.Add(vbView);
                try { vbView.SendToBack(); } catch { }
            }
            catch { }

            // Create a transparent overlay form for all UI controls.
            // WinForms Color.Transparent only paints the parent's BackColor (dark gray),
            // so we use a separate form with TransparencyKey to achieve true transparency
            // over the LibVLC video surface.
            _overlayForm = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                BackColor = Color.Magenta,
                TransparencyKey = Color.Magenta,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                KeyPreview = true,
            };
            // Route keyboard from overlay to the main form's handler,
            // but call Close() on the *main* form, not the overlay.
            _overlayForm.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    EnsureExplorerRunning();
                    this.Close();
                }
            };

            titleLabel = new Label
            {
                Text = config.Ui.Title,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Font = new Font("Segoe UI", 24, FontStyle.Bold)
            };
            AddOverlayControl(titleLabel);

            foreach (var opt in config.Options)
            {
                var resolvedExe = ResolvePath(opt.Exe, config.Paths.ToolsRoot);
                var resolvedImg = ResolvePath(opt.Image, config.Paths.ImagesRoot);

                // read optional waitForProcessName property via reflection so config remains backward-compatible
                string? waitName = null;
                try
                {
                    var t = opt.GetType();
                    var p = t.GetProperty("WaitForProcessName") ?? t.GetProperty("waitForProcessName") ?? t.GetProperty("waitForProcess") ;
                    if (p != null) waitName = p.GetValue(opt) as string;
                }
                catch { waitName = null; }

                var pic = CreatePictureBox(resolvedImg);
                var lbl = CreateOptionLabel(opt.Label);

                optionUis.Add((pic, lbl, resolvedExe, waitName));

                WirePictureBox(pic, resolvedExe, waitName);
                AddOverlayControl(pic);
                AddOverlayControl(lbl);
            }

            AutorApp = new Label
            {
                Text = config.Autor.Quien.ToString(),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = false,              
                Font = new Font("Segoe UI", 12, FontStyle.Regular)
            };
            AddOverlayControl(AutorApp);

            // App icon next to author
            var icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
            if (File.Exists(icoPath))
            {
                autorIcon = new PictureBox
                {
                    SizeMode = PictureBoxSizeMode.Zoom,
                    BackColor = Color.Transparent,
                    BorderStyle = BorderStyle.None,
                };
                try { autorIcon.Image = new Icon(icoPath).ToBitmap(); } catch { }
                AddOverlayControl(autorIcon);
            }

            closeButton = new Button
            {
                Text = "Salir / Exit",
                Width = 140,
                Height = 44,
                Font = new Font("Segoe UI", 12F, FontStyle.Regular),
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                FlatStyle = FlatStyle.Flat
            };
            closeButton.FlatAppearance.BorderColor = Color.Gray;
            closeButton.FlatAppearance.BorderSize = 1;
            closeButton.Click += CloseButton_Click;
            AddOverlayControl(closeButton);

            // Spectrum analyzer — WASAPI loopback, no LibVLC interference
            spectrumAnalyzer = new SpectrumAnalyzer();
            spectrumPanel = new SpectrumPanel(spectrumAnalyzer)
            {
                Dock = DockStyle.Fill,
            };

            // Dedicated form for spectrum: transparent background, bars only
            // Uses click-through style so it never steals clicks from the overlay.
            _spectrumForm = new ClickThroughForm
            {
                FormBorderStyle = FormBorderStyle.None,
                BackColor = Color.Magenta,
                TransparencyKey = Color.Magenta,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Opacity = 0.20,
            };
            _spectrumForm.Controls.Add(spectrumPanel);

        }

        private void AddOverlayControl(Control control)
        {
            if (control == null) return;
            if (_overlayForm != null)
                _overlayForm.Controls.Add(control);
            else
                Controls.Add(control);
        }

        private void SyncOverlayBounds()
        {
            if (_overlayForm == null) return;
            _overlayForm.Bounds = this.Bounds;
        }

        private void SyncSpectrumFormBounds()
        {
            if (_spectrumForm == null) return;
            _spectrumForm.Bounds = this.Bounds;
        }

        private void WirePictureBox(PictureBox pb, string exePath, string? waitForProcessName)
         {
             pb.MouseEnter += PicHoverEnter;
             pb.MouseLeave += PicHoverLeave;
             pb.Click += async (s, e) => await OnOptionClickedAsync(pb, exePath, waitForProcessName);
         }

        private PictureBox CreatePictureBox(string imagePath)
        {
            var pb = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.Hand,
                BorderStyle = BorderStyle.None,
                BackColor = Color.Transparent

            };

            var img = LoadImageNoLock(imagePath);
            if (img != null) pb.Image = img;

            // Custom paint for thicker selected border
            pb.Paint += Pb_Paint;

            return pb;
        }

        private static string ResolvePath(string maybeRelative, string baseDir)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative))
                return "";

            if (Path.IsPathRooted(maybeRelative) || maybeRelative.StartsWith(@"\\"))
                return maybeRelative;

            if (string.IsNullOrWhiteSpace(baseDir))
                return maybeRelative;
            // MessageBox.Show($"{baseDir}", "Config error", MessageBoxButtons.OK, MessageBoxIcon.Error);
          
            return Path.Combine(baseDir, maybeRelative);
           
        }

        private static Image? LoadImageNoLock(string path)
        {
            try
            {
                // MessageBox.Show($"{path}", "Config error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Debug.WriteLine($"Attempting to load image from path: {path}");
                if (!File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                using var ms = new MemoryStream(bytes);
                using var temp = Image.FromStream(ms);
                return (Image)temp.Clone();
            }
            catch 
            {
                return null;
            }
        }

        private static Label CreateOptionLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = true,
                Font = new Font("Segoe UI", 14F, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleCenter
            };
        }

        private void MainForm_Load(object? sender, EventArgs e)
        {
            LayoutControls();

            // Show spectrum form FIRST so it sits behind the overlay (between video and UI)
            if (_spectrumForm != null)
            {
                SyncSpectrumFormBounds();
                _spectrumForm.Show(this);
            }

            // Show the transparent overlay form on top — UI controls stay in front
            if (_overlayForm != null)
            {
                SyncOverlayBounds();
                _overlayForm.Show(this);
            }

            TryStartBackground();
            try { musicPlayer?.Start(); } catch { }

            // Start spectrum analyzer after music
            try
            {
                spectrumAnalyzer?.Start();
                spectrumPanel?.StartRefresh();
            }
            catch { }

            // If music fails to start, show a one-time diagnostic so the user knows why.
            try
            {
                if (musicPlayer != null && musicPlayer.HasTracks)
                {
                    _musicDiagCts = new CancellationTokenSource();
                    var ct = _musicDiagCts.Token;
                    _ = Task.Run(async () =>
                    {
                        try { await Task.Delay(900, ct); } catch (OperationCanceledException) { return; }
                        var mp = musicPlayer;
                        if (mp != null && mp.HasTracks && !mp.IsPlaying)
                        {
                            var err = mp.LastError;
                            if (!string.IsNullOrWhiteSpace(err))
                            {
                                try { BeginInvoke(new Action(() => MessageBox.Show(err, "Music error", MessageBoxButtons.OK, MessageBoxIcon.Warning))); } catch { }
                            }
                        }
                    }, ct);
                }
            }
            catch { }
        }

        // Music removed — no playback polling required.
        private void MainForm_Resize(object? sender, EventArgs e) => LayoutControls();

        private void LayoutControls()
        {
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            titleLabel.Location = new Point(
                (w - titleLabel.PreferredWidth) / 2,
                (int)(h * 0.12)
            );

            var count = Math.Max(1, optionUis.Count);
            int maxByHeight = (int)(h * config.Ui.ImageHeightRatio);
            int maxByWidthPerOption = (int)(w * config.Ui.ImageWidthRatioPerOption);
            int imgSize = Math.Max(config.Ui.MinImageSizePx, Math.Min(maxByHeight, maxByWidthPerOption));

            int centerY = (int)(h * 0.50);

            for (int i = 0; i < optionUis.Count; i++)
            {
                var (pic, lbl, _, _) = optionUis[i];

                int posX = (int)(w * (i + 1.0) / (count + 1.0));
                pic.Size = new Size(imgSize, imgSize);
                pic.Location = new Point(posX - pic.Width / 2, centerY - pic.Height / 2);

                // remember original bounds so we can zoom/restore without losing layout
                _originalBounds[pic] = new Rectangle(pic.Location, pic.Size);

                lbl.Location = new Point(
                    pic.Left + (pic.Width - lbl.PreferredWidth) / 2,
                    pic.Bottom + 12
                );
            }

            closeButton.Location = new Point(
                (w - closeButton.Width) / 2,
                h - closeButton.Height - 40
            );

            // Place author label right under the Exit button.
            int autorWidth = Math.Max(180, closeButton.Width + 60);
            int autorHeight = Math.Max(24, AutorApp.Font.Height + 8);
            int iconSize = autorHeight;
            int iconGap = 4;

            if (autorIcon != null)
            {
                autorIcon.Size = new Size(iconSize, iconSize);
                int totalW = iconSize + iconGap + autorWidth;
                int startX = closeButton.Left + (closeButton.Width - totalW) / 2;
                int topY = closeButton.Bottom + 8;

                autorIcon.Location = new Point(startX, topY);
                AutorApp.Size = new Size(autorWidth, autorHeight);
                AutorApp.TextAlign = ContentAlignment.MiddleLeft;
                AutorApp.Location = new Point(startX + iconSize + iconGap, topY);
            }
            else
            {
                AutorApp.Size = new Size(autorWidth, autorHeight);
                AutorApp.TextAlign = ContentAlignment.TopCenter;
                AutorApp.Location = new Point(
                    closeButton.Left + (closeButton.Width - AutorApp.Width) / 2,
                    closeButton.Bottom + 8
                );
            }

            SyncOverlayBounds();
            SyncSpectrumFormBounds();
        }

        private void PicHoverEnter(object? sender, EventArgs e)
        {
            if (sender is PictureBox pb && pb != selectedPic)
                ApplyZoom(pb, 1.10);
        }

        private void PicHoverLeave(object? sender, EventArgs e)
        {
            if (sender is PictureBox pb && pb != selectedPic)
                ResetZoom(pb);
        }

        private void LogLaunch(string msg) => DebugLogger.Log("LAUNCH", msg);

        private async Task OnOptionClickedAsync(PictureBox clickedPic, string exePath, string? waitForProcessName)
        {
            if (_childRunning) return;
            _childRunning = true;

            LogLaunch($"--- OnOptionClickedAsync: exePath={exePath}, waitFor={waitForProcessName}");

            // Wait for any pending resume from the previous launch to complete
            // before calling Stop/Pause (prevents LibVLC race condition)
            if (_resumeTask != null)
            {
                try { await _resumeTask; } catch { }
                _resumeTask = null;
            }

            selectedPic = clickedPic;
            RefreshSelectionVisuals();

            // disable UI while child runs
            foreach (var (pic, _, _, _) in optionUis)
                pic.Enabled = false;
            closeButton.Enabled = false;

            // stop XInput polling so child app gets exclusive gamepad
            try { xinputTimer?.Stop(); } catch { }

            // stop spectrum (lightweight, safe on UI thread)
            try { spectrumPanel?.StopRefresh(); } catch { }
            try { spectrumAnalyzer?.Stop(); } catch { }

            // Hide owned forms BEFORE minimizing to avoid WinForms owned-form restore issues
            try { if (_overlayForm != null) _overlayForm.Visible = false; } catch { }
            try { if (_spectrumForm != null) _spectrumForm.Visible = false; } catch { }

            // Stop music & pause video on background thread (LibVLC Stop() is blocking)
            await Task.Run(() =>
            {
                try { musicPlayer?.Stop(); } catch { }
                try { videoBackground?.Pause(); } catch { }
            });

            // minimize launcher
            try { WindowState = FormWindowState.Minimized; } catch { }

            // run and wait (RunSelectedApp returns error string or null)
            string? error = await Task.Run(() => RunSelectedApp(exePath, waitForProcessName));

            LogLaunch($"--- RunSelectedApp returned, error={error ?? "(none)"}, restoring UI...");

            // Restore window — toggle TopMost to bypass Windows focus-steal prevention
            try
            {
                WindowState = FormWindowState.Maximized;
                TopMost = true;
                Activate();
                if (!config.Ui.TopMost) TopMost = false;
            }
            catch { }

            // Small delay to let WinForms finish processing the maximize before showing owned forms
            await Task.Delay(200);

            // Re-show owned forms
            try
            {
                SyncSpectrumFormBounds();
                if (_spectrumForm != null) _spectrumForm.Visible = true;
                SyncOverlayBounds();
                if (_overlayForm != null)
                {
                    _overlayForm.Visible = true;
                    _overlayForm.BringToFront();
                }
            }
            catch { }

            // Resume video & music on a background thread (store task so next launch can await it)
            _resumeTask = Task.Run(() =>
            {
                try { videoBackground?.Resume(); } catch { }
                try { musicPlayer?.Resume(); } catch { }
            });

            // Restart spectrum
            try { spectrumAnalyzer?.Start(); } catch { }
            try { spectrumPanel?.StartRefresh(); } catch { }

            foreach (var (pic, _, _, _) in optionUis)
                pic.Enabled = true;
            closeButton.Enabled = true;

            // resume XInput polling
            try { xinputTimer?.Start(); } catch { }

            _childRunning = false;

            if (!string.IsNullOrEmpty(error))
            {
                MessageBox.Show(error, "Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
 
        private string? RunSelectedApp(string exePath, string? waitForProcessName = null)
        {
            LogLaunch($"=== RunSelectedApp START === exe={exePath}, waitFor={waitForProcessName}");
            try
            {
                if (string.IsNullOrWhiteSpace(exePath))
                    return "Empty executable path.";

                var isUnc = exePath.StartsWith(@"\\", StringComparison.Ordinal);
                LogLaunch($"isUnc={isUnc}");
                var waitSeconds = Math.Clamp(config.Paths.NetworkWaitSeconds, 0, 120);
                if (waitSeconds > 0 && isUnc)
                {
                    LogLaunch($"Waiting up to {waitSeconds}s for UNC path...");
                    var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
                    while (DateTime.UtcNow < deadline && !File.Exists(exePath))
                    {
                        Thread.Sleep(500);
                    }
                    LogLaunch($"UNC wait done, File.Exists={File.Exists(exePath)}");
                }

                if (!File.Exists(exePath))
                {
                    LogLaunch($"ERROR: File not found: {exePath}");
                    var hint = isUnc
                        ? "\n\nUNC path not reachable yet. If this runs at logon as shell, the network may not be ready. Try increasing paths.networkWaitSeconds or verify the server/share name."
                        : "\n\nConfirm the path exists and you have permissions.";
                    return $"Executable not found:\n{exePath}{hint}";
                }

                LogLaunch("File exists, starting process...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false
                };

                if (!isUnc)
                {
                    startInfo.WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty;
                }

                Process? proc = null;
                try
                {
                    proc = Process.Start(startInfo);
                    LogLaunch($"Process.Start OK, proc.Id={proc?.Id}, proc.ProcessName={TryGetProcessName(proc)}");
                }
                catch (Exception ex1)
                {
                    LogLaunch($"Process.Start FAILED: {ex1.Message}, trying UseShellExecute=true");
                    // fallback: try with shell execute if direct start fails (some shells or associations require it)
                    try
                    {
                        startInfo.UseShellExecute = true;
                        proc = Process.Start(startInfo);
                        LogLaunch($"ShellExecute OK, proc.Id={proc?.Id}");
                    }
                    catch (Exception ex2)
                    {
                        LogLaunch($"ShellExecute also FAILED: {ex2.Message}");
                        proc = null;
                    }
                }

                // Helper: wait for a process by name to appear and then exit.
                // discoverySeconds = how long to wait for it to appear.
                // maxWaitSeconds = total max wait once found.
                void WaitForProcessByName(string processName, int discoverySeconds, int maxWaitSeconds)
                {
                    var name = Path.GetFileNameWithoutExtension(processName);
                    LogLaunch($"WaitForProcessByName: looking for '{name}' (from '{processName}'), discovery={discoverySeconds}s, maxWait={maxWaitSeconds}s");
                    var discoveryDeadline = DateTime.UtcNow.AddSeconds(discoverySeconds);
                    var totalDeadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);

                    // Phase 1: wait for the process to appear
                    bool found = false;
                    int discoveryAttempts = 0;
                    while (DateTime.UtcNow < discoveryDeadline)
                    {
                        discoveryAttempts++;
                        var procs = Process.GetProcessesByName(name);
                        found = procs.Length > 0;
                        if (discoveryAttempts <= 5 || discoveryAttempts % 10 == 0)
                            LogLaunch($"  Discovery attempt #{discoveryAttempts}: GetProcessesByName('{name}') returned {procs.Length} processes");
                        foreach (var p in procs) try { p.Dispose(); } catch { }
                        if (found) break;
                        Thread.Sleep(1000);
                    }

                    LogLaunch($"  Discovery result: found={found} after {discoveryAttempts} attempts");
                    if (!found) return; // process never appeared

                    // Phase 2: wait for all instances with that name to exit
                    LogLaunch("  Phase 2: waiting for process to exit...");
                    int exitChecks = 0;
                    while (DateTime.UtcNow < totalDeadline)
                    {
                        exitChecks++;
                        var procs = Process.GetProcessesByName(name);
                        bool stillRunning = procs.Length > 0;
                        foreach (var p in procs) try { p.Dispose(); } catch { }
                        if (!stillRunning)
                        {
                            LogLaunch($"  Process exited after {exitChecks} checks");
                            return;
                        }
                        if (exitChecks <= 3 || exitChecks % 30 == 0)
                            LogLaunch($"  Exit check #{exitChecks}: still running ({procs.Length} instances)");
                        Thread.Sleep(1000);
                    }
                    LogLaunch($"  Max wait reached ({maxWaitSeconds}s), giving up");
                }

                // Strategy: if waitForProcessName is configured, use it as
                // the primary wait mechanism. This handles both cases:
                //   - Stub launcher that exits immediately and spawns the real process
                //   - Direct launch where the exe IS the real process
                // We don't use proc.WaitForExit() because it can hang for
                // processes launched from network drives.
                if (!string.IsNullOrWhiteSpace(waitForProcessName))
                {
                    LogLaunch($"Using WaitForProcessByName strategy for '{waitForProcessName}'");
                    // Wait up to 90s for the target process to appear,
                    // then up to 2 hours for it to exit.
                    WaitForProcessByName(waitForProcessName, 90, 7200);
                }
                else if (proc != null)
                {
                    LogLaunch("Using proc.WaitForExit strategy (no waitForProcessName)");
                    // No waitForProcessName configured — use proc.WaitForExit with a timeout
                    try { proc.WaitForExit(3600 * 1000); } catch { }
                    LogLaunch("proc.WaitForExit returned");
                }
                else
                {
                    LogLaunch("No proc and no waitForProcessName — sleeping 5s fallback");
                    // No process object and no name to wait for — brief fallback
                    Thread.Sleep(5000);
                }

                LogLaunch("=== RunSelectedApp END (success) ===");
                return null;
            }
            catch (Exception ex)
            {
                LogLaunch($"=== RunSelectedApp END (exception: {ex.Message}) ===");
                var extra = exePath.StartsWith(@"\\", StringComparison.Ordinal)
                    ? "\n\nIf this is a NAS name, try using its IP or a resolvable DNS name (the server name may not be available at logon)."
                    : "";
                return $"Failed to launch:\n{exePath}\n\n{ex.Message}{extra}";
            }
         }

        private static string TryGetProcessName(Process? p)
        {
            if (p == null) return "(null)";
            try { return p.ProcessName; } catch { return "(error)"; }
        }

        private void EnsureExplorerRunning()
        {
            var existing = Process.GetProcessesByName("explorer");
            if (existing == null || existing.Length == 0)
                Process.Start("explorer.exe");
        }

        private void ApplyZoom(PictureBox pb, double scale)
        {
            if (!_originalBounds.TryGetValue(pb, out var rect)) return;
            int newW = (int)(rect.Width * scale);
            int newH = (int)(rect.Height * scale);
            var center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            pb.Size = new Size(newW, newH);
            pb.Location = new Point(center.X - pb.Width / 2, center.Y - pb.Height / 2);
            pb.Invalidate();
        }

        private void ResetZoom(PictureBox pb)
        {
            if (!_originalBounds.TryGetValue(pb, out var rect)) return;
            pb.Size = rect.Size;
            pb.Location = rect.Location;
            pb.Invalidate();
        }

        private void RefreshSelectionVisuals()
        {
            foreach (var (pic, _, _, _) in optionUis)
            {
                if (pic == selectedPic)
                {
                    ApplyZoom(pic, 1.20);
                }
                else
                {
                    ResetZoom(pic);
                }
                pic.Invalidate();
            }
        }

        private void Pb_Paint(object? sender, PaintEventArgs e)
        {
            if (sender is not PictureBox pb) return;

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (pb == selectedPic)
            {
                using var pen = new Pen(Color.CornflowerBlue, 6);
                pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Outset;
                var r = new Rectangle(0, 0, pb.ClientSize.Width - 1, pb.ClientSize.Height - 1);
                e.Graphics.DrawRectangle(pen, r);
            }
            else
            {
                // if currently zoomed (hover), draw a thinner subtle outline
                if (_originalBounds.TryGetValue(pb, out var rect) && (pb.Width != rect.Width || pb.Height != rect.Height))
                {
                    using var pen = new Pen(Color.FromArgb(200, 200, 200), 2);
                    var r = new Rectangle(0, 0, pb.ClientSize.Width - 1, pb.ClientSize.Height - 1);
                    e.Graphics.DrawRectangle(pen, r);
                }
            }
        }

        private void CloseButton_Click(object? sender, EventArgs e)
        {
            EnsureExplorerRunning();
            Close();
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                EnsureExplorerRunning();
                Close();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // cancel any pending music diagnostic task
            try { _musicDiagCts?.Cancel(); } catch { }

            // stop and dispose XInput timer
            xinputTimer?.Stop();
            xinputTimer?.Dispose();

            // Owned forms are auto-closed by WinForms; no need to close overlay manually.

            try { musicPlayer?.Dispose(); } catch { }
            try { spectrumPanel?.StopRefresh(); } catch { }
            try { spectrumAnalyzer?.Dispose(); } catch { }
            // _spectrumForm is owned, so WinForms auto-closes it.

            // Stop video synchronously before dispose to avoid blocking the UI thread.
            try { videoBackground?.Stop(); } catch { }
            try { videoBackground?.Dispose(); } catch { }
            base.OnFormClosed(e);
        }

        private void TryStartBackground()
        {
            try
            {
                // Look for a video file in the Bkg folder and play the first recognised video.
                var bkgDir = Path.Combine(AppContext.BaseDirectory, "Bkg");
                try { if (!Directory.Exists(bkgDir)) Directory.CreateDirectory(bkgDir); } catch { }

                var files = Directory.GetFiles(bkgDir);
                var exts = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ogg" };

                // If Bkg is empty, also look in the app base directory and copy any
                // found video into Bkg so the app references videos from Bkg.
                if (files == null || files.Length == 0)
                {
                    try
                    {
                        var rootFiles = Directory.GetFiles(AppContext.BaseDirectory);
                        var rootVid = rootFiles.FirstOrDefault(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
                        if (!string.IsNullOrWhiteSpace(rootVid))
                        {
                            var dest = Path.Combine(bkgDir, Path.GetFileName(rootVid));
                            try { if (!File.Exists(dest)) File.Copy(rootVid, dest); } catch { }
                            files = Directory.GetFiles(bkgDir);
                        }
                    }
                    catch { }
                }

                var vid = files.FirstOrDefault(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(vid))
                {
                    try { videoBackground?.PlayLoop(vid); } catch { }
                }

                // no status label to update
            }
            catch
            {
                // ignore background startup errors
            }
        }
    }

    /// <summary>
    /// A Form that is completely click-through (WS_EX_TRANSPARENT | WS_EX_LAYERED).
    /// Mouse events pass through to whatever window is behind it.
    /// Used for the spectrum visualizer so it never blocks clicks on the overlay UI.
    /// </summary>
    internal sealed class ClickThroughForm : Form
    {
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
                return cp;
            }
        }
    }
}