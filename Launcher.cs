using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LibVLCSharp.Shared;
using SharpDX.DirectInput;
using SharpDX.XInput;

namespace ArcadeShellSelector
{
   
    
    public partial class Launcher : Form
    {
        private Controller xinputController;
        private System.Windows.Forms.Timer xinputTimer;
        private Form? _overlayForm;
        private GamepadButtonFlags lastButtons;

        // DirectInput (arcade encoders: Xin-Mo, I-PAC, Zero Delay, etc.)
        private DirectInput? _directInput;
        private Joystick? _dinputJoystick;
        private System.Windows.Forms.Timer? _dinputTimer;
        private bool[] _lastDInputButtons = Array.Empty<bool>();
        // Timestamp of the last accepted navigation move — used as a global debounce/cooldown
        // to prevent noisy arcade encoder signals from looping selections erratically.
        private DateTime _lastNavTime = DateTime.MinValue;
        private int _lastDInputAxisDir; // -1 = left, 0 = center, 1 = right
        private bool _lastXInputStickLeft;
        private bool _lastXInputStickRight;
        private readonly AppConfig config;
        private readonly List<(PictureBox Pic, Label Label, string ExePath, string? WaitForProcessName)> optionUis = new();
        private readonly Dictionary<PictureBox, Rectangle> _originalBounds = new();
        private readonly Dictionary<PictureBox, string> _thumbVideoPaths = new();
        private readonly Dictionary<PictureBox, Image> _thumbOriginalImages = new();
        private LibVLC? _thumbLibVlc;
        private MediaPlayer? _thumbPlayer;
        private Media? _thumbMedia;
        private PictureBox? _thumbActivePic;
        private IntPtr _thumbBuffer = IntPtr.Zero;
        private int _thumbW, _thumbH;

        private MusicPlayer? musicPlayer;
        private VideoBackground? videoBackground;
        private SpectrumAnalyzer? spectrumAnalyzer;
        private SpectrumPanel? spectrumPanel;
        private Form? _spectrumForm;
        private LedBlinky? _ledBlinky;
        private Button configButton = null!;
        private Button closeButton = null!;
        private Label titleLabel = null!;
        private Label AutorApp = null!;
        private Label _inputIndicator = null!;
        private PictureBox? autorIcon;
        private PictureBox? selectedPic;
        private int _navIndex = -1; // -1 = no selection; 0..N-1 = options; N = configButton; N+1 = closeButton
        private bool _childRunning;
        private Task? _resumeTask;
        private CancellationTokenSource? _musicDiagCts;
        private System.Windows.Forms.Timer? _zOrderTimer;

        public Launcher(AppConfig? preloadedConfig = null)
        {
            AppConfig? cfg;
            if (preloadedConfig != null)
            {
                cfg = preloadedConfig;
            }
            else
            {
                var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");
                string? loadErr;
                (cfg, loadErr) = AppConfig.TryLoadFromFile(configPath);
                if (cfg == null)
                {
                    MessageBox.Show(loadErr ?? "Unknown config error.", "Config error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Environment.Exit(1);
                    return;
                }
            }
            config = cfg!;
            DebugLogger.Init(config.Activa.Activa);

            InitializeForm();
            InitializeControls();

            // Initialize XInput (only if enabled in config)
            {
                int slot = config.Input.XInputSlot;
                if (slot < 0 || slot > 3)
                {
                    slot = 0; // fallback — find first connected
                    for (int i = 0; i < 4; i++)
                        if (new Controller((UserIndex)i).IsConnected) { slot = i; break; }
                }
                xinputController = new Controller((UserIndex)slot);
            }
            xinputTimer = new System.Windows.Forms.Timer();
            xinputTimer.Interval = 100;
            xinputTimer.Tick += XinputTimer_Tick;
            if (config.Input.XInputEnabled)
            {
                DebugLogger.Log("XInput", xinputController.IsConnected
                    ? "Controller connected (UserIndex.One)"
                    : "No controller connected");
                xinputTimer.Start();
                DebugLogger.Log("XInput", "Timer started.");
            }
            else
            {
                DebugLogger.Log("XInput", "Disabled in config.");
            }

            InitDirectInput();
        }

        private void XinputTimer_Tick(object? sender, EventArgs e)
        {
            if (!xinputController.IsConnected)
                return;

            var state = xinputController.GetState();
            var gp = state.Gamepad;
            var buttons = gp.Buttons;

            var selectFlag = (GamepadButtonFlags)config.Input.XInputButtonSelect;
            var backFlag   = (GamepadButtonFlags)config.Input.XInputButtonBack;
            var leftFlag   = (GamepadButtonFlags)config.Input.XInputButtonLeft;
            var rightFlag  = (GamepadButtonFlags)config.Input.XInputButtonRight;

            // Navigation: configured button or DPad Left/Right (0 = DPad/axis mode)
            if (leftFlag != 0)
            {
                if ((buttons & leftFlag) != 0 && (lastButtons & leftFlag) == 0) MoveSelection(-1);
            }
            else
            {
                if ((buttons & GamepadButtonFlags.DPadLeft) != 0 && (lastButtons & GamepadButtonFlags.DPadLeft) == 0)
                    MoveSelection(-1);
            }
            if (rightFlag != 0)
            {
                if ((buttons & rightFlag) != 0 && (lastButtons & rightFlag) == 0) MoveSelection(1);
            }
            else
            {
                if ((buttons & GamepadButtonFlags.DPadRight) != 0 && (lastButtons & GamepadButtonFlags.DPadRight) == 0)
                    MoveSelection(1);
            }

            // Left stick axis — only active when the corresponding direction is in DPad/axis mode
            const short stickDeadzone = 16000; // ~49% of 32767
            bool stickLeft  = gp.LeftThumbX < -stickDeadzone;
            bool stickRight = gp.LeftThumbX >  stickDeadzone;
            bool wasStickLeft  = _lastXInputStickLeft;
            bool wasStickRight = _lastXInputStickRight;
            _lastXInputStickLeft  = stickLeft;
            _lastXInputStickRight = stickRight;
            if (leftFlag  == 0 && stickLeft  && !wasStickLeft)  MoveSelection(-1);
            if (rightFlag == 0 && stickRight && !wasStickRight) MoveSelection(1);

            // Select
            if ((buttons & selectFlag) != 0 && (lastButtons & selectFlag) == 0)
                SelectCurrentOption();

            // Back/Close
            if ((buttons & backFlag) != 0 && (lastButtons & backFlag) == 0)
                Close();

            // Start always closes as an escape hatch (unless already mapped to it)
            if (backFlag != GamepadButtonFlags.Start)
                if ((buttons & GamepadButtonFlags.Start) != 0 && (lastButtons & GamepadButtonFlags.Start) == 0)
                    Close();

            lastButtons = buttons;
        }

        private static bool IsXInputDevice(DeviceInstance di)
        {
            // XInput devices expose a well-known HID usage page (0x01) and
            // their product GUIDs embed the string "IG_" when created by the
            // XInput driver.  The InstanceGuid trick is the standard way to
            // detect them; the name check is a fallback.
            var id = di.InstanceGuid.ToString();
            if (id.Contains("IG_", StringComparison.OrdinalIgnoreCase))
                return true;
            var pid = di.ProductGuid.ToString();
            if (pid.Contains("IG_", StringComparison.OrdinalIgnoreCase))
                return true;
            // Fallback: name-based filter
            var name = di.ProductName;
            if (name.Contains("XINPUT", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Xbox", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private void InitDirectInput()
        {
            if (!config.Input.DInputEnabled) return;
            try
            {
                _directInput = new DirectInput();
                // Enumerate attached gamepads and joysticks; skip XInput devices
                var devices = _directInput
                    .GetDevices(SharpDX.DirectInput.DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly)
                    .Concat(_directInput.GetDevices(SharpDX.DirectInput.DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly))
                    .Where(di => !IsXInputDevice(di))
                    .ToList();

                if (devices.Count == 0)
                {
                    DebugLogger.Log("DInput", "No non-XInput joystick/gamepad found.");
                    // Log skipped devices for diagnostics
                    var all = _directInput
                        .GetDevices(SharpDX.DirectInput.DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly)
                        .Concat(_directInput.GetDevices(SharpDX.DirectInput.DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly));
                    foreach (var d in all)
                        DebugLogger.Log("DInput", $"Skipped (XInput): {d.ProductName} [{d.InstanceGuid}]");
                    return;
                }

                // Pick preferred device by name if configured; fall back to first found.
                // This lets multi-board arcade cabinets (e.g. two Zero Delay encoders)
                // pin the launcher to the correct controller board.
                var preferred = (!string.IsNullOrWhiteSpace(config.Input.DInputDeviceName)
                    ? devices.FirstOrDefault(d => string.Equals(d.ProductName,
                          config.Input.DInputDeviceName, StringComparison.OrdinalIgnoreCase))
                    : null) ?? devices[0];
                DebugLogger.Log("DInput", $"Using: {preferred.ProductName}{(preferred == devices[0] && !string.IsNullOrWhiteSpace(config.Input.DInputDeviceName) ? " (preferred not found, fell back to first)" : "")}");
                var joystick = new Joystick(_directInput, preferred.InstanceGuid);
                joystick.SetCooperativeLevel(Handle, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                joystick.Acquire();
                _dinputJoystick = joystick;

                _dinputTimer = new System.Windows.Forms.Timer { Interval = 100 };
                _dinputTimer.Tick += DinputTimer_Tick;
                _dinputTimer.Start();
                DebugLogger.Log("DInput", "Timer started.");
            }
            catch (Exception ex)
            {
                DebugLogger.Log("DInput", $"Init failed: {ex.Message}");
            }
        }

        private void DinputTimer_Tick(object? sender, EventArgs e)
        {
            if (_dinputJoystick == null) return;
            try
            {
                _dinputJoystick.Poll();
                var state = _dinputJoystick.GetCurrentState();
                var buttons = state.Buttons;

                if (_lastDInputButtons.Length != buttons.Length)
                    _lastDInputButtons = new bool[buttons.Length];

                var cfg = config.Input;
                int selectIdx = cfg.DInputButtonSelect - 1;
                int backIdx   = cfg.DInputButtonBack   - 1;
                int leftIdx   = cfg.DInputButtonLeft   - 1; // -1 when DInputButtonLeft = 0
                int rightIdx  = cfg.DInputButtonRight  - 1;

                // Button-based left/right (only when explicitly configured, i.e. > 0)
                if (leftIdx >= 0 && leftIdx < buttons.Length && buttons[leftIdx] && !_lastDInputButtons[leftIdx])
                    MoveSelection(-1);
                if (rightIdx >= 0 && rightIdx < buttons.Length && buttons[rightIdx] && !_lastDInputButtons[rightIdx])
                    MoveSelection(1);

                // Axis / POV hat navigation — edge-triggered: fires once on deflection,
                // must return to center before firing again. Prevents auto-sweep when held.
                if (leftIdx < 0 && rightIdx < 0) // skip if buttons cover left/right
                {
                    int axisDir = 0; // current direction: -1 left, 0 center, 1 right

                    // SharpDX JoystickState axes are 0–65535, center ≈ 32767
                    const int center = 32767;
                    const int deadzone = 16384; // ~50% of half-range
                    int xOffset = state.X - center;
                    if (xOffset < -deadzone)
                        axisDir = -1;
                    else if (xOffset > deadzone)
                        axisDir = 1;
                    else
                    {
                        // POV / hat switch (value in 1/100 degrees; -1 = centred)
                        var pov = state.PointOfViewControllers;
                        if (pov != null && pov.Length > 0 && pov[0] != -1)
                        {
                            if (pov[0] > 22500 && pov[0] < 31500)      // ~270° = left
                                axisDir = -1;
                            else if (pov[0] > 4500 && pov[0] < 13500)  // ~90°  = right
                                axisDir = 1;
                        }
                    }

                    // Only fire on transition from center (0) to a direction
                    if (axisDir != 0 && _lastDInputAxisDir == 0)
                        MoveSelection(axisDir);
                    _lastDInputAxisDir = axisDir;
                }

                // Select / confirm
                if (selectIdx >= 0 && selectIdx < buttons.Length && buttons[selectIdx] && !_lastDInputButtons[selectIdx])
                    SelectCurrentOption();

                // Back / close
                if (backIdx >= 0 && backIdx < buttons.Length && buttons[backIdx] && !_lastDInputButtons[backIdx])
                    Close();

                Array.Copy(buttons, _lastDInputButtons, buttons.Length);
            }
            catch (SharpDX.SharpDXException)
            {
                // Device lost — try to re-acquire next tick
                try { _dinputJoystick?.Acquire(); } catch { }
            }
        }

        private void MoveSelection(int direction)
        {
            int totalItems = optionUis.Count + 2; // options + configButton + closeButton
            if (totalItems == 0) return;

            var now = DateTime.UtcNow;
            if ((now - _lastNavTime).TotalMilliseconds < config.Input.NavCooldownMs) return;
            _lastNavTime = now;

            _navIndex = _navIndex < 0 ? 0 : (_navIndex + direction + totalItems) % totalItems;
            selectedPic = _navIndex < optionUis.Count ? optionUis[_navIndex].Pic : null;
            RefreshSelectionVisuals();
        }

        private void SelectCurrentOption()
        {
            if (_childRunning) return;
            if (_navIndex < 0) return;

            if (_navIndex < optionUis.Count)
            {
                var opt = optionUis[_navIndex];
                if (opt.Pic != null)
                    _ = OnOptionClickedAsync(opt.Pic, opt.ExePath, opt.WaitForProcessName);
            }
            else if (_navIndex == optionUis.Count)
            {
                ConfigButton_Click(configButton, EventArgs.Empty);
            }
            else if (_navIndex == optionUis.Count + 1)
            {
                CloseButton_Click(closeButton, EventArgs.Empty);
            }
        }

        private void EnforceZOrder()
        {
            try { _spectrumForm?.SendToBack(); } catch { }
            try { _overlayForm?.BringToFront(); } catch { }
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
            Move += (_, __) => { SyncOverlayBounds(); EnforceZOrder(); };
            Activated += (_, __) => EnforceZOrder();

            // Heartbeat: keep the overlay in front no matter what repaints underneath it.
            _zOrderTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _zOrderTimer.Tick += (_, __) => EnforceZOrder();
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
            var overlayTransKey = Color.FromArgb(1, 1, 1);
            _overlayForm = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                BackColor = overlayTransKey,
                TransparencyKey = overlayTransKey,
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                KeyPreview = true,
                TopMost = config.Ui.TopMost,
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
                Font = new Font("Segoe UI", 28, FontStyle.Bold)
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

                // Store thumb video path for hover preview
                if (!string.IsNullOrWhiteSpace(opt.ThumbVideo))
                {
                    var resolvedThumb = opt.ThumbVideo;
                    if (!Path.IsPathRooted(resolvedThumb))
                        resolvedThumb = Path.Combine(AppContext.BaseDirectory, resolvedThumb);
                    if (File.Exists(resolvedThumb))
                        _thumbVideoPaths[pic] = resolvedThumb;
                }

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

            configButton = new Button
            {
                Text = "Configuración",
                Width = 160,
                Height = 44,
                Font = new Font("Segoe UI", 12F, FontStyle.Regular),
                BackColor = Color.Transparent,
                ForeColor = Color.White,
                UseVisualStyleBackColor = false,
                FlatStyle = FlatStyle.Flat
            };
            configButton.FlatAppearance.BorderColor = Color.Gray;
            configButton.FlatAppearance.BorderSize = 1;
            configButton.Click += ConfigButton_Click;
            WireButtonHover(configButton);
            AddOverlayControl(configButton);

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
            WireButtonHover(closeButton);
            AddOverlayControl(closeButton);

            // Input indicator — shows which input method is active
            string inputText = (config.Input.XInputEnabled && config.Input.DInputEnabled) ? "🎮 XInput + DInput"
                             : config.Input.XInputEnabled ? "🎮 XInput"
                             : config.Input.DInputEnabled ? "🎮 DInput"
                             : "⌨ Keyboard";
            _inputIndicator = new Label
            {
                Text = inputText,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(140, 255, 255, 255),
                BackColor = Color.Transparent,
            };
            AddOverlayControl(_inputIndicator);

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
                BackColor = Color.FromArgb(1, 1, 1),
                TransparencyKey = Color.FromArgb(1, 1, 1),
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
                Font = new Font("Segoe UI", 20F, FontStyle.Bold),
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
                _overlayForm.BringToFront();
            }

            _zOrderTimer?.Start();

            // 1. Video background — visual feedback first
            TryStartBackground();

            // 2. Music — initialize and start playback
            try
            {
                if (config.Music != null && config.Music.Enabled)
                {
                    var mp = new MusicPlayer(AppContext.BaseDirectory, config.Music);
                    if (mp.HasTracks)
                    {
                        musicPlayer = mp;
                        musicPlayer.Start();
                    }
                    else
                    {
                        mp.Dispose();
                    }
                }
            }
            catch { }

            // 3. Spectrum analyzer — depends on audio playing
            try
            {
                spectrumAnalyzer?.Start();
                spectrumPanel?.StartRefresh();
            }
            catch { }

            // 4. LEDBlinky — signal front-end start + animation
            try
            {
                _ledBlinky = new LedBlinky(config.LedBlinky);
                _ledBlinky.FrontEndStart();
                _ledBlinky.StartAnimation();
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

            // --- Author + Close button on one centered line at the bottom ---
            int autorHeight = Math.Max(24, AutorApp.Font.Height + 8);
            int iconSize = autorHeight;
            int iconGap = 4;
            int gap = 16; // gap between author text and close button
            int bottomY = h - Math.Max(closeButton.Height, autorHeight) - 40;

            // Measure total width of the combined line
            using var gfx = CreateGraphics();
            int textW = (int)gfx.MeasureString(AutorApp.Text, AutorApp.Font).Width + 8;
            int autorBlockW = (autorIcon != null ? iconSize + iconGap : 0) + textW;
            int totalLineW = autorBlockW + gap + configButton.Width + gap + closeButton.Width;
            int startX = (w - totalLineW) / 2;

            if (autorIcon != null)
            {
                autorIcon.Size = new Size(iconSize, iconSize);
                autorIcon.Location = new Point(startX, bottomY + (Math.Max(closeButton.Height, autorHeight) - iconSize) / 2);
                AutorApp.Size = new Size(textW, autorHeight);
                AutorApp.TextAlign = ContentAlignment.MiddleLeft;
                AutorApp.Location = new Point(startX + iconSize + iconGap, bottomY + (Math.Max(closeButton.Height, autorHeight) - autorHeight) / 2);
            }
            else
            {
                AutorApp.Size = new Size(textW, autorHeight);
                AutorApp.TextAlign = ContentAlignment.MiddleLeft;
                AutorApp.Location = new Point(startX, bottomY + (Math.Max(closeButton.Height, autorHeight) - autorHeight) / 2);
            }

            configButton.Location = new Point(
                startX + autorBlockW + gap,
                bottomY + (Math.Max(closeButton.Height, autorHeight) - configButton.Height) / 2
            );

            closeButton.Location = new Point(
                configButton.Right + gap,
                bottomY + (Math.Max(closeButton.Height, autorHeight) - closeButton.Height) / 2
            );

            _inputIndicator.Location = new Point(
                closeButton.Right + gap,
                bottomY + (Math.Max(closeButton.Height, autorHeight) - _inputIndicator.Height) / 2
            );

            SyncOverlayBounds();
            SyncSpectrumFormBounds();
            EnforceZOrder();
        }

        private void PicHoverEnter(object? sender, EventArgs e)
        {
            if (sender is PictureBox pb && pb != selectedPic)
            {
                ApplyZoom(pb, 1.10);
                StartThumbVideo(pb);
            }
        }

        private void PicHoverLeave(object? sender, EventArgs e)
        {
            if (sender is PictureBox pb && pb != selectedPic)
            {
                ResetZoom(pb);
                StopThumbVideo(pb);
            }
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
            try { _dinputTimer?.Stop(); } catch { }

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

            // Signal LEDBlinky: game starting
            try { _ledBlinky?.GameStart(); } catch { }

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

            // Signal LEDBlinky: back to animation / attract mode
            try { _ledBlinky?.StartAnimation(); } catch { }

            foreach (var (pic, _, _, _) in optionUis)
                pic.Enabled = true;
            closeButton.Enabled = true;

            // resume XInput polling
            try { xinputTimer?.Start(); } catch { }
            try { _dinputTimer?.Start(); } catch { }

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
                    StartThumbVideo(pic);
                }
                else
                {
                    ResetZoom(pic);
                    if (_thumbActivePic == pic) StopThumbVideo(pic);
                }
                pic.Invalidate();
            }

            // Highlight bottom buttons when navigated via controller
            bool cfgSelected = _navIndex == optionUis.Count;
            configButton.BackColor = cfgSelected ? Color.White : Color.Transparent;
            configButton.ForeColor = cfgSelected ? Color.Black : Color.White;

            bool exitSelected = _navIndex == optionUis.Count + 1;
            closeButton.BackColor = exitSelected ? Color.White : Color.Transparent;
            closeButton.ForeColor = exitSelected ? Color.Black : Color.White;
        }

        private void StartThumbVideo(PictureBox pb)
        {
            if (!_thumbVideoPaths.TryGetValue(pb, out var videoPath)) return;
            if (_thumbActivePic == pb) return; // already playing for this pic

            StopThumbVideoInternal();

            // Store original image so we can restore later
            if (!_thumbOriginalImages.ContainsKey(pb) && pb.Image != null)
                _thumbOriginalImages[pb] = pb.Image;

            _thumbActivePic = pb;
            _thumbW = pb.Width;
            _thumbH = pb.Height;

            // Allocate buffer for raw video frames (BGRA, 4 bytes/pixel)
            var bufSize = _thumbW * _thumbH * 4;
            if (_thumbBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(_thumbBuffer);
            _thumbBuffer = Marshal.AllocHGlobal(bufSize);

            try
            {
                // Clamp thumb volume to never exceed main music volume so main is always louder
                var mainVol  = config.Music?.Volume ?? 100;
                var thumbVol = Math.Min(config.Music?.ThumbVideoVolume ?? 0, mainVol);

                if (_thumbPlayer == null)
                {
                    _thumbLibVlc = thumbVol > 0 ? new LibVLC() : new LibVLC("--no-audio");
                    _thumbPlayer = new MediaPlayer(_thumbLibVlc);
                    _thumbPlayer.EndReached += (_, __) =>
                    {
                        _ = Task.Run(() =>
                        {
                            try { _thumbPlayer?.Stop(); } catch { }
                            try
                            {
                                _thumbPlayer?.Play();
                                // Re-apply volume immediately after restart
                                var mv = config.Music?.Volume ?? 100;
                                var tv = Math.Min(config.Music?.ThumbVideoVolume ?? 0, mv);
                                if (tv > 0 && _thumbPlayer != null) _thumbPlayer.Volume = tv;
                            }
                            catch { }
                        });
                    };
                    if (thumbVol > 0)
                    {
                        _thumbPlayer.Playing += (_, __) =>
                        {
                            // Re-evaluate at play time so config changes take effect;
                            // always cap below main music volume
                            var mv = config.Music?.Volume ?? 100;
                            var tv = Math.Min(config.Music?.ThumbVideoVolume ?? 0, mv);
                            try { _thumbPlayer.Volume = tv; } catch { }
                        };
                    }
                }
                else
                {
                    // Stop any current playback before reconfiguring
                    try { Task.Run(() => { try { _thumbPlayer.Stop(); } catch { } }).Wait(500); } catch { }
                }

                // Software rendering: LibVLC writes frames to our buffer instead of a native window
                _thumbPlayer.SetVideoFormat("RV32", (uint)_thumbW, (uint)_thumbH, (uint)(_thumbW * 4));
                _thumbPlayer.SetVideoCallbacks(ThumbLockCb, null, ThumbDisplayCb);

                try { _thumbMedia?.Dispose(); } catch { }
                _thumbMedia = new Media(_thumbLibVlc!, videoPath, FromType.FromPath);
                _thumbMedia.AddOption(":run-time=6");       // play only 6 seconds per loop
                _thumbMedia.AddOption(":input-repeat=65535"); // loop virtually forever
                _thumbPlayer.Media = _thumbMedia;
                // Set thumb volume before Play() so it doesn't briefly blast at 100
                if (thumbVol > 0)
                    _thumbPlayer.Volume = thumbVol;
                _thumbPlayer.Play();
                // Main music volume is NOT touched — it stays at its configured level
            }
            catch { }
        }

        private IntPtr ThumbLockCb(IntPtr opaque, IntPtr planes)
        {
            // Tell LibVLC to write the frame into our pre-allocated buffer
            Marshal.WriteIntPtr(planes, _thumbBuffer);
            return IntPtr.Zero;
        }

        private void ThumbDisplayCb(IntPtr opaque, IntPtr picture)
        {
            var pic = _thumbActivePic;
            if (pic == null || _thumbBuffer == IntPtr.Zero) return;

            try
            {
                // Create a Bitmap that wraps the buffer (no copy), then clone to own the data
                using var temp = new Bitmap(_thumbW, _thumbH, _thumbW * 4,
                    System.Drawing.Imaging.PixelFormat.Format32bppRgb, _thumbBuffer);
                var bmp = new Bitmap(temp); // deep copy — safe after LibVLC overwrites buffer

                pic.BeginInvoke(() =>
                {
                    if (_thumbActivePic != pic) { bmp.Dispose(); return; }
                    var old = pic.Image;
                    pic.Image = bmp;
                    // Dispose old frame bitmaps, but NOT the stored original images
                    if (old != null && !_thumbOriginalImages.ContainsValue(old))
                        old.Dispose();
                });
            }
            catch { }
        }

        private void StopThumbVideo(PictureBox pb)
        {
            if (_thumbActivePic != pb) return;
            StopThumbVideoInternal();
        }

        private void StopThumbVideoInternal()
        {
            var pic = _thumbActivePic;
            _thumbActivePic = null;

            if (_thumbPlayer != null && _thumbPlayer.IsPlaying)
            {
                try { Task.Run(() => { try { _thumbPlayer.Stop(); } catch { } }).Wait(500); } catch { }
            }

            // Re-assert main music volume — the thumb player's audio session may have
            // lowered the process-wide mixer level while it was active.
            try { musicPlayer?.SetVolume(musicPlayer.ConfiguredVolume); } catch { }

            // Restore original image
            if (pic != null && _thumbOriginalImages.TryGetValue(pic, out var orig))
                pic.Image = orig;
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

        private static void WireButtonHover(Button btn)
        {
            btn.MouseEnter += (_, _) => { btn.BackColor = Color.White; btn.ForeColor = Color.Black; };
            btn.MouseLeave += (_, _) => { btn.BackColor = Color.Transparent; btn.ForeColor = Color.White; };
        }

        private async void ConfigButton_Click(object? sender, EventArgs e)
        {
            var cfgExe = Path.Combine(AppContext.BaseDirectory, "ArcadeShellConfigurator.exe");
            if (!File.Exists(cfgExe)) return;

            configButton.Enabled = false;

            var proc = Process.Start(new ProcessStartInfo(cfgExe) { UseShellExecute = false });
            if (proc == null) { configButton.Enabled = true; return; }

            // Poll until the configurator has a main window (rendered on screen)
            bool windowReady = await Task.Run(() =>
            {
                for (int i = 0; i < 100; i++) // up to ~10 seconds
                {
                    if (proc.HasExited) return false;
                    proc.Refresh();
                    if (proc.MainWindowHandle != IntPtr.Zero) return true;
                    Thread.Sleep(100);
                }
                return !proc.HasExited;
            });

            if (!windowReady) { configButton.Enabled = true; return; }

            EnsureExplorerRunning();
            Close();
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
            // Signal LEDBlinky: front-end quitting
            try { _ledBlinky?.FrontEndQuit(); } catch { }

            // cancel any pending music diagnostic task
            try { _musicDiagCts?.Cancel(); } catch { }

            // stop and dispose timers
            _zOrderTimer?.Stop();
            _zOrderTimer?.Dispose();
            xinputTimer?.Stop();
            xinputTimer?.Dispose();

            _dinputTimer?.Stop();
            _dinputTimer?.Dispose();
            try { _dinputJoystick?.Unacquire(); } catch { }
            _dinputJoystick?.Dispose();
            _directInput?.Dispose();

            // Owned forms are auto-closed by WinForms; no need to close overlay manually.

            try { musicPlayer?.Dispose(); } catch { }
            try { spectrumPanel?.StopRefresh(); } catch { }
            try { spectrumAnalyzer?.Dispose(); } catch { }
            // _spectrumForm is owned, so WinForms auto-closes it.

            // Stop thumb video rendering
            try { StopThumbVideoInternal(); } catch { }
            try { _thumbMedia?.Dispose(); } catch { }
            try { _thumbPlayer?.Dispose(); } catch { }
            try { _thumbLibVlc?.Dispose(); } catch { }
            if (_thumbBuffer != IntPtr.Zero)
            {
                try { Marshal.FreeHGlobal(_thumbBuffer); } catch { }
                _thumbBuffer = IntPtr.Zero;
            }

            // Stop video synchronously before dispose to avoid blocking the UI thread.
            try { videoBackground?.Stop(); } catch { }
            try { videoBackground?.Dispose(); } catch { }
            base.OnFormClosed(e);
        }

        private void TryStartBackground()
        {
            try
            {
                var exts = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".ogg" };

                // 1. Use the explicit videoBackground path from config if set.
                if (!string.IsNullOrWhiteSpace(config.Paths.VideoBackground))
                {
                    var vidPath = config.Paths.VideoBackground;
                    if (!Path.IsPathRooted(vidPath))
                        vidPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, vidPath));
                    if (File.Exists(vidPath))
                    {
                        try { videoBackground?.PlayLoop(vidPath); } catch { }
                        return;
                    }
                }

                // 2. Fallback: scan Media\Bkg folder for any recognised video file.
                var bkgDir = Path.Combine(AppContext.BaseDirectory, "Media", "Bkg");
                try { if (!Directory.Exists(bkgDir)) Directory.CreateDirectory(bkgDir); } catch { }

                var files = Directory.GetFiles(bkgDir);

                // If Media\Bkg is empty, look for a video in the app root and copy it in.
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
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        // Route all hit-tests to HTTRANSPARENT so mouse events fall through to
        // whatever window sits below this one in z-order.
        // Using WM_NCHITTEST instead of WS_EX_TRANSPARENT avoids the side-effect
        // where WS_EX_TRANSPARENT forces the window to always paint LAST among
        // siblings (which caused the spectrum to render on top of the overlay).
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST)
            {
                m.Result = (IntPtr)HTTRANSPARENT;
                return;
            }
            base.WndProc(ref m);
        }
    }
}