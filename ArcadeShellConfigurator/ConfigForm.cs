using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using ArcadeShellSelector;
using NAudio.CoreAudioApi;
using SharpDX.DirectInput;
using SharpDX.XInput;

namespace ArcadeShellConfigurator
{
    internal sealed class ConfigForm : Form
    {
        private readonly string _configPath;
        private readonly List<string> _allConfigPaths = new();
        private AppConfig _config = new();

        // General tab
        private TextBox txtTitle = null!;
        private CheckBox chkTopMost = null!;
        private CheckBox chkLogging = null!;

        // Paths tab
        private TextBox txtToolsRoot = null!;
        private TextBox txtImagesRoot = null!;
        private TextBox txtVideoBackground = null!;
        private PictureBox picVideoThumb = null!;
        private Panel pnlArcadeScreen = null!;
        private Button btnStopVideo = null!;

        // LEDBlinky
        private CheckBox chkLedBlinkyEnabled = null!;
        private TextBox txtLedBlinkyExe = null!;

        // Music tab
        private CheckBox chkMusicEnabled = null!;
        private TextBox txtMusicRoot = null!;
        private TrackBar trkVolume = null!;
        private Label lblVolumeValue = null!;
        private TrackBar trkThumbVideoVolume = null!;
        private Label lblThumbVideoVolumeValue = null!;
        private ComboBox cboAudioDevice = null!;
        private CheckBox chkPlayRandom = null!;
        private ListBox lstMusicFiles = null!;
        private RichTextBox txtMetaInfo = null!;

        // Options tab
        private DataGridView gridOptions = null!;

        // Input tab
        private CheckBox chkXInputEnabled = null!;
        private CheckBox chkDInputEnabled = null!;
        private ComboBox cboDInputDevice = null!;
        // (scan buttons removed — device changes are detected automatically via WM_DEVICECHANGE)
        // Interactive button-binding labels (show current assignment)
        private Label lblBindSelect = null!;
        private Label lblBindBack   = null!;
        private Label lblBindLeft   = null!;
        private Label lblBindRight  = null!;
        // Interactive button-binding trigger buttons
        private Button btnBindSelect = null!;
        private Button btnBindBack   = null!;
        private Button btnBindLeft   = null!;
        private Button btnBindRight  = null!;
        // Stored binding values (1-based; 0 = axis/POV for Left/Right)
        private int _bindSelectBtn = 1;
        private int _bindBackBtn   = 2;
        private int _bindLeftBtn   = 0;
        private int _bindRightBtn  = 0;
        // Assignment session state
        private int _bindingTarget = -1; // 0=select,1=back,2=left,3=right,-1=idle
        private int _bindCountdown;
        private System.Windows.Forms.Timer? _bindTimer;
        private Joystick? _bindJoystick;
        private DirectInput? _bindDInput;
        private bool[] _bindLastButtons = Array.Empty<bool>();

        // Input test panel — DirectInput
        private GroupBox grpDI = null!;
        private InputVisualPanel visualDInput = null!;
        private Button btnTestDInput = null!;
        private Label lblTestDevice = null!;
        private Label lblTestButtons = null!;
        private Label lblTestAxes = null!;
        private DirectInput? _testDInput;
        private Joystick? _testJoystick;
        private System.Windows.Forms.Timer? _testTimer;
        private readonly List<DeviceInstance> _dinputDeviceList = new();

        // Input test panel — XInput
        private GroupBox grpXI = null!;
        private InputVisualPanel visualXInput = null!;
        private ListBox lstXInputSlots = null!;
        private Button btnTestXInput = null!;
        private Label lblXInputStatus = null!;
        private Label lblXInputButtons = null!;
        private Label lblXInputAxes = null!;
        private System.Windows.Forms.Timer? _xinputTestTimer;
        // Auto-rescan: fires once 600 ms after the last WM_DEVICECHANGE notification
        private System.Windows.Forms.Timer? _deviceRescanTimer;
        // XInput button-binding display labels
        private Label lblXiBindSelect = null!, lblXiBindBack = null!, lblXiBindLeft = null!, lblXiBindRight = null!;
        // XInput button-binding trigger buttons
        private Button btnXiBindSelect = null!, btnXiBindBack = null!, btnXiBindLeft = null!, btnXiBindRight = null!;
        // Stored XInput binding values (GamepadButtonFlags integer; 0 = DPad/stick for L/R)
        private int _xiBindSelectBtn = 4096;  // A
        private int _xiBindBackBtn   = 8192;  // B
        private int _xiBindLeftBtn   = 0;     // DPad + stick
        private int _xiBindRightBtn  = 0;     // DPad + stick
        // XInput assignment session state
        private int _xiBindingTarget = -1;    // 0=select,1=back,2=left,3=right,-1=idle
        private int _xiBindCountdown;
        private int _xiBindSlot;
        private System.Windows.Forms.Timer? _xiBindTimer;
        private GamepadButtonFlags _xiBindLastButtons;

        // Bottom panel
        private bool _suppressDirty;
        private StatusStrip statusStrip = null!;
        private ToolStripStatusLabel lblStatusPath = null!;
        private ToolStripStatusLabel lblStatusSave = null!;

        // Log tab
        private RichTextBox txtLog = null!;
        private FileSystemWatcher? _logWatcher;
        private System.Windows.Forms.Timer? _logRefreshTimer;
        private long _logLastLength;
        private string _logFilePath = "";
        private string _logRawContent = "";

        // Music preview — uses LibVLCManager.Instance (same as the main app's MusicPlayer)
        private LibVLCSharp.Shared.MediaPlayer? _previewPlayer;
        private LibVLCSharp.Shared.Media? _previewMedia;

        // Video preview — _previewLibVlc is the warm-up instance for video only
        private LibVLCSharp.Shared.LibVLC? _previewLibVlc;
        private LibVLCSharp.Shared.MediaPlayer? _videoPreviewPlayer;
        private bool _isVideoPlaying;
        private System.Diagnostics.Stopwatch? _videoStartWatch;
        private System.Windows.Forms.Timer? _videoPreviewTimer;
        private Task<LibVLCSharp.Shared.LibVLC>? _vlcInitTask;

        public ConfigForm()
        {
            // Find ALL config.json files: walk up from the exe, and collect every one found.
            // This ensures we write to both the source copy and any build output copies.
            var dir = Path.GetDirectoryName(Application.ExecutablePath);
            _configPath = "";
            while (dir != null)
            {
                var candidate = Path.Combine(dir, "config.json");
                if (File.Exists(candidate))
                    _allConfigPaths.Add(candidate);
                dir = Path.GetDirectoryName(dir);
            }

            // Primary config is the one closest to the solution root (last found)
            if (_allConfigPaths.Count > 0)
                _configPath = _allConfigPaths[^1]; // furthest up = source
            else
                _configPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "config.json");

            // Also find config.json in sibling project output directories (e.g. ArcadeShellSelector\bin\...)
            // so saved changes are picked up by the main app without rebuilding.
            var solutionRoot = Path.GetDirectoryName(_configPath);
            if (solutionRoot != null)
            {
                foreach (var binDir in Directory.EnumerateFiles(solutionRoot, "config.json", SearchOption.AllDirectories))
                {
                    if (!_allConfigPaths.Contains(binDir, StringComparer.OrdinalIgnoreCase))
                        _allConfigPaths.Add(binDir);
                }
            }

            _vlcInitTask = Task.Run(() =>
            {
                LibVLCSharp.Shared.Core.Initialize();
                return new LibVLCSharp.Shared.LibVLC(
                    "--no-osd", "--no-snapshot-preview", "--no-stats",
                    "--no-sub-autodetect-file", "--no-metadata-network-access");
            });

            InitializeUI();
            LoadConfig();
            Shown += (_, _) =>
            {
                ScanDInputDevices();
                ScanXInputSlots();
                lblStatusSave.Text = "Initializing media engine…";
                WarmUpVideoPreview();
            };
        }

        private void InitializeUI()
        {
            Text = "Arcade Shell Configurator";
            Size = new Size(860, 900);
            MinimumSize = new Size(820, 840);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            Padding = new Padding(0);

            // Set window icon from the embedded app.ico resource
            var icoPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "app.ico");
            if (File.Exists(icoPath))
                Icon = new Icon(icoPath);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.Padding = new Point(12, 6);

            // === General tab ===
            var tabGeneral = new TabPage("General") { Padding = new Padding(8) };

            var grpApp = new GroupBox
            {
                Text = "Application",
                Dock = DockStyle.Top,
                Height = 62,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(12, 8, 12, 8)
            };
            var lblTitle = new Label { Text = "App Title:", Location = new Point(16, 24), AutoSize = true };
            txtTitle = new TextBox { Location = new Point(140, 21), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            grpApp.Controls.AddRange(new Control[] { lblTitle, txtTitle });
            grpApp.Layout += (_, _) =>
            {
                txtTitle.Width = grpApp.ClientSize.Width - txtTitle.Left - 16;
            };

            var grpBehavior = new GroupBox
            {
                Text = "Behavior",
                Dock = DockStyle.Top,
                Height = 48,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(12, 8, 12, 8)
            };
            chkTopMost = new CheckBox { Text = "Always on top (TopMost)", Location = new Point(16, 22), AutoSize = true };
            var lblSep = new Label { Text = "|", Location = new Point(210, 23), AutoSize = true, ForeColor = SystemColors.GrayText };
            chkLogging = new CheckBox { Text = "Enable logging (Depuracion)", Location = new Point(228, 22), AutoSize = true };
            grpBehavior.Controls.AddRange(new Control[] { chkTopMost, lblSep, chkLogging });

            // === Paths tab ===
            var tabPaths = new TabPage("Directorios") { Padding = new Padding(8) };

            var grpDirectories = new GroupBox
            {
                Text = "Directories",
                Dock = DockStyle.Top,
                Height = 120,
                Padding = new Padding(12, 8, 12, 8)
            };
            var lblToolsRoot = new Label { Text = "Tools Root:", Location = new Point(16, 24), AutoSize = true };
            txtToolsRoot = new TextBox { Location = new Point(140, 21), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var btnToolsRoot = new Button { Text = "...", Width = 30, Height = 23, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnToolsRoot.Click += (_, _) => BrowseDrive(txtToolsRoot);
            var lblToolsHint = new Label
            {
                Text = "Drive or root folder where child apps live (e.g. D:\\)",
                Location = new Point(140, 44),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font, FontStyle.Italic),
            };

            var lblImagesRoot = new Label { Text = "Images Root:", Location = new Point(16, 72), AutoSize = true };
            txtImagesRoot = new TextBox { Location = new Point(140, 69), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var btnImagesRoot = new Button { Text = "...", Width = 30, Height = 23, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnImagesRoot.Click += (_, _) => BrowseFolderUnder(txtImagesRoot, txtToolsRoot.Text, _configPath);
            var lblImagesHint = new Label
            {
                Text = "Relative path inside Tools Root where artwork is stored",
                Location = new Point(140, 92),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font, FontStyle.Italic),
            };

            grpDirectories.Controls.AddRange(new Control[] {
                lblToolsRoot, txtToolsRoot, btnToolsRoot, lblToolsHint,
                lblImagesRoot, txtImagesRoot, btnImagesRoot, lblImagesHint
            });

            // Position browse buttons and size textboxes on layout
            grpDirectories.Layout += (_, _) =>
            {
                int right = grpDirectories.ClientSize.Width - 16;
                btnToolsRoot.Location = new Point(right - btnToolsRoot.Width, 21);
                txtToolsRoot.Width = btnToolsRoot.Left - txtToolsRoot.Left - 6;
                btnImagesRoot.Location = new Point(right - btnImagesRoot.Width, 69);
                txtImagesRoot.Width = btnImagesRoot.Left - txtImagesRoot.Left - 6;
            };

            var grpVideo = new GroupBox
            {
                Text = "Video Background",
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8)
            };
            var lblVideo = new Label { Text = "Video File:", Location = new Point(16, 28), AutoSize = true };
            txtVideoBackground = new TextBox { Location = new Point(140, 25), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var btnVideo = new Button { Text = "...", Width = 30, Height = 23, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnVideo.Click += (_, _) =>
            {
                BrowseAndDeployVideo();
                AutoPreviewVideo();
            };
            txtVideoBackground.TextChanged += (_, _) => AutoPreviewVideo();
            var lblVideoHint = new Label
            {
                Text = "Video file played as background (copied to Media/Bkg folder automatically)",
                Location = new Point(140, 52),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font, FontStyle.Italic),
            };

            picVideoThumb = new PictureBox
            {
                Location = new Point(16, 72),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.None,
                BackColor = Color.Transparent,
            };

            // Load arcade cabinet image
            var cabinetPath = Path.Combine(Path.GetDirectoryName(_configPath) ?? ".", "Media", "Img", "Arcade.png");
            if (File.Exists(cabinetPath))
                picVideoThumb.Image = Image.FromFile(cabinetPath);

            // Screen overlay panel — video plays here, positioned over the cabinet's monitor
            pnlArcadeScreen = new Panel
            {
                BackColor = Color.Black,
            };
            picVideoThumb.Controls.Add(pnlArcadeScreen);
            picVideoThumb.Resize += (_, _) => PositionArcadeScreen();

            btnStopVideo = new Button
            {
                Text = "\u25A0 Stop",
                Width = 70,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White,
                Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            btnStopVideo.FlatAppearance.BorderColor = Color.Gray;
            btnStopVideo.Click += (_, _) => StopVideoPreview();

            grpVideo.Controls.AddRange(new Control[] { lblVideo, txtVideoBackground, btnVideo, lblVideoHint, picVideoThumb, btnStopVideo });

            // Position browse button, textbox, and thumb on layout
            grpVideo.Layout += (_, _) =>
            {
                int right = grpVideo.ClientSize.Width - 16;
                btnVideo.Location = new Point(right - btnVideo.Width, 25);
                txtVideoBackground.Width = btnVideo.Left - txtVideoBackground.Left - 6;
                picVideoThumb.Width = right - picVideoThumb.Left;
                picVideoThumb.Height = grpVideo.ClientSize.Height - picVideoThumb.Top - 12;
                btnStopVideo.Location = new Point(right - btnStopVideo.Width, picVideoThumb.Top);
                PositionArcadeScreen();
            };

            tabPaths.Controls.Add(grpVideo);
            tabPaths.Controls.Add(grpDirectories);

            // === LEDBlinky group (on Directorios tab) ===
            var grpLedBlinky = new GroupBox
            {
                Text = "LEDBlinky",
                Dock = DockStyle.Top,
                Height = 80,
                Padding = new Padding(12, 8, 12, 8)
            };
            chkLedBlinkyEnabled = new CheckBox { Text = "LEDBlinky enabled", Location = new Point(16, 24), AutoSize = true };
            var lblLedBlinkyExe = new Label { Text = "LEDBlinky.exe:", Location = new Point(16, 52), AutoSize = true };
            txtLedBlinkyExe = new TextBox { Location = new Point(140, 49), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var btnLedBlinkyExe = new Button { Text = "...", Width = 30, Height = 23, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnLedBlinkyExe.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "Select LEDBlinky.exe",
                    Filter = "Executable|LEDBlinky.exe|All files|*.*",
                };
                if (!string.IsNullOrWhiteSpace(txtLedBlinkyExe.Text))
                    dlg.InitialDirectory = Path.GetDirectoryName(txtLedBlinkyExe.Text) ?? "";
                if (dlg.ShowDialog() == DialogResult.OK)
                    txtLedBlinkyExe.Text = dlg.FileName;
            };
            chkLedBlinkyEnabled.CheckedChanged += (_, _) => txtLedBlinkyExe.Enabled = chkLedBlinkyEnabled.Checked;

            grpLedBlinky.Controls.AddRange(new Control[] { chkLedBlinkyEnabled, lblLedBlinkyExe, txtLedBlinkyExe, btnLedBlinkyExe });
            grpLedBlinky.Layout += (_, _) =>
            {
                int right = grpLedBlinky.ClientSize.Width - 16;
                btnLedBlinkyExe.Location = new Point(right - btnLedBlinkyExe.Width, 49);
                txtLedBlinkyExe.Width = btnLedBlinkyExe.Left - txtLedBlinkyExe.Left - 6;
            };
            // LEDBlinky group is added to the Music tab below

            // === Music tab ===
            var tabMusic = new TabPage("Media/Led") { Padding = new Padding(8) };

            var grpPlayback = new GroupBox
            {
                Text = "Playback",
                Dock = DockStyle.Top,
                Height = 80,
                Padding = new Padding(12, 8, 12, 8)
            };
            chkMusicEnabled = new CheckBox { Text = "Music enabled", Location = new Point(16, 24), AutoSize = true };
            var lblMusicRoot = new Label { Text = "Music Folder:", Location = new Point(16, 52), AutoSize = true };
            txtMusicRoot = new TextBox { Location = new Point(140, 49), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            var btnMusicRoot = new Button { Text = "...", Width = 30, Height = 23, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnMusicRoot.Click += (_, _) => BrowseFolder(txtMusicRoot, _configPath);
            grpPlayback.Controls.AddRange(new Control[] { chkMusicEnabled, lblMusicRoot, txtMusicRoot, btnMusicRoot });

            grpPlayback.Layout += (_, _) =>
            {
                int right = grpPlayback.ClientSize.Width - 16;
                btnMusicRoot.Location = new Point(right - btnMusicRoot.Width, 49);
                txtMusicRoot.Width = btnMusicRoot.Left - txtMusicRoot.Left - 6;
            };

            // === Music Files group ===
            var grpMusicFiles = new GroupBox
            {
                Text = "Music Files",
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 8, 12, 8)
            };
            chkPlayRandom = new CheckBox { Text = "Play Random music", Location = new Point(16, 22), AutoSize = true, Checked = true };
            lstMusicFiles = new ListBox
            {
                Location = new Point(16, 48),
                IntegralHeight = false,
            };
            txtMetaInfo = new RichTextBox
            {
                ReadOnly = true,
                BackColor = Color.FromArgb(24, 24, 24),
                ForeColor = Color.FromArgb(0, 200, 80),
                Font = new Font("Consolas", 8.5f),
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                WordWrap = true,
            };
            var btnRefreshFiles = new Button
            {
                Text = "↻ Refresh",
                Width = 80,
                Height = 23,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            var btnStopPreview = new Button
            {
                Text = "■ Stop",
                Width = 60,
                Height = 23,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            btnStopPreview.Click += (_, _) => StopMusicPreview();
            btnRefreshFiles.Click += (_, _) => RefreshMusicFileList();
            chkPlayRandom.CheckedChanged += (_, _) =>
            {
                lstMusicFiles.Enabled = !chkPlayRandom.Checked;
                // When switching away from random, auto-preview the currently selected file
                if (!chkPlayRandom.Checked && lstMusicFiles.SelectedItem is string fileName)
                {
                    var root = txtMusicRoot.Text;
                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        var fullPath = Path.IsPathRooted(root)
                            ? Path.Combine(root, fileName)
                            : Path.Combine(Path.GetDirectoryName(_configPath) ?? ".", root, fileName);
                        if (File.Exists(fullPath))
                            PreviewMusicFile(fullPath);
                    }
                }
            };

            // Preview + metadata on selection change (only when not in random mode and not loading)
            lstMusicFiles.SelectedIndexChanged += (_, _) =>
            {
                if (_suppressDirty) return;
                if (lstMusicFiles.SelectedItem is string fileName)
                {
                    var root = txtMusicRoot.Text;
                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        var fullPath = Path.IsPathRooted(root)
                            ? Path.Combine(root, fileName)
                            : Path.Combine(Path.GetDirectoryName(_configPath) ?? ".", root, fileName);
                        if (File.Exists(fullPath))
                        {
                            if (!chkPlayRandom.Checked)
                                PreviewMusicFile(fullPath);
                            ShowTrackerMetadata(txtMetaInfo, fullPath);
                        }
                    }
                }
            };

            grpMusicFiles.Controls.AddRange(new Control[] { chkPlayRandom, lstMusicFiles, txtMetaInfo, btnRefreshFiles, btnStopPreview });
            grpMusicFiles.Layout += (_, _) =>
            {
                int right = grpMusicFiles.ClientSize.Width - 16;
                int bottom = grpMusicFiles.ClientSize.Height - 12;
                btnRefreshFiles.Location = new Point(right - btnRefreshFiles.Width, 19);
                btnStopPreview.Location = new Point(btnRefreshFiles.Left - btnStopPreview.Width - 6, 19);
                // Split horizontally: ListBox on left, metadata on right
                int midX = 16 + (right - 16) / 2 - 4;
                lstMusicFiles.SetBounds(16, 48, midX - 16, bottom - 48);
                txtMetaInfo.SetBounds(midX + 4, 48, right - midX - 4, bottom - 48);
            };

            var grpAudio = new GroupBox
            {
                Text = "Audio Output",
                Dock = DockStyle.Top,
                Height = 170,
                Padding = new Padding(12, 8, 12, 8)
            };
            var lblAudioDev = new Label { Text = "Audio Device:", Location = new Point(16, 28), AutoSize = true };
            cboAudioDevice = new ComboBox
            {
                Location = new Point(140, 25),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DrawMode = DrawMode.OwnerDrawFixed
            };
            cboAudioDevice.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;
                e.DrawBackground();
                var item = cboAudioDevice.Items[e.Index]?.ToString() ?? "";
                var isDefault = item.EndsWith(" *");
                var font = isDefault ? new Font(e.Font!, FontStyle.Bold) : e.Font!;
                var brush = (e.State & DrawItemState.Selected) != 0
                    ? SystemBrushes.HighlightText
                    : isDefault ? Brushes.DarkGreen : SystemBrushes.ControlText;
                e.Graphics.DrawString(item, font, brush, e.Bounds);
                if (isDefault) font.Dispose();
                e.DrawFocusRectangle();
            };
            PopulateAudioDevices();
            var lblAudioDevHint = new Label
            {
                Text = "First entry uses system default; * = current default",
                Location = new Point(140, 48),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font, FontStyle.Italic),
            };

            var lblVolume = new Label { Text = "Volume:", Location = new Point(16, 82), AutoSize = true };
            trkVolume = new TrackBar
            {
                Location = new Point(140, 74),
                Width = 300,
                Height = 45,
                AutoSize = false,
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                LargeChange = 10,
                SmallChange = 1,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            lblVolumeValue = new Label { Text = "0", Location = new Point(448, 82), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold) };
            trkVolume.ValueChanged += (_, _) => lblVolumeValue.Text = $"{trkVolume.Value}%";

            var lblThumbVol = new Label { Text = "Thumb Video Vol:", Location = new Point(16, 127), AutoSize = true };
            trkThumbVideoVolume = new TrackBar
            {
                Location = new Point(140, 119),
                Width = 300,
                Height = 45,
                AutoSize = false,
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                LargeChange = 10,
                SmallChange = 1,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            lblThumbVideoVolumeValue = new Label { Text = "0", Location = new Point(448, 127), AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right, Font = new Font(Font.FontFamily, 9f, FontStyle.Bold) };
            trkThumbVideoVolume.ValueChanged += (_, _) => lblThumbVideoVolumeValue.Text = $"{trkThumbVideoVolume.Value}%";

            grpAudio.Controls.AddRange(new Control[] { lblAudioDev, cboAudioDevice, lblAudioDevHint, lblVolume, trkVolume, lblVolumeValue, lblThumbVol, trkThumbVideoVolume, lblThumbVideoVolumeValue });

            // Position volume label and size controls on layout
            grpAudio.Layout += (_, _) =>
            {
                int right = grpAudio.ClientSize.Width - 16;
                cboAudioDevice.Width = right - cboAudioDevice.Left;
                lblVolumeValue.Location = new Point(right - 40, 82);
                trkVolume.Width = lblVolumeValue.Left - trkVolume.Left - 6;
                lblThumbVideoVolumeValue.Location = new Point(right - 40, 127);
                trkThumbVideoVolume.Width = lblThumbVideoVolumeValue.Left - trkThumbVideoVolume.Left - 6;
            };

            tabMusic.Controls.Add(grpMusicFiles);  // Dock.Fill — must be added first
            tabMusic.Controls.Add(grpLedBlinky);
            tabMusic.Controls.Add(grpAudio);
            tabMusic.Controls.Add(grpPlayback);

            // === Options grid (added to General tab) ===
            gridOptions = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                EditMode = DataGridViewEditMode.EditOnEnter,
            };
            gridOptions.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Label",
                HeaderText = "FrontEnd (Nombre)",
                FillWeight = 30,
            });
            gridOptions.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Exe",
                HeaderText = "Front End (Exe)",
                FillWeight = 50,
            });
            gridOptions.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "BrowseExe",
                HeaderText = "",
                Text = "...",
                UseColumnTextForButtonValue = true,
                Width = 30,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });
            gridOptions.Columns.Add(new DataGridViewImageColumn
            {
                Name = "ImageThumb",
                HeaderText = "Image",
                ImageLayout = DataGridViewImageCellLayout.Zoom,
                Width = 60,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });
            gridOptions.Columns.Add("ImagePath", "Image Path");
            gridOptions.Columns["ImagePath"]!.Visible = false;
            gridOptions.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "BrowseImage",
                HeaderText = "",
                Text = "...",
                UseColumnTextForButtonValue = true,
                Width = 30,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });
            gridOptions.Columns.Add(new DataGridViewImageColumn
            {
                Name = "VideoThumb",
                HeaderText = "Video",
                ImageLayout = DataGridViewImageCellLayout.Zoom,
                Width = 60,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });
            gridOptions.Columns.Add("ThumbVideoPath", "Thumb Video Path");
            gridOptions.Columns["ThumbVideoPath"]!.Visible = false;
            gridOptions.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "BrowseThumbVideo",
                HeaderText = "",
                Text = "...",
                UseColumnTextForButtonValue = true,
                Width = 30,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            });
            gridOptions.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "DeleteRow",
                HeaderText = "",
                Text = "✖",
                UseColumnTextForButtonValue = true,
                Width = 30,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                FlatStyle = FlatStyle.Flat,
            });
            gridOptions.RowTemplate.Height = 48;
            gridOptions.CellContentClick += GridOptions_CellContentClick;

            var grpOptions = new GroupBox
            {
                Text = "Opciones del lanzador",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 12, 8, 8),
                MinimumSize = new Size(0, 160),
            };
            gridOptions.Dock = DockStyle.Fill;
            grpOptions.Controls.Add(gridOptions);

            // === Onboarding image panel ===
            var imgDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "Media", "Img");
            // Walk up to find the Media\Img folder (handles running from bin\Debug or bin\Release)
            if (!Directory.Exists(imgDir))
            {
                var search = Path.GetDirectoryName(Application.ExecutablePath);
                while (search != null)
                {
                    var candidate = Path.Combine(search, "Media", "Img");
                    if (Directory.Exists(candidate)) { imgDir = candidate; break; }
                    search = Path.GetDirectoryName(search);
                }
            }

            var onboardingImages = new Image?[6]; // index 0..5
            for (int i = 0; i <= 5; i++)
            {
                var p = Path.Combine(imgDir, $"Onboarding{i}.png");
                if (File.Exists(p))
                    onboardingImages[i] = Image.FromFile(p);
            }

            var pnlOnboarding = new Panel
            {
                Dock = DockStyle.Top,
                Height = 290,
                Padding = new Padding(4, 8, 4, 4),
            };
            var picOnboarding = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                Image = onboardingImages[0], // default when tab loads
                BackColor = Color.FromArgb(30, 30, 30),
            };
            var lblOnboardingHint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 20,
                Text = "",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font.FontFamily, 8f, FontStyle.Italic),
            };
            pnlOnboarding.Controls.Add(picOnboarding);
            pnlOnboarding.Controls.Add(lblOnboardingHint);

            // Map grid columns to onboarding images
            var columnImageMap = new Dictionary<string, (int imgIndex, string hint)>
            {
                ["Label"]            = (3, "Nombre del frontend"),
                ["Exe"]              = (5, "Ubicación del ejecutable"),
                ["BrowseExe"]        = (5, "Ubicación del ejecutable"),
                ["ImageThumb"]       = (2, "Imagen del frontend"),
                ["ImagePath"]        = (2, "Imagen del frontend"),
                ["BrowseImage"]      = (2, "Imagen del frontend"),
                ["VideoThumb"]       = (4, "Vídeo thumbnail al pasar el ratón"),
                ["ThumbVideoPath"]   = (4, "Vídeo thumbnail al pasar el ratón"),
                ["BrowseThumbVideo"] = (4, "Vídeo thumbnail al pasar el ratón"),
            };

            void ShowOnboarding(int index, string hint)
            {
                if (index >= 0 && index <= 5 && onboardingImages[index] != null)
                {
                    picOnboarding.Image = onboardingImages[index];
                    lblOnboardingHint.Text = hint;
                }
            }

            // Switch image when a grid cell is selected
            gridOptions.CellEnter += (_, e) =>
            {
                var colName = gridOptions.Columns[e.ColumnIndex].Name;
                if (columnImageMap.TryGetValue(colName, out var info))
                    ShowOnboarding(info.imgIndex, info.hint);
            };

            // Switch to Onboarding1 when the title field is focused
            txtTitle.Enter += (_, _) => ShowOnboarding(1, "Título de la aplicación");

            // Add grid group FIRST so Dock.Fill works under the Top-docked groups
            tabGeneral.Controls.Add(grpOptions);
            tabGeneral.Controls.Add(pnlOnboarding);
            tabGeneral.Controls.Add(grpBehavior);
            tabGeneral.Controls.Add(grpApp);

            // === Input tab ===
            var tabInput = new TabPage("Controles") { Padding = new Padding(8) };

            // Two self-contained side-by-side frames: DInput (left) | XInput (right).
            // Each follows the natural user flow: (1) enable → (2) select device → (3) assign buttons → (4) test.
            // TableLayoutPanel gives each frame exactly 50 % of the available width.
            var tblInput = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None,
            };
            tblInput.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tblInput.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            tblInput.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // ══════════════════════════════════════════════════════════
            // LEFT — DirectInput (Zero Delay, Xin-Mo, I-PAC …)
            // ══════════════════════════════════════════════════════════
            grpDI = new GroupBox
            {
                Text = "DirectInput — Arcade Encoders",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 4, 10, 8)
            };

            // (1) Enable
            chkDInputEnabled = new CheckBox { Text = "DirectInput habilitado", Location = new Point(10, 22), AutoSize = true };

            // (2) Device selector
            var diSep1 = MakeSectionLabel("Dispositivo activo", new Point(10, 50));
            var lblDiAutoUpdate = new Label
            {
                Text = "Se actualiza al conectar / desconectar",
                Location = new Point(10, 50),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font, FontStyle.Italic),
            };
            var lblDInputDevice = new Label { Text = "Dispositivo:", Location = new Point(10, 72), AutoSize = true };
            cboDInputDevice = new ComboBox
            {
                Location = new Point(100, 69),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            cboDInputDevice.Items.Add("(Primero disponible)");
            cboDInputDevice.SelectedIndex = 0;

            // (3) Button bindings — Y positions match XInput panel exactly
            var diSep2 = MakeSectionLabel("Asignación de botones", new Point(10, 152));
            var lblFnSelect = new Label { Text = "Seleccionar / Lanzar:", Location = new Point(10, 172), AutoSize = true };
            var lblFnBack   = new Label { Text = "Salir / Cerrar:",       Location = new Point(10, 200), AutoSize = true };
            var lblFnLeft   = new Label { Text = "Navegar Izquierda:",    Location = new Point(10, 228), AutoSize = true };
            var lblFnRight  = new Label { Text = "Navegar Derecha:",      Location = new Point(10, 256), AutoSize = true };

            lblBindSelect = new Label { Text = "Botón 1",   Location = new Point(165, 169), Size = new Size(110, 23), BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };
            lblBindBack   = new Label { Text = "Botón 2",   Location = new Point(165, 197), Size = new Size(110, 23), BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };
            lblBindLeft   = new Label { Text = "Eje / POV",  Location = new Point(165, 225), Size = new Size(110, 23), BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };
            lblBindRight  = new Label { Text = "Eje / POV",  Location = new Point(165, 253), Size = new Size(110, 23), BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };

            btnBindSelect = new Button { Text = "⊕ Asignar", Location = new Point(285, 169), Size = new Size(90, 23) };
            btnBindBack   = new Button { Text = "⊕ Asignar", Location = new Point(285, 197), Size = new Size(90, 23) };
            btnBindLeft   = new Button { Text = "⊕ Asignar", Location = new Point(285, 225), Size = new Size(90, 23) };
            btnBindRight  = new Button { Text = "⊕ Asignar", Location = new Point(285, 253), Size = new Size(90, 23) };
            btnBindSelect.Click += (_, _) => { if (_bindingTarget == 0) CancelBind(); else StartBind(0); };
            btnBindBack.Click   += (_, _) => { if (_bindingTarget == 1) CancelBind(); else StartBind(1); };
            btnBindLeft.Click   += (_, _) => { if (_bindingTarget == 2) CancelBind(); else StartBind(2); };
            btnBindRight.Click  += (_, _) => { if (_bindingTarget == 3) CancelBind(); else StartBind(3); };

            var lblInputHint = new Label
            {
                Text = "Pulsa ⊕ Asignar y presiona el botón en el dispositivo. Izquierda/Derecha también acepta eje o POV.",
                Location = new Point(10, 282),
                Size = new Size(400, 26),
                AutoSize = false,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font, FontStyle.Italic),
            };

            // (4) Test — Y positions now match XInput test section exactly
            var diSep3 = MakeSectionLabel("Probar dispositivo", new Point(10, 318));
            btnTestDInput = new Button { Text = "\u25b6 Iniciar", Location = new Point(10, 338), Width = 110, Height = 24 };
            btnTestDInput.Click += BtnTestDInput_Click;
            lblTestDevice = new Label { Text = "Activo: \u2014", Location = new Point(126, 342), AutoSize = true };
            visualDInput = new InputVisualPanel
            {
                Location = new Point(10, 370),
                Size = new Size(410, 180),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            lblTestButtons = new Label
            {
                Text = "Botones: \u2014",
                Location = new Point(10, 558),
                AutoSize = true,
                Font = new Font("Courier New", 8.5f),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            lblTestAxes = new Label
            {
                Text = "Eje X: \u2014 | Eje Y: \u2014 | POV: \u2014",
                Location = new Point(10, 576),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            grpDI.Controls.AddRange(new Control[]
            {
                chkDInputEnabled,
                diSep1, lblDiAutoUpdate, lblDInputDevice, cboDInputDevice,
                diSep2, lblFnSelect, lblBindSelect, btnBindSelect,
                        lblFnBack,   lblBindBack,   btnBindBack,
                        lblFnLeft,   lblBindLeft,   btnBindLeft,
                        lblFnRight,  lblBindRight,  btnBindRight,
                lblInputHint,
                diSep3, btnTestDInput, lblTestDevice,
                visualDInput, lblTestButtons, lblTestAxes
            });
            grpDI.Layout += (_, _) =>
            {
                int right = grpDI.ClientSize.Width - 10;
                lblDiAutoUpdate.Location = new Point(right - lblDiAutoUpdate.PreferredWidth, 50);
                // combo is on its own row — extend to full width
                cboDInputDevice.Width = right - cboDInputDevice.Left;
                foreach (var (lbl, btn) in new[]
                {
                    (lblBindSelect, btnBindSelect), (lblBindBack, btnBindBack),
                    (lblBindLeft,   btnBindLeft),   (lblBindRight, btnBindRight)
                })
                {
                    btn.Location = new Point(right - btn.Width, btn.Top);
                    lbl.Width = btn.Left - lbl.Left - 6;
                }
                lblInputHint.Width = right - 10;
                visualDInput.Width = right - visualDInput.Left;
            };

            // ══════════════════════════════════════════════════════════
            // RIGHT — XInput (Xbox / compatible)
            // ══════════════════════════════════════════════════════════
            grpXI = new GroupBox
            {
                Text = "XInput — Xbox / Compatible",
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 4, 10, 8)
            };

            // (1) Enable
            chkXInputEnabled = new CheckBox { Text = "XInput habilitado", Location = new Point(10, 22), AutoSize = true };

            // (2) Device / slot selector
            var xiSep1 = MakeSectionLabel("Controladores detectados", new Point(10, 50));
            var lblXiAutoUpdate = new Label
            {
                Text = "Se actualiza al conectar / desconectar",
                Location = new Point(10, 50),
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font, FontStyle.Italic),
            };
            lstXInputSlots = new ListBox
            {
                Location = new Point(10, 70),
                Size = new Size(300, 72),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                SelectionMode = SelectionMode.One
            };
            lstXInputSlots.SelectedIndexChanged += (_, _) => { if (!_suppressDirty) AutoSave(); };

            // (3) Button bindings
            var xiSep2 = MakeSectionLabel("Asignación de botones", new Point(10, 152));
            var lblFnXiSelect = new Label { Text = "Seleccionar / Lanzar:", Location = new Point(10, 172), AutoSize = true };
            var lblFnXiBack   = new Label { Text = "Salir / Cerrar:",       Location = new Point(10, 200), AutoSize = true };
            var lblFnXiLeft   = new Label { Text = "Navegar Izquierda:",    Location = new Point(10, 228), AutoSize = true };
            var lblFnXiRight  = new Label { Text = "Navegar Derecha:",      Location = new Point(10, 256), AutoSize = true };

            lblXiBindSelect = new Label { Text = "A",         Location = new Point(165, 169), Size = new Size(110, 23), BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };
            lblXiBindBack   = new Label { Text = "B",         Location = new Point(165, 197), Size = new Size(110, 23), BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };
            lblXiBindLeft   = new Label { Text = "DPad / Palanca", Location = new Point(165, 225), Size = new Size(110, 23), BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };
            lblXiBindRight  = new Label { Text = "DPad / Palanca", Location = new Point(165, 253), Size = new Size(110, 23), BorderStyle = BorderStyle.FixedSingle, TextAlign = ContentAlignment.MiddleCenter };

            btnXiBindSelect = new Button { Text = "\u2295 Asignar", Location = new Point(285, 169), Size = new Size(90, 23) };
            btnXiBindBack   = new Button { Text = "\u2295 Asignar", Location = new Point(285, 197), Size = new Size(90, 23) };
            btnXiBindLeft   = new Button { Text = "\u2295 Asignar", Location = new Point(285, 225), Size = new Size(90, 23) };
            btnXiBindRight  = new Button { Text = "\u2295 Asignar", Location = new Point(285, 253), Size = new Size(90, 23) };
            btnXiBindSelect.Click += (_, _) => { if (_xiBindingTarget == 0) CancelXiBind(); else StartXiBind(0); };
            btnXiBindBack.Click   += (_, _) => { if (_xiBindingTarget == 1) CancelXiBind(); else StartXiBind(1); };
            btnXiBindLeft.Click   += (_, _) => { if (_xiBindingTarget == 2) CancelXiBind(); else StartXiBind(2); };
            btnXiBindRight.Click  += (_, _) => { if (_xiBindingTarget == 3) CancelXiBind(); else StartXiBind(3); };

            var lblXiHint = new Label
            {
                Text = "Selecciona el slot activo, luego pulsa \u2295 Asignar y presiona el botón en el mando. Izquierda/Derecha: valor 0 usa DPad + palanca.",
                Location = new Point(10, 282),
                Size = new Size(400, 26),
                AutoSize = false,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font, FontStyle.Italic),
            };

            // (4) Test
            var xiSep3 = MakeSectionLabel("Probar dispositivo", new Point(10, 318));
            btnTestXInput = new Button { Text = "\u25b6 Iniciar", Location = new Point(10, 338), Width = 110, Height = 24 };
            btnTestXInput.Click += BtnTestXInput_Click;
            lblXInputStatus = new Label { Text = "Activo: \u2014", Location = new Point(126, 342), AutoSize = true };
            visualXInput = new InputVisualPanel
            {
                Location = new Point(10, 370),
                Size = new Size(300, 180),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            lblXInputButtons = new Label
            {
                Text = "Botones: \u2014",
                Location = new Point(10, 558),
                AutoSize = true,
                Font = new Font("Courier New", 8.5f),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            lblXInputAxes = new Label
            {
                Text = "LX: \u2014 | LY: \u2014 | RX: \u2014 | RY: \u2014 | LT: \u2014 | RT: \u2014",
                Location = new Point(10, 576),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };

            grpXI.Controls.AddRange(new Control[]
            {
                chkXInputEnabled,
                xiSep1, lblXiAutoUpdate, lstXInputSlots,
                xiSep2,
                    lblFnXiSelect, lblXiBindSelect, btnXiBindSelect,
                    lblFnXiBack,   lblXiBindBack,   btnXiBindBack,
                    lblFnXiLeft,   lblXiBindLeft,   btnXiBindLeft,
                    lblFnXiRight,  lblXiBindRight,  btnXiBindRight,
                lblXiHint,
                xiSep3, btnTestXInput, lblXInputStatus,
                visualXInput, lblXInputButtons, lblXInputAxes
            });
            grpXI.Layout += (_, _) =>
            {
                int right = grpXI.ClientSize.Width - 10;
                lblXiAutoUpdate.Location = new Point(right - lblXiAutoUpdate.PreferredWidth, 50);
                lstXInputSlots.Width = right - lstXInputSlots.Left;
                foreach (var (lbl, btn) in new[]
                {
                    (lblXiBindSelect, btnXiBindSelect), (lblXiBindBack, btnXiBindBack),
                    (lblXiBindLeft,   btnXiBindLeft),   (lblXiBindRight, btnXiBindRight)
                })
                {
                    btn.Location = new Point(right - btn.Width, btn.Top);
                    lbl.Width = btn.Left - lbl.Left - 6;
                }
                lblXiHint.Width    = right - 10;
                visualXInput.Width = right - visualXInput.Left;
            };

            tblInput.Controls.Add(grpDI, 0, 0);
            tblInput.Controls.Add(grpXI, 1, 0);
            tabInput.Controls.Add(tblInput);

            tabs.TabPages.AddRange(new[] { tabGeneral, tabPaths, tabMusic, tabInput });

            // === Log tab ===
            var tabLog = new TabPage("Log") { Padding = new Padding(4) };

            txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(12, 12, 12),
                ForeColor = Color.FromArgb(0, 255, 65),
                Font = new Font("Courier New", 9f),
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
            };

            txtLog.Resize += (_, _) => RefreshLogDisplay();

            tabLog.Controls.Add(txtLog);

            tabs.TabPages.Add(tabLog);

            // "Clear Log" button — lives in the bottom panel but declared here so the tab handler can reference it
            var btnClearLog = new Button
            {
                Text = "✖ Clear Log",
                Width = 110,
                Height = 32,
                Location = new Point(12, 9),
                FlatStyle = FlatStyle.System,
                Enabled = false
            };
            btnClearLog.Click += BtnLogClear_Click;

            // Recalculate padding when the Log tab is first shown
            Button? _btnDefaults = null; // forward reference for tab handler
            tabs.SelectedIndexChanged += (_, _) =>
            {
                if (tabs.SelectedTab == tabLog)
                    BeginInvoke(RefreshLogDisplay);
                if (tabs.SelectedTab == tabPaths && !_isVideoPlaying)
                    AutoPreviewVideo();
                btnClearLog.Enabled = tabs.SelectedTab == tabLog;
                if (_btnDefaults != null)
                    _btnDefaults.Enabled = tabs.SelectedIndex < 3; // General, Directorios, Media/Led
            };

            Controls.Add(tabs);

            Shown += (_, _) => InitLogWatcher();

            // Bottom panel — defaults on the left, launch/close on the right
            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 50,
                Padding = new Padding(12, 8, 12, 8),
                BackColor = SystemColors.Control
            };

            var btnDefaults = new Button
            {
                Text = "Valores por defecto",
                Width = 150,
                Height = 32,
                Location = new Point(130, 9),
                FlatStyle = FlatStyle.System
            };
            _btnDefaults = btnDefaults;
            btnDefaults.Click += BtnDefaults_Click;

            var btnLaunch = new Button
            {
                Text = "▶ Launch App",
                Width = 120,
                Height = 32,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.System
            };
            btnLaunch.Click += BtnLaunch_Click;

            var btnClose = new Button
            {
                Text = "Close",
                Width = 90,
                Height = 32,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.System
            };
            btnClose.Click += (_, _) => Close();

            // Position right-aligned buttons relative to panel
            bottomPanel.Layout += (_, _) =>
            {
                btnClose.Location = new Point(bottomPanel.ClientSize.Width - btnClose.Width - 12, 9);
                btnLaunch.Location = new Point(btnClose.Left - btnLaunch.Width - 8, 9);
            };
            bottomPanel.Controls.AddRange(new Control[] { btnClearLog, btnDefaults, btnLaunch, btnClose });
            Controls.Add(bottomPanel);

            // Status strip at the very bottom — shows config path and save status
            statusStrip = new StatusStrip();
            lblStatusPath = new ToolStripStatusLabel
            {
                Text = $"Config: {_configPath}",
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.GrayText
            };
            lblStatusSave = new ToolStripStatusLabel
            {
                Text = "",
                ForeColor = Color.ForestGreen
            };
            statusStrip.Items.AddRange(new ToolStripItem[] { lblStatusPath, lblStatusSave });
            Controls.Add(statusStrip);

            // Wire up auto-save on every change, logging diffs
            var _prevValues = new Dictionary<string, string>();

            string GetCtlValue(Control c) => c switch
            {
                CheckBox cb => cb.Checked.ToString(),
                TrackBar tb => tb.Value.ToString(),
                NumericUpDown n => n.Value.ToString(),
                ComboBox cb => cb.SelectedItem?.ToString() ?? "",
                TextBox t => t.Text,
                _ => ""
            };

            void TrackField(string name, Control c)
            {
                _prevValues[name] = GetCtlValue(c);
            }

            void OnChanged(string fieldName, Control c)
            {
                if (_suppressDirty) return;
                var newVal = GetCtlValue(c);
                if (_prevValues.TryGetValue(fieldName, out var oldVal) && oldVal != newVal)
                    LogConfig($"{fieldName}: \"{oldVal}\" → \"{newVal}\"");
                _prevValues[fieldName] = newVal;
                AutoSave();
            }

            void WireField(string name, Control c)
            {
                TrackField(name, c);
                switch (c)
                {
                    case CheckBox cb:   cb.CheckedChanged += (_, _) => OnChanged(name, c); break;
                    case TrackBar tb:   tb.ValueChanged   += (_, _) => OnChanged(name, c); break;
                    case NumericUpDown n: n.ValueChanged   += (_, _) => OnChanged(name, c); break;
                    case ComboBox cb:   cb.SelectedIndexChanged += (_, _) => OnChanged(name, c); break;
                    case TextBox t:     t.TextChanged      += (_, _) => OnChanged(name, c); break;
                }
            }

            WireField("AppTitle", txtTitle);
            WireField("TopMost", chkTopMost);
            WireField("Logging", chkLogging);
            WireField("ToolsRoot", txtToolsRoot);
            WireField("ImagesRoot", txtImagesRoot);
            WireField("VideoBackground", txtVideoBackground);
            WireField("MusicEnabled", chkMusicEnabled);
            WireField("MusicRoot", txtMusicRoot);
            WireField("Volume", trkVolume);
            WireField("ThumbVideoVolume", trkThumbVideoVolume);
            WireField("AudioDevice", cboAudioDevice);
            WireField("PlayRandom", chkPlayRandom);
            // ListBox is not handled by WireField — wire manually
            lstMusicFiles.SelectedIndexChanged += (_, _) => { if (!_suppressDirty) AutoSave(); };
            WireField("DInputEnabled", chkDInputEnabled);
            WireField("XInputEnabled", chkXInputEnabled);
            WireField("DInputDevice", cboDInputDevice);
            // Button bindings are saved directly inside AssignButton — no WireField needed.

            // Toggle controls inside each frame based on its own enable checkbox.
            // The checkbox itself stays enabled so the user can always toggle it back on.
            void UpdateInputPanelState()
            {
                bool di = chkDInputEnabled.Checked;
                foreach (Control c in grpDI.Controls)
                    if (c != chkDInputEnabled) c.Enabled = di;
                if (!di) { StopDInputTest(); CancelBind(); }

                bool xi = chkXInputEnabled.Checked;
                    foreach (Control c in grpXI.Controls)
                    if (c != chkXInputEnabled) c.Enabled = xi;
                if (!xi) { StopXInputTest(); CancelXiBind(); }
            }
            chkDInputEnabled.CheckedChanged += (_, _) => UpdateInputPanelState();
            chkXInputEnabled.CheckedChanged += (_, _) => UpdateInputPanelState();
            gridOptions.CellValueChanged += (s, a) =>
            {
                if (!_suppressDirty)
                {
                    if (a.RowIndex >= 0 && a.ColumnIndex >= 0)
                    {
                        var colName = gridOptions.Columns[a.ColumnIndex].Name;
                        var val = gridOptions.Rows[a.RowIndex].Cells[a.ColumnIndex].Value?.ToString() ?? "";
                        LogConfig($"Grid[{a.RowIndex}].{colName} = \"{val}\"");
                    }
                    AutoSave();
                }
            };
            gridOptions.RowsAdded += (s, a) => { if (!_suppressDirty) { LogConfig($"Row added (index {a.RowIndex})"); AutoSave(); } };
            gridOptions.RowsRemoved += (s, a) => { if (!_suppressDirty) { LogConfig($"Row removed (index {a.RowIndex})"); AutoSave(); } };
        }

        private void PopulateAudioDevices()
        {
            cboAudioDevice.Items.Clear();
            cboAudioDevice.Items.Add("(System Default)");

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                MMDevice? defaultDevice = null;
                try { defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); } catch { }

                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var dev in devices)
                {
                    var name = dev.FriendlyName;
                    var isDefault = defaultDevice != null &&
                                    string.Equals(dev.ID, defaultDevice.ID, StringComparison.OrdinalIgnoreCase);
                    cboAudioDevice.Items.Add(isDefault ? name + " *" : name);
                }
            }
            catch
            {
                // If enumeration fails, the user can still pick "(System Default)"
            }

            cboAudioDevice.SelectedIndex = 0;
        }

        private void RefreshMusicFileList()
        {
            lstMusicFiles.Items.Clear();
            var root = txtMusicRoot.Text;
            if (string.IsNullOrWhiteSpace(root)) return;

            if (!Path.IsPathRooted(root))
                root = Path.Combine(Path.GetDirectoryName(_configPath) ?? ".", root);

            if (!Directory.Exists(root)) return;

            var exts = new[] { ".ogg", ".mod", ".xm", ".mp3", ".wav", ".flac" };
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly)
                         .Where(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                lstMusicFiles.Items.Add(Path.GetFileName(file));
            }
        }

        private void PreviewMusicFile(string fullPath)
        {
            try
            {
                StopMusicPreview();

                var vlc = LibVLCManager.Instance;

                _previewMedia = new LibVLCSharp.Shared.Media(vlc, fullPath, LibVLCSharp.Shared.FromType.FromPath);
                try { _previewMedia.AddOption(":no-video"); } catch { }

                _previewPlayer = new LibVLCSharp.Shared.MediaPlayer(vlc);

                // Route audio to the configured device (same approach as MusicPlayer)
                try
                {
                    _previewPlayer.SetAudioOutput("mmdevice");
                    var selectedDevice = cboAudioDevice.SelectedItem?.ToString() ?? "";
                    if (selectedDevice.EndsWith(" *")) selectedDevice = selectedDevice[..^2];
                    if (cboAudioDevice.SelectedIndex > 0 && !string.IsNullOrWhiteSpace(selectedDevice))
                    {
                        var vlcDevs = vlc.AudioOutputDevices("mmdevice");
                        if (vlcDevs != null)
                        {
                            foreach (var d in vlcDevs)
                            {
                                if (string.Equals(d.Description, selectedDevice, StringComparison.OrdinalIgnoreCase))
                                {
                                    _previewPlayer.SetOutputDevice(d.DeviceIdentifier);
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }

                // Enforce volume once VLC actually starts playing (Play() is async)
                var vol = trkVolume.Value;
                _previewPlayer.Playing += (_, _) =>
                {
                    try { _previewPlayer.Volume = vol; } catch { }
                };
                _previewPlayer.EncounteredError += (_, _) =>
                {
                    BeginInvoke(() => lblStatusSave.Text = "Preview: playback error");
                };

                _previewPlayer.Media = _previewMedia;
                _previewPlayer.Play();
                _previewPlayer.Volume = vol;

                lblStatusSave.Text = $"Playing: {Path.GetFileName(fullPath)}";
            }
            catch (Exception ex)
            {
                lblStatusSave.Text = $"Preview error: {ex.Message}";
            }
        }

        private void StopMusicPreview()
        {
            try
            {
                if (_previewPlayer != null)
                {
                    _previewPlayer.Stop();
                    _previewPlayer.Dispose();
                    _previewPlayer = null;
                }
                // Dispose media AFTER player so VLC finishes any pending I/O
                if (_previewMedia != null)
                {
                    _previewMedia.Dispose();
                    _previewMedia = null;
                }
            }
            catch { }
        }

        // --- Video preview ---

        private void WarmUpVideoPreview()
        {
            Task.Run(async () =>
            {
                try
                {
                    var vlc = await _vlcInitTask!;
                    BeginInvoke(() =>
                    {
                        _previewLibVlc = vlc;
                        _videoPreviewPlayer = new LibVLCSharp.Shared.MediaPlayer(_previewLibVlc);
                        _videoPreviewPlayer.Hwnd = pnlArcadeScreen.Handle;
                        _videoPreviewPlayer.Mute = true;
                        _videoPreviewPlayer.EndReached += (_, _) => BeginInvoke(StopVideoPreview);
                        _videoPreviewPlayer.Playing += (_, _) =>
                        {
                            _videoStartWatch?.Stop();
                            var ms = _videoStartWatch?.ElapsedMilliseconds ?? 0;
                            BeginInvoke(() => lblStatusSave.Text = $"Video started in {ms} ms");
                        };
                        lblStatusSave.Text = "";
                    });
                }
                catch { BeginInvoke(() => lblStatusSave.Text = ""); }
            });
        }

        private void PositionArcadeScreen()
        {
            if (picVideoThumb.Image == null) return;
            var img = picVideoThumb.Image;
            // Calculate the rendered image rect inside Zoom mode
            float scaleX = (float)picVideoThumb.Width / img.Width;
            float scaleY = (float)picVideoThumb.Height / img.Height;
            float scale = Math.Min(scaleX, scaleY);
            int rw = (int)(img.Width * scale);
            int rh = (int)(img.Height * scale);
            int rx = (picVideoThumb.Width - rw) / 2;
            int ry = (picVideoThumb.Height - rh) / 2;

            // Screen area mapped from 1080x1080 cabinet PNG pixel coordinates
            const float screenLeft = 0.295f;
            const float screenTop = 0.170f;
            const float screenWidth = 0.385f;
            const float screenHeight = 0.220f;

            pnlArcadeScreen.Location = new Point(
                rx + (int)(rw * screenLeft),
                ry + (int)(rh * screenTop));
            pnlArcadeScreen.Size = new Size(
                (int)(rw * screenWidth),
                (int)(rh * screenHeight));
        }

        private void AutoPreviewVideo()
        {
            if (!IsHandleCreated) { RefreshVideoThumb(); return; }
            StopVideoPreview();
            var resolved = ResolveVideoPath(txtVideoBackground.Text);
            if (resolved == null)
            {
                RefreshVideoThumb();
                return;
            }
            StartVideoPreview(resolved);
        }

        private string? ResolveVideoPath(string videoPath)
        {
            var fullPath = videoPath;
            if (!Path.IsPathRooted(fullPath))
            {
                var baseDir = Path.GetDirectoryName(_configPath) ?? ".";
                var candidate = Path.Combine(baseDir, fullPath);
                if (File.Exists(candidate)) fullPath = candidate;
            }
            return File.Exists(fullPath) ? fullPath : null;
        }

        private void StartVideoPreview(string fullPath)
        {
            try
            {
                if (_previewLibVlc == null)
                {
                    LibVLCSharp.Shared.Core.Initialize();
                    _previewLibVlc = new LibVLCSharp.Shared.LibVLC(
                        "--no-osd", "--no-snapshot-preview", "--no-stats",
                        "--no-sub-autodetect-file", "--no-metadata-network-access");
                }

                _videoStartWatch = System.Diagnostics.Stopwatch.StartNew();

                if (_videoPreviewPlayer == null)
                {
                    _videoPreviewPlayer = new LibVLCSharp.Shared.MediaPlayer(_previewLibVlc);
                    _videoPreviewPlayer.Hwnd = pnlArcadeScreen.Handle;
                    _videoPreviewPlayer.Mute = true;
                    _videoPreviewPlayer.EndReached += (_, _) => BeginInvoke(StopVideoPreview);
                    _videoPreviewPlayer.Playing += (_, _) =>
                    {
                        _videoStartWatch?.Stop();
                        var ms = _videoStartWatch?.ElapsedMilliseconds ?? 0;
                        BeginInvoke(() => lblStatusSave.Text = $"Video started in {ms} ms");
                    };
                }
                else
                {
                    _videoPreviewPlayer.Stop();
                }

                using var media = new LibVLCSharp.Shared.Media(_previewLibVlc, fullPath, LibVLCSharp.Shared.FromType.FromPath);
                pnlArcadeScreen.BackgroundImage = null;
                _videoPreviewPlayer.Play(media);
                _isVideoPlaying = true;
                btnStopVideo.Visible = true;
                btnStopVideo.BringToFront();

                // Auto-stop after 10 seconds
                _videoPreviewTimer?.Stop();
                _videoPreviewTimer?.Dispose();
                _videoPreviewTimer = new System.Windows.Forms.Timer { Interval = 10_000 };
                _videoPreviewTimer.Tick += (_, _) => { _videoPreviewTimer?.Stop(); StopVideoPreview(); };
                _videoPreviewTimer.Start();
            }
            catch { }
        }

        private void StopVideoPreview()
        {
            try
            {
                _videoPreviewPlayer?.Stop();
            }
            catch { }
            _isVideoPlaying = false;
            _videoPreviewTimer?.Stop();
            _videoPreviewTimer?.Dispose();
            _videoPreviewTimer = null;
            btnStopVideo.Visible = false;
            RefreshVideoThumb();
        }

        private void ShowTrackerMetadata(RichTextBox rtb, string filePath)
        {
            rtb.Clear();
            var meta = TrackerMetadata.TryRead(filePath);
            if (meta == null)
            {
                rtb.Text = Path.GetFileName(filePath);
                return;
            }

            rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
            rtb.SelectionColor = Color.FromArgb(100, 220, 255);
            rtb.AppendText(meta.Title + Environment.NewLine);

            rtb.SelectionFont = rtb.Font;
            rtb.SelectionColor = Color.FromArgb(180, 180, 180);
            rtb.AppendText($"Format: {meta.Format}   Channels: {meta.Channels}" + Environment.NewLine);
            rtb.AppendText($"Patterns: {meta.Patterns}   BPM: {meta.Bpm}   Tempo: {meta.Tempo}" + Environment.NewLine);
            if (meta.Tracker != null)
                rtb.AppendText($"Tracker: {meta.Tracker}" + Environment.NewLine);

            if (meta.SampleNames.Count > 0)
            {
                rtb.AppendText(Environment.NewLine);
                rtb.SelectionFont = new Font(rtb.Font, FontStyle.Bold);
                rtb.SelectionColor = Color.FromArgb(255, 200, 80);
                rtb.AppendText("Samples / Instruments:" + Environment.NewLine);

                rtb.SelectionFont = rtb.Font;
                rtb.SelectionColor = Color.FromArgb(0, 200, 80);
                foreach (var name in meta.SampleNames)
                    rtb.AppendText("  " + name + Environment.NewLine);
            }
        }

        private void LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                MessageBox.Show($"Config file not found:\n{_configPath}\n\nA default config will be used.",
                    "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _config = new AppConfig();
                PopulateUI();
                return;
            }

            var (cfg, err) = AppConfig.TryLoadFromFile(_configPath);
            if (cfg == null)
            {
                MessageBox.Show($"Error loading config:\n{err}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _config = cfg;
            PopulateUI();
        }

        private void PopulateUI()
        {
            _suppressDirty = true;

            // General
            txtTitle.Text = _config.Ui.Title;
            chkTopMost.Checked = _config.Ui.TopMost;
            chkLogging.Checked = _config.Activa.Activa;

            // Paths
            txtToolsRoot.Text = _config.Paths.ToolsRoot;
            txtImagesRoot.Text = _config.Paths.ImagesRoot;

            // Video background
            txtVideoBackground.Text = _config.Paths.VideoBackground;

            // Music
            chkMusicEnabled.Checked = _config.Music.Enabled;
            txtMusicRoot.Text = _config.Music.MusicRoot ?? "";
            trkVolume.Value = Math.Clamp(_config.Music.Volume, 0, 100);
            lblVolumeValue.Text = $"{trkVolume.Value}%";
            trkThumbVideoVolume.Value = Math.Clamp(_config.Music.ThumbVideoVolume, 0, 100);
            lblThumbVideoVolumeValue.Text = $"{trkThumbVideoVolume.Value}%";
            chkPlayRandom.Checked = _config.Music.PlayRandom;
            lstMusicFiles.Enabled = !chkPlayRandom.Checked;
            RefreshMusicFileList();
            // Select the saved file in the list (case-insensitive to handle filesystem variations)
            if (!string.IsNullOrWhiteSpace(_config.Music.SelectedFile))
            {
                for (int i = 0; i < lstMusicFiles.Items.Count; i++)
                {
                    if (string.Equals(lstMusicFiles.Items[i] as string, _config.Music.SelectedFile, StringComparison.OrdinalIgnoreCase))
                    {
                        lstMusicFiles.SelectedIndex = i;
                        break;
                    }
                }
            }
            // Select the matching audio device in the dropdown
            var savedDevice = _config.Music.AudioDevice ?? "";
            cboAudioDevice.SelectedIndex = 0; // default = system default
            if (!string.IsNullOrWhiteSpace(savedDevice))
            {
                for (int i = 0; i < cboAudioDevice.Items.Count; i++)
                {
                    var item = cboAudioDevice.Items[i]?.ToString() ?? "";
                    var cleanItem = item.EndsWith(" *") ? item[..^2] : item;
                    if (string.Equals(cleanItem, savedDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        cboAudioDevice.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Input
            chkXInputEnabled.Checked = _config.Input.XInputEnabled;
            chkDInputEnabled.Checked = _config.Input.DInputEnabled;
            // cboDInputDevice is populated by ScanDInputDevices() (called in Shown);
            // just ensure a safe default until the scan completes.
            if (cboDInputDevice.Items.Count == 0)
                cboDInputDevice.Items.Add("(Primero disponible)");
            cboDInputDevice.SelectedIndex = 0;

            // LEDBlinky
            chkLedBlinkyEnabled.Checked = _config.LedBlinky.Enabled;
            txtLedBlinkyExe.Text = _config.LedBlinky.ExePath;
            txtLedBlinkyExe.Enabled = chkLedBlinkyEnabled.Checked;

            // Load saved button bindings into stored fields, then refresh binding display labels
            _bindSelectBtn = Math.Max(1, _config.Input.DInputButtonSelect);
            _bindBackBtn   = Math.Max(1, _config.Input.DInputButtonBack);
            _bindLeftBtn   = Math.Max(0, _config.Input.DInputButtonLeft);
            _bindRightBtn  = Math.Max(0, _config.Input.DInputButtonRight);
            UpdateBindLabels();

            // Load XInput button bindings
            _xiBindSelectBtn = _config.Input.XInputButtonSelect;
            _xiBindBackBtn   = _config.Input.XInputButtonBack;
            _xiBindLeftBtn   = _config.Input.XInputButtonLeft;
            _xiBindRightBtn  = _config.Input.XInputButtonRight;
            UpdateXiBindLabels();

            // Sync enabled state inside each frame (checkbox stays always enabled)
            foreach (Control c in grpDI.Controls)
                if (c != chkDInputEnabled) c.Enabled = chkDInputEnabled.Checked;
            foreach (Control c in grpXI.Controls)
                if (c != chkXInputEnabled) c.Enabled = chkXInputEnabled.Checked;

            // Options
            gridOptions.Rows.Clear();
            foreach (var opt in _config.Options)
            {
                var thumb = LoadThumbnail(opt.Image);
                var vidThumb = LoadVideoThumbnail(opt.ThumbVideo);
                gridOptions.Rows.Add(opt.Label, opt.Exe, "...", thumb, opt.Image, "...", vidThumb, opt.ThumbVideo ?? "", "...");
            }

            _suppressDirty = false;

            // Show metadata for the music file that was restored from config
            if (!chkPlayRandom.Checked && lstMusicFiles.SelectedItem is string selFile)
            {
                var root = txtMusicRoot.Text;
                if (!string.IsNullOrWhiteSpace(root))
                {
                    var fullPath = Path.IsPathRooted(root)
                        ? Path.Combine(root, selFile)
                        : Path.Combine(Path.GetDirectoryName(_configPath) ?? ".", root, selFile);
                    if (File.Exists(fullPath))
                        ShowTrackerMetadata(txtMetaInfo, fullPath);
                }
            }
        }

        private void CollectFromUI()
        {
            _config.Ui.Title = txtTitle.Text;
            _config.Ui.TopMost = chkTopMost.Checked;
            _config.Activa.Activa = chkLogging.Checked;

            _config.Paths.ToolsRoot = txtToolsRoot.Text;
            _config.Paths.ImagesRoot = txtImagesRoot.Text;
            _config.Paths.VideoBackground = txtVideoBackground.Text;

            _config.Music.Enabled = chkMusicEnabled.Checked;
            _config.Music.MusicRoot = txtMusicRoot.Text;
            _config.Music.Volume = trkVolume.Value;
            _config.Music.ThumbVideoVolume = trkThumbVideoVolume.Value;
            _config.Music.PlayRandom = chkPlayRandom.Checked;
            _config.Music.SelectedFile = chkPlayRandom.Checked ? "" : (lstMusicFiles.SelectedItem as string ?? "");
            var selectedDevice = cboAudioDevice.SelectedItem?.ToString() ?? "";
            if (selectedDevice.EndsWith(" *")) selectedDevice = selectedDevice[..^2];
            _config.Music.AudioDevice = (cboAudioDevice.SelectedIndex <= 0 || string.IsNullOrWhiteSpace(selectedDevice))
                ? null : selectedDevice;

            _config.Input.XInputEnabled      = chkXInputEnabled.Checked;
            _config.Input.DInputEnabled      = chkDInputEnabled.Checked;
            _config.Input.DInputButtonSelect = _bindSelectBtn;
            _config.Input.DInputButtonBack   = _bindBackBtn;
            _config.Input.DInputButtonLeft   = _bindLeftBtn;
            _config.Input.DInputButtonRight  = _bindRightBtn;
            // Preferred DInput device: index 0 = "first found" = empty string
            var devSel = cboDInputDevice.SelectedIndex;
            _config.Input.DInputDeviceName = (devSel > 0 && devSel - 1 < _dinputDeviceList.Count)
                ? _dinputDeviceList[devSel - 1].ProductName
                : string.Empty;
            // XInput slot: listbox index = slot number (0-3); -1 = first connected
            _config.Input.XInputSlot          = lstXInputSlots.SelectedIndex >= 0 ? lstXInputSlots.SelectedIndex : -1;
            _config.Input.XInputButtonSelect  = _xiBindSelectBtn;
            _config.Input.XInputButtonBack    = _xiBindBackBtn;
            _config.Input.XInputButtonLeft    = _xiBindLeftBtn;
            _config.Input.XInputButtonRight   = _xiBindRightBtn;

            _config.LedBlinky.Enabled = chkLedBlinkyEnabled.Checked;
            _config.LedBlinky.ExePath = txtLedBlinkyExe.Text;

            _config.Options.Clear();
            foreach (DataGridViewRow row in gridOptions.Rows)
            {
                if (row.IsNewRow) continue;
                var label = row.Cells["Label"].Value?.ToString() ?? "";
                var exe = row.Cells["Exe"].Value?.ToString() ?? "";
                var image = row.Cells["ImagePath"].Value?.ToString() ?? "";
                var thumbVideo = row.Cells["ThumbVideoPath"].Value?.ToString();
                if (string.IsNullOrWhiteSpace(label) && string.IsNullOrWhiteSpace(exe)) continue;
                _config.Options.Add(new OptionConfig
                {
                    Label = label,
                    Exe = exe,
                    Image = image,
                    ThumbVideo = string.IsNullOrWhiteSpace(thumbVideo) ? null : thumbVideo,
                });
            }
        }

        private void LogConfig(string message)
        {
            if (string.IsNullOrEmpty(_logFilePath)) return;
            try
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] [CONFIG] {message}{Environment.NewLine}";
                File.AppendAllText(_logFilePath, line);
            }
            catch { }
        }

        private void AutoSave()
        {
            CollectFromUI();
            try
            {
                var opts = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };
                var json = JsonSerializer.Serialize(_config, opts);
                var paths = _allConfigPaths.Count > 0 ? _allConfigPaths : new List<string> { _configPath };
                foreach (var path in paths)
                    File.WriteAllText(path, json);

                // Flash save confirmation in the status bar — include selectedFile for visibility
                var sel = _config.Music.SelectedFile;
                var selInfo = string.IsNullOrEmpty(sel) ? "" : $" | music={sel}";
                lblStatusSave.Text = $"Saved ({paths.Count} file{(paths.Count > 1 ? "s" : "")}){selInfo}";
                var fadeTimer = new System.Windows.Forms.Timer { Interval = 2000 };
                fadeTimer.Tick += (_, _) => { lblStatusSave.Text = ""; fadeTimer.Dispose(); };
                fadeTimer.Start();
            }
            catch (Exception ex)
            {
                lblStatusSave.Text = "✗ Save error";
                lblStatusSave.ForeColor = Color.Red;
                MessageBox.Show($"Error saving config:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                var resetTimer = new System.Windows.Forms.Timer { Interval = 3000 };
                resetTimer.Tick += (_, _) => { lblStatusSave.Text = ""; lblStatusSave.ForeColor = Color.ForestGreen; resetTimer.Dispose(); };
                resetTimer.Start();
            }
        }

        private void BtnDefaults_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show("¿Restaurar todos los valores por defecto?",
                    "Valores por defecto", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _config = new AppConfig();
            PopulateUI();
            AutoSave();
        }

        private void BtnLaunch_Click(object? sender, EventArgs e)
        {
            const string processName = "ArcadeShellSelector";
            var existing = Process.GetProcessesByName(processName);
            if (existing.Length > 0)
            {
                MessageBox.Show($"{processName} is already running.", "Info",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Look for the exe next to the config file (solution root)
            var dir = Path.GetDirectoryName(_configPath) ?? ".";
            var exe = Path.Combine(dir, "bin", "Release", "net10.0-windows", $"{processName}.exe");
            if (!File.Exists(exe))
                exe = Path.Combine(dir, "bin", "Debug", "net10.0-windows", $"{processName}.exe");

            if (!File.Exists(exe))
            {
                MessageBox.Show($"Cannot find {processName}.exe.\nLooked in:\n{dir}",
                    "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            Close();
        }

        private static void BrowseFolder(TextBox target, string configPath)
        {
            using var dlg = new FolderBrowserDialog();
            var path = target.Text.Trim();
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (!Path.IsPathRooted(path))
                    path = Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(configPath) ?? ".", path));
                if (Directory.Exists(path))
                    dlg.SelectedPath = path;
            }
            if (dlg.ShowDialog() == DialogResult.OK)
                target.Text = dlg.SelectedPath;
        }

        private static void BrowseFile(TextBox target, string filter)
        {
            using var dlg = new OpenFileDialog { Filter = filter };
            if (File.Exists(target.Text))
                dlg.FileName = target.Text;
            else if (Directory.Exists(Path.GetDirectoryName(target.Text)))
                dlg.InitialDirectory = Path.GetDirectoryName(target.Text);
            if (dlg.ShowDialog() == DialogResult.OK)
                target.Text = dlg.FileName;
        }

        private static void BrowseDrive(TextBox target)
        {
            using var dlg = new FolderBrowserDialog();
            var text = target.Text.Trim();
            if (text.Length >= 2 && text[1] == ':')
            {
                var root = Path.GetPathRoot(text);
                if (root != null && Directory.Exists(root))
                    dlg.SelectedPath = root;
            }
            else if (Directory.Exists(text))
            {
                dlg.SelectedPath = text;
            }
            if (dlg.ShowDialog() == DialogResult.OK)
                target.Text = dlg.SelectedPath;
        }

        private static void BrowseFolderUnder(TextBox target, string baseDir, string configPath)
        {
            using var dlg = new FolderBrowserDialog();
            var targetPath = target.Text.Trim();
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                // Resolve relative paths against the solution root (where config.json lives)
                if (!Path.IsPathRooted(targetPath))
                    targetPath = Path.GetFullPath(Path.Combine(
                        Path.GetDirectoryName(configPath) ?? ".", targetPath));
                if (Directory.Exists(targetPath))
                    dlg.SelectedPath = targetPath;
            }
            if (string.IsNullOrEmpty(dlg.SelectedPath) && Directory.Exists(baseDir))
                dlg.SelectedPath = baseDir;
            if (dlg.ShowDialog() == DialogResult.OK)
                target.Text = dlg.SelectedPath;
        }

        private void BrowseAndDeployVideo()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Video files|*.mp4;*.avi;*.mkv;*.wmv|All files|*.*",
            };
            if (File.Exists(txtVideoBackground.Text))
                dlg.FileName = txtVideoBackground.Text;
            else if (Directory.Exists(Path.GetDirectoryName(txtVideoBackground.Text)))
                dlg.InitialDirectory = Path.GetDirectoryName(txtVideoBackground.Text);
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var srcFile = dlg.FileName;

            // Resolve the Bkg folder next to the solution root (where config.json lives)
            var solutionDir = Path.GetDirectoryName(_configPath) ?? ".";
            var bkgDir = Path.Combine(solutionDir, "Media", "Bkg");
            try { Directory.CreateDirectory(bkgDir); } catch { }

            // Also deploy to the main app's output Bkg folder
            var binBkgDir = Path.Combine(solutionDir, "bin", "Release", "net10.0-windows", "Media", "Bkg");
            try { Directory.CreateDirectory(binBkgDir); } catch { }

            // Remove existing video files from both Bkg folders
            var videoExts = new[] { ".mp4", ".avi", ".mkv", ".wmv", ".mov", ".ogg" };
            foreach (var dir in new[] { bkgDir, binBkgDir })
            {
                try
                {
                    foreach (var old in Directory.GetFiles(dir)
                        .Where(f => videoExts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
                    {
                        try { File.Delete(old); } catch { }
                    }
                }
                catch { }
            }

            // Copy the new video into both Bkg folders
            var destName = Path.GetFileName(srcFile);
            var destSource = Path.Combine(bkgDir, destName);
            var destBin = Path.Combine(binBkgDir, destName);
            try { File.Copy(srcFile, destSource, true); } catch { }
            try { File.Copy(srcFile, destBin, true); } catch { }

            txtVideoBackground.Text = destSource;
        }

        private void GridOptions_CellContentClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            var row = gridOptions.Rows[e.RowIndex];
            if (row.IsNewRow) return;

            var colName = gridOptions.Columns[e.ColumnIndex].Name;
            if (colName == "DeleteRow")
            {
                var label = row.Cells["Label"].Value?.ToString() ?? "";
                var msg = string.IsNullOrWhiteSpace(label)
                    ? $"¿Eliminar la fila {e.RowIndex + 1}?"
                    : $"¿Eliminar \"{label}\"?";
                if (MessageBox.Show(msg, "Eliminar opción", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    gridOptions.Rows.RemoveAt(e.RowIndex);
                    AutoSave();
                }
                return;
            }
            if (colName == "BrowseExe")
            {
                var exePath = row.Cells["Exe"].Value?.ToString();
                using var dlg = new OpenFileDialog { Filter = "Executables|*.exe|All files|*.*" };
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    if (File.Exists(exePath)) dlg.FileName = exePath;
                    else if (Directory.Exists(Path.GetDirectoryName(exePath))) dlg.InitialDirectory = Path.GetDirectoryName(exePath);
                    else if (Directory.Exists(_config.Paths.ToolsRoot)) dlg.InitialDirectory = _config.Paths.ToolsRoot;
                }
                if (dlg.ShowDialog() == DialogResult.OK)
                    row.Cells["Exe"].Value = dlg.FileName;
            }
            else if (colName == "BrowseImage")
            {
                var imgPath = row.Cells["ImagePath"].Value?.ToString();
                using var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.bmp;*.gif|All files|*.*" };
                if (!string.IsNullOrWhiteSpace(imgPath))
                {
                    if (File.Exists(imgPath)) dlg.FileName = imgPath;
                    else if (Directory.Exists(Path.GetDirectoryName(imgPath))) dlg.InitialDirectory = Path.GetDirectoryName(imgPath);
                    else if (Directory.Exists(_config.Paths.ImagesRoot)) dlg.InitialDirectory = _config.Paths.ImagesRoot;
                }
                else if (Directory.Exists(_config.Paths.ImagesRoot))
                {
                    dlg.InitialDirectory = _config.Paths.ImagesRoot;
                }
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    var srcFile = dlg.FileName;
                    var solutionDir = Path.GetDirectoryName(_configPath) ?? ".";
                    var imgDir = Path.Combine(solutionDir, "Media", "Img");
                    try { Directory.CreateDirectory(imgDir); } catch { }

                    var binImgDir = Path.Combine(solutionDir, "bin", "Release", "net10.0-windows", "Media", "Img");
                    try { Directory.CreateDirectory(binImgDir); } catch { }

                    var destName  = Path.GetFileName(srcFile);
                    var destLocal = Path.Combine(imgDir, destName);
                    var destBin   = Path.Combine(binImgDir, destName);
                    try { File.Copy(srcFile, destLocal, true); } catch { }
                    try { File.Copy(srcFile, destBin,   true); } catch { }

                    row.Cells["ImagePath"].Value  = destLocal;
                    row.Cells["ImageThumb"].Value = LoadThumbnail(destLocal);
                }
            }
            else if (colName == "BrowseThumbVideo")
            {
                var vidPath = row.Cells["ThumbVideoPath"].Value?.ToString();
                using var dlg = new OpenFileDialog { Filter = "Video files|*.mp4;*.avi;*.mkv;*.wmv|All files|*.*" };
                if (!string.IsNullOrWhiteSpace(vidPath))
                {
                    if (File.Exists(vidPath)) dlg.FileName = vidPath;
                    else if (Directory.Exists(Path.GetDirectoryName(vidPath))) dlg.InitialDirectory = Path.GetDirectoryName(vidPath);
                }
                if (dlg.ShowDialog() != DialogResult.OK) return;

                var srcFile = dlg.FileName;
                var solutionDir = Path.GetDirectoryName(_configPath) ?? ".";
                var videoDir = Path.Combine(solutionDir, "Media", "Video");
                try { Directory.CreateDirectory(videoDir); } catch { }

                // Also keep a copy in the build output folder
                var binVideoDir = Path.Combine(solutionDir, "bin", "Release", "net10.0-windows", "Media", "Video");
                try { Directory.CreateDirectory(binVideoDir); } catch { }

                var destName = Path.GetFileName(srcFile);
                var destLocal = Path.Combine(videoDir, destName);
                var destBin   = Path.Combine(binVideoDir, destName);
                try { File.Copy(srcFile, destLocal, true); } catch { }
                try { File.Copy(srcFile, destBin,   true); } catch { }

                row.Cells["ThumbVideoPath"].Value = destLocal;
                row.Cells["VideoThumb"].Value = LoadVideoThumbnail(destLocal);
            }
        }

        private Image GetNoMediaThumb(int w, int h)
        {
            // Try loading a custom placeholder from Media\Img\no-media.png
            var baseDir = Path.GetDirectoryName(_configPath) ?? ".";
            var candidate = Path.Combine(baseDir, "Media", "Img", "no-media.png");
            if (File.Exists(candidate))
            {
                try
                {
                    using var src = Image.FromFile(candidate);
                    return (Image)src.GetThumbnailImage(w, h, () => false, IntPtr.Zero);
                }
                catch { }
            }
            return MakeNoMediaThumb(w, h);
        }

        private static Bitmap MakeNoMediaThumb(int w, int h)
        {
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.FromArgb(80, 80, 80));

            // Draw a white alert triangle
            float cx = w / 2f;
            float cy = h / 2f;
            float size = Math.Min(w, h) * 0.55f;
            var tri = new PointF[]
            {
                new(cx,           cy - size * 0.55f),
                new(cx + size * 0.5f, cy + size * 0.45f),
                new(cx - size * 0.5f, cy + size * 0.45f),
            };
            using var fillBrush = new SolidBrush(Color.White);
            using var borderPen = new Pen(Color.FromArgb(80, 80, 80), Math.Max(1f, size * 0.06f));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillPolygon(fillBrush, tri);

            // Exclamation mark in grey
            using var excBrush = new SolidBrush(Color.FromArgb(80, 80, 80));
            float lineH = size * 0.28f;
            float dotR  = size * 0.06f;
            float barW  = size * 0.10f;
            float barTop = cy - size * 0.28f;
            g.FillRectangle(excBrush, cx - barW / 2f, barTop, barW, lineH);
            g.FillEllipse(excBrush, cx - dotR, barTop + lineH + dotR * 0.6f, dotR * 2f, dotR * 2f);

            return bmp;
        }

        private Image LoadThumbnail(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return MakeNoMediaThumb(48, 48);

            var fullPath = imagePath;
            if (!Path.IsPathRooted(fullPath))
            {
                // Try resolving relative to imagesRoot (from config)
                var imagesRoot = _config.Paths.ImagesRoot;
                if (!string.IsNullOrWhiteSpace(imagesRoot))
                {
                    var baseDir = Path.GetDirectoryName(_configPath) ?? ".";
                    var resolved = Path.IsPathRooted(imagesRoot)
                        ? Path.Combine(imagesRoot, fullPath)
                        : Path.Combine(baseDir, imagesRoot, fullPath);
                    if (File.Exists(resolved)) fullPath = resolved;
                }
                // Also try relative to config directory
                if (!File.Exists(fullPath))
                {
                    var candidate = Path.Combine(Path.GetDirectoryName(_configPath) ?? ".", fullPath);
                    if (File.Exists(candidate)) fullPath = candidate;
                }
            }

            try
            {
                if (!File.Exists(fullPath)) return MakeNoMediaThumb(48, 48);
                using var img = Image.FromFile(fullPath);
                return (Image)img.GetThumbnailImage(48, 48, () => false, IntPtr.Zero);
            }
            catch { return MakeNoMediaThumb(48, 48); }
        }

        private Image LoadVideoThumbnail(string? videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath)) return MakeNoMediaThumb(80, 48);

            var fullPath = videoPath;
            if (!Path.IsPathRooted(fullPath))
            {
                var baseDir = Path.GetDirectoryName(_configPath) ?? ".";
                var candidate = Path.Combine(baseDir, fullPath);
                if (File.Exists(candidate)) fullPath = candidate;
            }

            try
            {
                if (!File.Exists(fullPath)) return MakeNoMediaThumb(80, 48);
                return GetShellThumbnail(fullPath, 80, 48) ?? MakeNoMediaThumb(80, 48);
            }
            catch { return MakeNoMediaThumb(80, 48); }
        }

        private void RefreshVideoThumb()
        {
            // Show thumbnail in the arcade screen panel via a background image
            var thumb = LoadVideoThumbnail(txtVideoBackground.Text);
            pnlArcadeScreen.BackgroundImage = thumb;
            pnlArcadeScreen.BackgroundImageLayout = ImageLayout.Zoom;
        }

        private void ScanDInputDevices()
        {
            _dinputDeviceList.Clear();

            Task.Run(() =>
            {
                var found = new List<DeviceInstance>();
                string? error = null;
                try
                {
                    using var di = new DirectInput();
                    found = di
                        .GetDevices(SharpDX.DirectInput.DeviceType.Gamepad,  DeviceEnumerationFlags.AttachedOnly)
                        .Concat(di.GetDevices(SharpDX.DirectInput.DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly))
                        .Where(d => !d.ProductName.Contains("XINPUT", StringComparison.OrdinalIgnoreCase))
                        .GroupBy(d => d.InstanceGuid)
                        .Select(g => g.First())
                        .ToList();
                }
                catch (Exception ex) { error = ex.Message; }

                BeginInvoke(() =>
                {
                    _dinputDeviceList.Clear();
                    if (error == null)
                        foreach (var d in found)
                            _dinputDeviceList.Add(d);

                    // Rebuild the "active device" combo — index 0 = first found (default)
                    _suppressDirty = true;
                    cboDInputDevice.Items.Clear();
                    cboDInputDevice.Items.Add("(Primero disponible)");
                    int comboSel = 0;
                    var savedName = _config.Input.DInputDeviceName;
                    foreach (var d in _dinputDeviceList)
                    {
                        var (vid, pid) = ExtractVidPid(d.ProductGuid);
                        cboDInputDevice.Items.Add($"[VID:{vid:X4} PID:{pid:X4}]  {d.ProductName}");
                        if (!string.IsNullOrWhiteSpace(savedName) &&
                            string.Equals(d.ProductName, savedName, StringComparison.OrdinalIgnoreCase))
                            comboSel = cboDInputDevice.Items.Count - 1;
                    }
                    cboDInputDevice.SelectedIndex = comboSel;
                    _suppressDirty = false;
                });
            });
        }

        private void ScanXInputSlots()
        {
            lstXInputSlots.Items.Clear();
            lstXInputSlots.Items.Add("Escaneando\u2026");

            Task.Run(() =>
            {
                var lines = new List<string>();
                int firstConnected = -1;
                for (int i = 0; i < 4; i++)
                {
                    var c = new Controller((UserIndex)i);
                    if (c.IsConnected)
                    {
                        var caps = c.GetCapabilities(DeviceQueryType.Any);
                        bool wireless = caps.Flags.HasFlag(CapabilityFlags.Wireless);
                        lines.Add($"Slot {i + 1}: {caps.SubType}{(wireless ? " [inal\u00e1mbrico]" : "")}  [conectado]");
                        if (firstConnected < 0) firstConnected = i;
                    }
                    else
                    {
                        lines.Add($"Slot {i + 1}: no conectado");
                    }
                }

                BeginInvoke(() =>
                {
                    lstXInputSlots.Items.Clear();
                    foreach (var l in lines) lstXInputSlots.Items.Add(l);
                    // Prefer the configured slot; fall back to first connected
                    int configuredSlot = _config.Input.XInputSlot;
                    if (configuredSlot >= 0 && configuredSlot < lstXInputSlots.Items.Count)
                        lstXInputSlots.SelectedIndex = configuredSlot;
                    else if (firstConnected >= 0)
                        lstXInputSlots.SelectedIndex = firstConnected;
                });
            });
        }

        private static (ushort vid, ushort pid) ExtractVidPid(Guid productGuid)
        {
            // DirectInput HID ProductGuid: bytes 0-1 = VID, bytes 2-3 = PID (little-endian)
            var b = productGuid.ToByteArray();
            return ((ushort)(b[0] | (b[1] << 8)), (ushort)(b[2] | (b[3] << 8)));
        }

        private void BtnTestDInput_Click(object? sender, EventArgs e)
        {
            if (_testTimer != null)
            {
                StopDInputTest();
                btnTestDInput.Text  = "\u25b6 Iniciar";
                lblTestDevice.Text  = "Activo: \u2014";
                lblTestButtons.Text = "Botones: \u2014";
                lblTestAxes.Text    = "Eje X: \u2014 | Eje Y: \u2014 | POV: \u2014";
                return;
            }

            // Auto-scan if list is empty
            if (_dinputDeviceList.Count == 0) ScanDInputDevices();
            if (_dinputDeviceList.Count == 0)
            {
                MessageBox.Show("No se encontr\u00f3 ning\u00fan dispositivo DirectInput (no XInput).",
                    "Sin dispositivo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Use the device chosen in "Dispositivo activo" (index 0 = first found)
            int comboSel = cboDInputDevice.SelectedIndex - 1; // index 0 is "(Primero disponible)"
            var deviceInfo = _dinputDeviceList[comboSel >= 0 && comboSel < _dinputDeviceList.Count ? comboSel : 0];
            try
            {
                _testDInput = new DirectInput();
                _testJoystick = new Joystick(_testDInput, deviceInfo.InstanceGuid);
                _testJoystick.SetCooperativeLevel(Handle, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                _testJoystick.Acquire();

                var (vid, pid) = ExtractVidPid(deviceInfo.ProductGuid);
                lblTestDevice.Text = $"Activo: {deviceInfo.ProductName}  [VID:{vid:X4} PID:{pid:X4}]";

                _testTimer = new System.Windows.Forms.Timer { Interval = 100 };
                _testTimer.Tick += TestTimer_Tick;
                _testTimer.Start();
                btnTestDInput.Text = "\u25a0 Detener";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al iniciar prueba:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                StopDInputTest();
            }
        }

        private void TestTimer_Tick(object? sender, EventArgs e)
        {
            if (_testJoystick == null) return;
            try
            {
                _testJoystick.Poll();
                var state = _testJoystick.GetCurrentState();

                var pressed = state.Buttons
                    .Select((b, i) => (b, i + 1))
                    .Where(x => x.b)
                    .Select(x => x.Item2.ToString())
                    .ToArray();
                lblTestButtons.Text = pressed.Length > 0
                    ? $"Botones: [{string.Join("] [", pressed)}]"
                    : "Botones: \u2014";

                string povText = "\u2014";
                var pov = state.PointOfViewControllers;
                if (pov != null && pov.Length > 0 && pov[0] != -1)
                {
                    int deg = pov[0] / 100;
                    povText = deg switch
                    {
                        >= 315 or < 45   => "\u2191 Arriba",
                        >= 45 and < 135  => "\u2192 Derecha",
                        >= 135 and < 225 => "\u2193 Abajo",
                        _                => "\u2190 Izquierda"
                    };
                }
                lblTestAxes.Text = $"Eje X: {state.X,7} | Eje Y: {state.Y,7} | POV: {povText}";

                // Update visual panel
                float normX = (state.X - 32767f) / 32767f;
                float normY = (state.Y - 32767f) / 32767f;
                int povDeg = (pov != null && pov.Length > 0 && pov[0] != -1) ? pov[0] / 100 : -1;
                visualDInput.UpdateDInput(normX, normY, povDeg, state.Buttons);
            }
            catch (SharpDX.SharpDXException)
            {
                try { _testJoystick?.Acquire(); } catch { }
            }
        }

        private void StopDInputTest()
        {
            _testTimer?.Stop();
            _testTimer?.Dispose();
            _testTimer = null;
            try { _testJoystick?.Unacquire(); } catch { }
            _testJoystick?.Dispose();
            _testJoystick = null;
            _testDInput?.Dispose();
            _testDInput = null;
            visualDInput?.Reset();
        }

        private void BtnTestXInput_Click(object? sender, EventArgs e)
        {
            if (_xinputTestTimer != null)
            {
                StopXInputTest();
                btnTestXInput.Text    = "\u25b6 Iniciar";
                lblXInputStatus.Text  = "Activo: \u2014";
                lblXInputButtons.Text = "Botones: \u2014";
                lblXInputAxes.Text    = "LX: \u2014 | LY: \u2014 | RX: \u2014 | RY: \u2014 | LT: \u2014 | RT: \u2014";
                return;
            }

            // Auto-scan if list is empty
            if (lstXInputSlots.Items.Count == 0) ScanXInputSlots();

            // Determine slot from listbox selection
            Controller? controller = null;
            int activeSlotIdx = 0;
            int selIdx = lstXInputSlots.SelectedIndex >= 0 ? lstXInputSlots.SelectedIndex : 0;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                int idx = (selIdx + attempt) % 4;
                var c = new Controller((UserIndex)idx);
                if (c.IsConnected) { controller = c; activeSlotIdx = idx; break; }
            }

            if (controller == null)
            {
                MessageBox.Show("No se encontr\u00f3 ning\u00fan mando XInput conectado.",
                    "Sin dispositivo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var caps = controller.GetCapabilities(DeviceQueryType.Any);
            bool wireless = caps.Flags.HasFlag(CapabilityFlags.Wireless);
            string deviceDesc = $"{caps.SubType}{(wireless ? " [inal\u00e1mbrico]" : "")}";
            lblXInputStatus.Text = $"Activo: Slot {activeSlotIdx + 1} — {deviceDesc}";

            _xinputTestTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _xinputTestTimer.Tick += (_, _) => XInputTestTimer_Tick(controller);
            _xinputTestTimer.Start();
            btnTestXInput.Text = "\u25a0 Detener";
        }

        private void XInputTestTimer_Tick(Controller controller)
        {
            if (!controller.IsConnected)
            {
                lblXInputStatus.Text  = "Activo: desconectado";
                lblXInputButtons.Text = "Botones: \u2014";
                lblXInputAxes.Text    = "LX: \u2014 | LY: \u2014 | RX: \u2014 | RY: \u2014 | LT: \u2014 | RT: \u2014";
                visualXInput?.Reset();
                return;
            }

            var gp = controller.GetState().Gamepad;
            var pressed = Enum.GetValues<GamepadButtonFlags>()
                .Where(f => f != GamepadButtonFlags.None && (gp.Buttons & f) != 0)
                .Select(f => f.ToString())
                .ToArray();
            lblXInputButtons.Text = pressed.Length > 0
                ? $"Botones: {string.Join(" ", pressed)}"
                : "Botones: \u2014";

            lblXInputAxes.Text =
                $"LX: {gp.LeftThumbX,6} | LY: {gp.LeftThumbY,6} | RX: {gp.RightThumbX,6} | RY: {gp.RightThumbY,6} | LT: {gp.LeftTrigger,3} | RT: {gp.RightTrigger,3}";

            // Update visual panel
            float normX  = gp.LeftThumbX  / 32767f;
            float normY  = gp.LeftThumbY  / 32767f;
            float normRX = gp.RightThumbX / 32767f;
            float normRY = gp.RightThumbY / 32767f;
            visualXInput.UpdateXInput(normX, normY, normRX, normRY, gp.LeftTrigger, gp.RightTrigger, pressed);
        }

        private void StopXInputTest()
        {
            _xinputTestTimer?.Stop();
            _xinputTestTimer?.Dispose();
            _xinputTestTimer = null;
            visualXInput?.Reset();
        }

        // --- Interactive button binding ---

        private Label MakeSectionLabel(string text, Point location) => new Label
        {
            Text = text,
            Location = location,
            AutoSize = true,
            Font = new Font(Font.FontFamily, 8f, FontStyle.Bold),
            ForeColor = SystemColors.GrayText,
        };

        private static string FormatBindLabel(int btn, bool allowAxis) =>
            btn <= 0 && allowAxis ? "Eje / POV" : $"Botón {btn}";

        private void UpdateBindLabels()
        {
            lblBindSelect.Text = FormatBindLabel(_bindSelectBtn, false);
            lblBindBack.Text   = FormatBindLabel(_bindBackBtn,   false);
            lblBindLeft.Text   = FormatBindLabel(_bindLeftBtn,   true);
            lblBindRight.Text  = FormatBindLabel(_bindRightBtn,  true);
        }

        private void StartBind(int target)
        {
            // Stop any active device test so we can acquire exclusively
            if (_testTimer != null)
            {
                StopDInputTest();
                btnTestDInput.Text  = "▶ Iniciar";
                lblTestDevice.Text  = "Activo: —";
                lblTestButtons.Text = "Botones: —";
                lblTestAxes.Text    = "Eje X: — | Eje Y: — | POV: —";
            }
            CancelBind(); // clean up any previous binding session

            // Resolve which device to listen on
            DeviceInstance? di = null;
            int devIdx = cboDInputDevice.SelectedIndex - 1; // index 0 = "first available"
            if (devIdx >= 0 && devIdx < _dinputDeviceList.Count)
                di = _dinputDeviceList[devIdx];
            else if (_dinputDeviceList.Count > 0)
                di = _dinputDeviceList[0];

            if (di == null)
            {
                MessageBox.Show(
                    "No se encontró ningún dispositivo DirectInput.\nConecte el dispositivo y pulse ‘↻ Escanear’.",
                    "Sin dispositivo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                _bindDInput   = new DirectInput();
                _bindJoystick = new Joystick(_bindDInput, di.InstanceGuid);
                _bindJoystick.SetCooperativeLevel(Handle, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
                _bindJoystick.Acquire();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al conectar dispositivo:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                CancelBind();
                return;
            }

            _bindingTarget   = target;
            _bindLastButtons = Array.Empty<bool>();
            _bindCountdown   = 100; // 100 × 50 ms = 5 s

            // Highlight active button; disable the others to avoid confusion
            foreach (var (tgt, btn) in new[] {
                (0, btnBindSelect), (1, btnBindBack), (2, btnBindLeft), (3, btnBindRight) })
            {
                btn.Text    = tgt == target ? "● 5s…" : "⊕ Asignar";
                btn.Enabled = tgt == target; // click again to cancel
            }

            _bindTimer = new System.Windows.Forms.Timer { Interval = 50 };
            _bindTimer.Tick += BindTimer_Tick;
            _bindTimer.Start();
        }

        private void BindTimer_Tick(object? sender, EventArgs e)
        {
            _bindCountdown--;
            int secs = Math.Max(0, (_bindCountdown + 19) / 20);
            var activeBtn = _bindingTarget switch {
                0 => btnBindSelect, 1 => btnBindBack,
                2 => btnBindLeft,   3 => btnBindRight, _ => null };
            if (activeBtn != null) activeBtn.Text = secs > 0 ? $"● {secs}s…" : "● …";

            if (_bindJoystick == null) { CancelBind(); return; }
            try
            {
                _bindJoystick.Poll();
                var state   = _bindJoystick.GetCurrentState();
                var buttons = state.Buttons;

                if (_bindLastButtons.Length != buttons.Length)
                    _bindLastButtons = new bool[buttons.Length];

                // Rising-edge detection: first button pressed wins
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (buttons[i] && !_bindLastButtons[i])
                    {
                        AssignButton(_bindingTarget, i + 1); // 1-based
                        return;
                    }
                }

                // For Left/Right: also accept joystick axis or POV movement → record as 0
                if (_bindingTarget == 2 || _bindingTarget == 3)
                {
                    const int center   = 32767;
                    const int deadzone = 20000;
                    bool axisActive = Math.Abs(state.X - center) > deadzone;
                    var  pov        = state.PointOfViewControllers;
                    bool povActive  = pov != null && pov.Length > 0 && pov[0] != -1;
                    if (axisActive || povActive)
                    {
                        AssignButton(_bindingTarget, 0); // 0 = use axis/POV
                        return;
                    }
                }

                Array.Copy(buttons, _bindLastButtons, buttons.Length);
            }
            catch (SharpDX.SharpDXException)
            {
                try { _bindJoystick?.Acquire(); } catch { }
            }

            if (_bindCountdown <= 0) CancelBind();
        }

        private void AssignButton(int target, int buttonNum)
        {
            switch (target)
            {
                case 0: _bindSelectBtn = Math.Max(1, buttonNum); break;
                case 1: _bindBackBtn   = Math.Max(1, buttonNum); break;
                case 2: _bindLeftBtn   = buttonNum; break; // 0 = axis/POV is valid
                case 3: _bindRightBtn  = buttonNum; break;
            }
            UpdateBindLabels();
            FlashLabel(GetDiBindLabel(target));
            StopBind();
            AutoSave();
        }

        private void StopBind()
        {
            _bindTimer?.Stop();
            _bindTimer?.Dispose();
            _bindTimer      = null;
            _bindingTarget  = -1;
            try { _bindJoystick?.Unacquire(); } catch { }
            _bindJoystick?.Dispose();
            _bindJoystick = null;
            _bindDInput?.Dispose();
            _bindDInput = null;
            if (!IsDisposed)
            {
                bool di = chkDInputEnabled.Checked;
                btnBindSelect.Text = "⊕ Asignar"; btnBindSelect.Enabled = di;
                btnBindBack.Text   = "⊕ Asignar"; btnBindBack.Enabled   = di;
                btnBindLeft.Text   = "⊕ Asignar"; btnBindLeft.Enabled   = di;
                btnBindRight.Text  = "⊕ Asignar"; btnBindRight.Enabled  = di;
            }
        }

        private void CancelBind()
        {
            if (_bindTimer == null) return; // nothing active
            StopBind();
        }

        // ─── XInput interactive button binding ───────────────────────────────

        private static string FormatXiBindLabel(int flags, bool allowAxis)
        {
            if (flags == 0) return allowAxis ? "DPad / Palanca" : "—";
            return (GamepadButtonFlags)flags switch
            {
                GamepadButtonFlags.A             => "A",
                GamepadButtonFlags.B             => "B",
                GamepadButtonFlags.X             => "X",
                GamepadButtonFlags.Y             => "Y",
                GamepadButtonFlags.Start         => "Start",
                GamepadButtonFlags.Back          => "Back",
                GamepadButtonFlags.LeftShoulder  => "LB",
                GamepadButtonFlags.RightShoulder => "RB",
                GamepadButtonFlags.LeftThumb     => "LS",
                GamepadButtonFlags.RightThumb    => "RS",
                GamepadButtonFlags.DPadUp        => "↑ DPad",
                GamepadButtonFlags.DPadDown      => "↓ DPad",
                GamepadButtonFlags.DPadLeft      => "← DPad",
                GamepadButtonFlags.DPadRight     => "→ DPad",
                _                                => $"0x{flags:X4}"
            };
        }

        private void UpdateXiBindLabels()
        {
            lblXiBindSelect.Text = FormatXiBindLabel(_xiBindSelectBtn, false);
            lblXiBindBack.Text   = FormatXiBindLabel(_xiBindBackBtn,   false);
            lblXiBindLeft.Text   = FormatXiBindLabel(_xiBindLeftBtn,   true);
            lblXiBindRight.Text  = FormatXiBindLabel(_xiBindRightBtn,  true);
        }

        private Button GetXiBindButton(int target) => target switch
        {
            0 => btnXiBindSelect,
            1 => btnXiBindBack,
            2 => btnXiBindLeft,
            _ => btnXiBindRight
        };

        private Label GetDiBindLabel(int target) => target switch
        {
            0 => lblBindSelect,
            1 => lblBindBack,
            2 => lblBindLeft,
            _ => lblBindRight
        };

        private Label GetXiBindLabel(int target) => target switch
        {
            0 => lblXiBindSelect,
            1 => lblXiBindBack,
            2 => lblXiBindLeft,
            _ => lblXiBindRight
        };

        /// <summary>Briefly flashes a label green to confirm a button was assigned.</summary>
        private void FlashLabel(Label lbl)
        {
            lbl.BackColor = Color.FromArgb(50, 255, 100);
            lbl.ForeColor = Color.White;
            var t = new System.Windows.Forms.Timer { Interval = 700 };
            t.Tick += (_, _) =>
            {
                t.Stop();
                t.Dispose();
                if (!lbl.IsDisposed)
                {
                    lbl.BackColor = SystemColors.Control;
                    lbl.ForeColor = SystemColors.ControlText;
                }
            };
            t.Start();
        }

        private void StartXiBind(int target)
        {
            StopXiBind(); // cancel any previous assign session
            _xiBindingTarget = target;
            _xiBindCountdown = 50; // 50 × 100 ms = 5 s

            _xiBindSlot = lstXInputSlots.SelectedIndex >= 0 ? lstXInputSlots.SelectedIndex : 0;
            var ctrl = new Controller((UserIndex)_xiBindSlot);
            if (!ctrl.IsConnected)
            {
                MessageBox.Show($"El slot {_xiBindSlot + 1} no está conectado.", "XInput",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _xiBindingTarget = -1;
                return;
            }
            _xiBindLastButtons = ctrl.GetState().Gamepad.Buttons;

            GetXiBindButton(target).Text = "✕ (5s)";

            _xiBindTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _xiBindTimer.Tick += XiBindTimer_Tick;
            _xiBindTimer.Start();
        }

        private void XiBindTimer_Tick(object? sender, EventArgs e)
        {
            _xiBindCountdown--;
            int secsLeft = (_xiBindCountdown + 9) / 10; // ceiling: 50→5, 40→4…
            GetXiBindButton(_xiBindingTarget).Text = $"✕ ({secsLeft}s)";

            var ctrl = new Controller((UserIndex)_xiBindSlot);
            if (ctrl.IsConnected)
            {
                var currentButtons = ctrl.GetState().Gamepad.Buttons;
                var newlyPressed   = currentButtons & ~_xiBindLastButtons;
                _xiBindLastButtons = currentButtons;
                if (newlyPressed != GamepadButtonFlags.None)
                {
                    // isolate lowest set bit so we assign exactly one button
                    int lowestBit = (int)newlyPressed & -(int)newlyPressed;
                    AssignXiButton(_xiBindingTarget, lowestBit);
                    return;
                }
            }

            if (_xiBindCountdown <= 0) StopXiBind();
        }

        private void AssignXiButton(int target, int flagValue)
        {
            switch (target)
            {
                case 0: _xiBindSelectBtn = flagValue; break;
                case 1: _xiBindBackBtn   = flagValue; break;
                case 2: _xiBindLeftBtn   = flagValue; break;
                case 3: _xiBindRightBtn  = flagValue; break;
            }
            StopXiBind();
            UpdateXiBindLabels();
            FlashLabel(GetXiBindLabel(target));
            AutoSave();
        }

        private void StopXiBind()
        {
            if (_xiBindTimer != null)
            {
                _xiBindTimer.Stop();
                _xiBindTimer.Tick -= XiBindTimer_Tick;
                _xiBindTimer.Dispose();
                _xiBindTimer = null;
            }
            if (_xiBindingTarget >= 0)
            {
                GetXiBindButton(_xiBindingTarget).Text = "\u2295 Asignar";
                _xiBindingTarget = -1;
            }
        }

        private void CancelXiBind()
        {
            if (_xiBindingTarget >= 0) StopXiBind();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopVideoPreview();
            StopMusicPreview();
            try { _videoPreviewPlayer?.Dispose(); } catch { }
            try { _previewLibVlc?.Dispose(); } catch { }
            StopDInputTest();
            StopXInputTest();
            CancelBind();
            CancelXiBind();
            _logWatcher?.Dispose();
            _logRefreshTimer?.Stop();
            _logRefreshTimer?.Dispose();
            _deviceRescanTimer?.Stop();
            _deviceRescanTimer?.Dispose();
            base.OnFormClosed(e);
        }

        // ── WM_DEVICECHANGE auto-rescan ───────────────────────────────────────
        // Windows sends 0x0219 (WM_DEVICECHANGE) whenever a USB device is
        // plugged or unplugged.  We debounce with a 600 ms one-shot timer so
        // rapid re-connections don’t flood the scan code.
        private const int WM_DEVICECHANGE      = 0x0219;
        private const int DBT_DEVICEARRIVAL    = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_DEVICECHANGE)
            {
                int wParam = m.WParam.ToInt32();
                if (wParam == DBT_DEVICEARRIVAL || wParam == DBT_DEVICEREMOVECOMPLETE)
                    ScheduleDeviceRescan();
            }
        }

        private void ScheduleDeviceRescan()
        {
            // Restart the debounce timer — fires 600 ms after the last event
            if (_deviceRescanTimer == null)
            {
                _deviceRescanTimer = new System.Windows.Forms.Timer { Interval = 600 };
                _deviceRescanTimer.Tick += (_, _) =>
                {
                    _deviceRescanTimer.Stop();
                    ScanDInputDevices();
                    ScanXInputSlots();
                };
            }
            _deviceRescanTimer.Stop();
            _deviceRescanTimer.Start();
        }

        private void InitLogWatcher()
        {
            // Find debug.log next to the main app exe (in build output)
            var solutionDir = Path.GetDirectoryName(_configPath) ?? ".";
            var candidates = new[]
            {
                Path.Combine(solutionDir, "bin", "Release", "net10.0-windows", "debug.log"),
                Path.Combine(solutionDir, "bin", "Debug", "net10.0-windows", "debug.log"),
                Path.Combine(solutionDir, "debug.log"),
            };
            _logFilePath = candidates.FirstOrDefault(File.Exists) ?? candidates[0];

            LoadLogFull();

            // Watch for changes
            var dir = Path.GetDirectoryName(_logFilePath);
            if (dir != null && Directory.Exists(dir))
            {
                _logWatcher = new FileSystemWatcher(dir, "debug.log")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                // Coalesce rapid writes with a short timer
                _logRefreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
                _logRefreshTimer.Tick += (_, _) =>
                {
                    _logRefreshTimer.Stop();
                    AppendNewLogLines();
                };
                _logWatcher.Changed += (_, _) =>
                {
                    try { BeginInvoke(() => { _logRefreshTimer!.Stop(); _logRefreshTimer.Start(); }); }
                    catch { }
                };
            }
        }

        private void LoadLogFull()
        {
            _logLastLength = 0;
            if (!File.Exists(_logFilePath))
            {
                _logRawContent = "";
                RefreshLogDisplay();
                return;
            }
            try
            {
                using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                _logRawContent = sr.ReadToEnd();
                _logLastLength = fs.Length;
                RefreshLogDisplay();
            }
            catch (Exception ex)
            {
                _logRawContent = $"Error reading log: {ex.Message}";
                RefreshLogDisplay();
            }
        }

        private void AppendNewLogLines()
        {
            if (!File.Exists(_logFilePath)) return;
            try
            {
                using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length <= _logLastLength)
                {
                    if (fs.Length < _logLastLength) LoadLogFull();
                    return;
                }
                fs.Seek(_logLastLength, SeekOrigin.Begin);
                using var sr = new StreamReader(fs);
                var newText = sr.ReadToEnd();
                _logLastLength = fs.Length;
                if (!string.IsNullOrEmpty(newText))
                {
                    _logRawContent += newText;
                    RefreshLogDisplay();
                }
            }
            catch { }
        }

        private int MeasureLogLineHeight()
        {
            // Put two lines in, measure the Y difference to get the real rendered line height
            var oldText = txtLog.Text;
            txtLog.Text = "A\nB";
            int y0 = txtLog.GetPositionFromCharIndex(0).Y;
            int y1 = txtLog.GetPositionFromCharIndex(2).Y; // char index of 'B'
            txtLog.Text = oldText;
            int h = y1 - y0;
            return h > 0 ? h : 14;
        }

        private void RefreshLogDisplay()
        {
            if (txtLog == null || !txtLog.IsHandleCreated) return;
            int clientH = txtLog.ClientSize.Height;
            if (clientH <= 0) return; // tab not visible yet

            int lineHeight = MeasureLogLineHeight();
            int visibleLines = clientH / lineHeight;
            int contentLines = string.IsNullOrEmpty(_logRawContent) ? 0 : _logRawContent.Split('\n').Length;
            int padLines = Math.Max(0, visibleLines - contentLines + 1);
            var fullText = (padLines > 0 ? new string('\n', padLines) : "") + _logRawContent;

            txtLog.Text = fullText;
            HighlightCategories(fullText);
            ScrollLogToEnd();
        }

        private static readonly (string tag, Color bg)[] _logCategories =
        {
            ("[MUSIC]",    Color.FromArgb(180, 150, 0)),
            ("[DInput]",   Color.FromArgb(40, 80, 180)),
            ("[XInput]",   Color.FromArgb(0, 100, 200)),
            ("[VIDEO]",    Color.FromArgb(140, 90, 20)),
            ("[LAUNCH]",   Color.FromArgb(160, 60, 160)),
            ("[SPECTRUM]", Color.FromArgb(100, 100, 100)),
            ("[CONFIG]",   Color.FromArgb(180, 120, 0)),
        };

        private void HighlightCategories(string _)
        {
            txtLog.SuspendLayout();
            var rtbText = txtLog.Text; // uses \r\n — matches RichTextBox indices
            foreach (var (tag, bg) in _logCategories)
            {
                int idx = 0;
                while ((idx = rtbText.IndexOf(tag, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    txtLog.Select(idx, tag.Length);
                    txtLog.SelectionBackColor = bg;
                    txtLog.SelectionColor = Color.White;
                    idx += tag.Length;
                }
            }
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;
            txtLog.ResumeLayout();
        }

        private const int WM_VSCROLL = 0x0115;
        private const int SB_BOTTOM = 7;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private void ScrollLogToEnd()
        {
            SendMessage(txtLog.Handle, WM_VSCROLL, (IntPtr)SB_BOTTOM, IntPtr.Zero);
        }

        private void BtnLogClear_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show("¿Vaciar el archivo de log?", "Clear Log",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try
            {
                if (File.Exists(_logFilePath))
                    File.WriteAllText(_logFilePath, "");
                _logRawContent = "";
                _logLastLength = 0;
                RefreshLogDisplay();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error clearing log:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // --- Windows Shell thumbnail extraction (works for video, images, etc.) ---

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(NativeSize size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            public int cx;
            public int cy;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            string pszPath, IntPtr pbc,
            [In] ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private static Image? GetShellThumbnail(string path, int width, int height)
        {
            var iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            var size = new NativeSize { cx = width, cy = height };
            // Try thumbnail first (0x02), fall back to default (0x00)
            if (factory.GetImage(size, 0x02, out var hBitmap) != 0)
            {
                if (factory.GetImage(size, 0x00, out hBitmap) != 0)
                    return null;
            }
            try
            {
                return Image.FromHbitmap(hBitmap);
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
    }
}
