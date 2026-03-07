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

        // Music tab
        private CheckBox chkMusicEnabled = null!;
        private TextBox txtMusicRoot = null!;
        private TrackBar trkVolume = null!;
        private Label lblVolumeValue = null!;
        private TextBox txtAudioDevice = null!;

        // Options tab
        private DataGridView gridOptions = null!;

        // Input tab
        private CheckBox chkDInputEnabled = null!;
        private NumericUpDown numDInputButtonSelect = null!;
        private NumericUpDown numDInputButtonBack = null!;
        private NumericUpDown numDInputButtonLeft = null!;
        private NumericUpDown numDInputButtonRight = null!;

        // Input test panel — DirectInput
        private ListBox lstDInputDevices = null!;
        private Button btnTestDInput = null!;
        private Label lblTestDevice = null!;
        private Label lblTestButtons = null!;
        private Label lblTestAxes = null!;
        private DirectInput? _testDInput;
        private Joystick? _testJoystick;
        private System.Windows.Forms.Timer? _testTimer;
        private readonly List<DeviceInstance> _dinputDeviceList = new();

        // Input test panel — XInput
        private ListBox lstXInputSlots = null!;
        private Button btnTestXInput = null!;
        private Label lblXInputStatus = null!;
        private Label lblXInputButtons = null!;
        private Label lblXInputAxes = null!;
        private System.Windows.Forms.Timer? _xinputTestTimer;

        // Bottom panel
        private bool _suppressDirty;

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

            InitializeUI();
            LoadConfig();
            Shown += (_, _) => { ScanDInputDevices(); ScanXInputSlots(); };
        }

        private void InitializeUI()
        {
            Text = "Arcade Shell Configurator";
            Size = new Size(820, 750);
            MinimumSize = new Size(820, 600);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;

            // Set window icon from the embedded app.ico resource
            var icoPath = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? ".", "app.ico");
            if (File.Exists(icoPath))
                Icon = new Icon(icoPath);

            var tabs = new TabControl { Dock = DockStyle.Fill };

            // === General tab ===
            var tabGeneral = new TabPage("General");
            var lblTitle = new Label { Text = "App Title:", Location = new Point(16, 20), AutoSize = true };
            txtTitle = new TextBox { Location = new Point(140, 17), Width = 400 };
            chkTopMost = new CheckBox { Text = "Always on top (TopMost)", Location = new Point(140, 50), AutoSize = true };
            chkLogging = new CheckBox { Text = "Enable logging (Depuracion)", Location = new Point(140, 80), AutoSize = true };
            tabGeneral.Controls.AddRange(new Control[] { lblTitle, txtTitle, chkTopMost, chkLogging });

            // === Paths tab ===
            var tabPaths = new TabPage("Configuración Launcher");
            var lblToolsRoot = new Label { Text = "Tools Root:", Location = new Point(16, 20), AutoSize = true };
            txtToolsRoot = new TextBox { Location = new Point(140, 17), Width = 370 };
            var btnToolsRoot = new Button { Text = "...", Location = new Point(516, 16), Width = 30, Height = 23 };
            btnToolsRoot.Click += (_, _) => BrowseDrive(txtToolsRoot);
            var lblToolsHint = new Label
            {
                Text = "Drive or root folder where child apps live (e.g. D:\\)",
                Location = new Point(140, 40),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font, FontStyle.Italic),
            };

            var lblImagesRoot = new Label { Text = "Images Root:", Location = new Point(16, 65), AutoSize = true };
            txtImagesRoot = new TextBox { Location = new Point(140, 62), Width = 370 };
            var btnImagesRoot = new Button { Text = "...", Location = new Point(516, 61), Width = 30, Height = 23 };
            btnImagesRoot.Click += (_, _) => BrowseFolderUnder(txtImagesRoot, txtToolsRoot.Text, _configPath);
            var lblImagesHint = new Label
            {
                Text = "Relative path inside Tools Root where artwork is stored",
                Location = new Point(140, 85),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font, FontStyle.Italic),
            };

            var lblVideo = new Label { Text = "Video Background:", Location = new Point(16, 115), AutoSize = true };
            txtVideoBackground = new TextBox { Location = new Point(140, 112), Width = 370 };
            var btnVideo = new Button { Text = "...", Location = new Point(516, 111), Width = 30, Height = 23 };
            btnVideo.Click += (_, _) =>
            {
                BrowseAndDeployVideo();
                RefreshVideoThumb();
            };
            txtVideoBackground.TextChanged += (_, _) => RefreshVideoThumb();
            var lblVideoHint = new Label
            {
                Text = "Video file played as background (copied to Bkg folder automatically)",
                Location = new Point(140, 135),
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font, FontStyle.Italic),
            };
            picVideoThumb = new PictureBox
            {
                Location = new Point(552, 107),
                Size = new Size(80, 48),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
            };

            tabPaths.Controls.AddRange(new Control[] {
                lblToolsRoot, txtToolsRoot, btnToolsRoot, lblToolsHint,
                lblImagesRoot, txtImagesRoot, btnImagesRoot, lblImagesHint,
                lblVideo, txtVideoBackground, btnVideo, lblVideoHint, picVideoThumb
            });

            // === Music tab ===
            var tabMusic = new TabPage("Musica");
            chkMusicEnabled = new CheckBox { Text = "Music enabled", Location = new Point(16, 20), AutoSize = true };

            var lblMusicRoot = new Label { Text = "Music Folder:", Location = new Point(16, 55), AutoSize = true };
            txtMusicRoot = new TextBox { Location = new Point(140, 52), Width = 370 };
            var btnMusicRoot = new Button { Text = "...", Location = new Point(516, 51), Width = 30, Height = 23 };
            btnMusicRoot.Click += (_, _) => BrowseFolder(txtMusicRoot, _configPath);

            var lblVolume = new Label { Text = "Volume:", Location = new Point(16, 93), AutoSize = true };
            trkVolume = new TrackBar
            {
                Location = new Point(140, 82),
                Width = 300,
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                LargeChange = 10,
                SmallChange = 1,
            };
            lblVolumeValue = new Label { Text = "0", Location = new Point(448, 93), AutoSize = true };
            trkVolume.ValueChanged += (_, _) => lblVolumeValue.Text = trkVolume.Value.ToString();

            var lblAudioDev = new Label { Text = "Audio Device:", Location = new Point(16, 140), AutoSize = true };
            txtAudioDevice = new TextBox { Location = new Point(140, 137), Width = 400 };

            tabMusic.Controls.AddRange(new Control[] {
                chkMusicEnabled, lblMusicRoot, txtMusicRoot, btnMusicRoot,
                lblVolume, trkVolume, lblVolumeValue, lblAudioDev, txtAudioDevice
            });

            // === Options (Apps) tab ===
            var tabOptions = new TabPage("Opciones Lanzador");
            gridOptions = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                EditMode = DataGridViewEditMode.EditOnEnter,
            };
            gridOptions.Columns.Add("Label", "Label");
            gridOptions.Columns.Add("Exe", "Front End (Exe)");
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
            gridOptions.RowTemplate.Height = 48;
            gridOptions.CellContentClick += GridOptions_CellContentClick;

            tabOptions.Controls.AddRange(new Control[] { gridOptions });

            // === Input tab ===
            var tabInput = new TabPage("Control");
            chkDInputEnabled = new CheckBox { Text = "DirectInput habilitado", Location = new Point(16, 20), AutoSize = true };

            var lblSelect = new Label { Text = "Botón Seleccionar (base 1):", Location = new Point(16, 58), AutoSize = true };
            numDInputButtonSelect = new NumericUpDown { Location = new Point(220, 55), Width = 70, Minimum = 1, Maximum = 32, Value = 1 };

            var lblBack = new Label { Text = "Botón Atrás / Cerrar (base 1):", Location = new Point(16, 91), AutoSize = true };
            numDInputButtonBack = new NumericUpDown { Location = new Point(220, 88), Width = 70, Minimum = 1, Maximum = 32, Value = 2 };

            var lblLeft = new Label { Text = "Botón Izquierda (0 = eje/POV):", Location = new Point(16, 124), AutoSize = true };
            numDInputButtonLeft = new NumericUpDown { Location = new Point(220, 121), Width = 70, Minimum = 0, Maximum = 32, Value = 0 };

            var lblRight = new Label { Text = "Botón Derecha (0 = eje/POV):", Location = new Point(16, 157), AutoSize = true };
            numDInputButtonRight = new NumericUpDown { Location = new Point(220, 154), Width = 70, Minimum = 0, Maximum = 32, Value = 0 };

            var lblInputHint = new Label
            {
                Text = "0 = navegar con eje analógico / hat POV del joystick.",
                Location = new Point(16, 193),
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };

            // Test panel — two columns: DirectInput (left) | XInput (right)
            var grpTest = new GroupBox
            {
                Text = "Probar dispositivo",
                Location = new Point(10, 215),
                Size = new Size(745, 250),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };

            // --- DirectInput column ---
            var grpDI = new GroupBox
            {
                Text = "DirectInput",
                Location = new Point(8, 18),
                Size = new Size(360, 220),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom
            };
            lstDInputDevices = new ListBox
            {
                Location = new Point(8, 18),
                Size = new Size(340, 68),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                SelectionMode = SelectionMode.One,
                HorizontalScrollbar = true
            };
            btnTestDInput  = new Button { Text = "\u25b6 Iniciar", Location = new Point(8, 92), Width = 110, Height = 24 };
            btnTestDInput.Click += BtnTestDInput_Click;
            lblTestDevice  = new Label { Text = "Activo: \u2014", Location = new Point(8, 124), AutoSize = true };
            lblTestButtons = new Label
            {
                Text = "Botones: \u2014",
                Location = new Point(8, 148),
                AutoSize = true,
                Font = new Font("Courier New", 9f)
            };
            lblTestAxes = new Label
            {
                Text = "Eje X: \u2014 | Eje Y: \u2014 | POV: \u2014",
                Location = new Point(8, 178),
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };
            grpDI.Controls.AddRange(new Control[] { lstDInputDevices, btnTestDInput, lblTestDevice, lblTestButtons, lblTestAxes });

            // --- XInput column ---
            var grpXI = new GroupBox
            {
                Text = "XInput (Xbox / compatible)",
                Location = new Point(378, 18),
                Size = new Size(360, 220),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            lstXInputSlots = new ListBox
            {
                Location = new Point(8, 18),
                Size = new Size(340, 68),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                SelectionMode = SelectionMode.One
            };
            btnTestXInput  = new Button { Text = "\u25b6 Iniciar", Location = new Point(8, 92), Width = 110, Height = 24 };
            btnTestXInput.Click += BtnTestXInput_Click;
            lblXInputStatus  = new Label { Text = "Activo: \u2014", Location = new Point(8, 124), AutoSize = true };
            lblXInputButtons = new Label
            {
                Text = "Botones: \u2014",
                Location = new Point(8, 148),
                AutoSize = true,
                Font = new Font("Courier New", 9f)
            };
            lblXInputAxes = new Label
            {
                Text = "LX: \u2014 | LY: \u2014 | LT: \u2014 | RT: \u2014",
                Location = new Point(8, 178),
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            };
            grpXI.Controls.AddRange(new Control[] { lstXInputSlots, btnTestXInput, lblXInputStatus, lblXInputButtons, lblXInputAxes });

            grpTest.Controls.AddRange(new Control[] { grpDI, grpXI });

            tabInput.Controls.AddRange(new Control[]
            {
                chkDInputEnabled,
                lblSelect, numDInputButtonSelect,
                lblBack,   numDInputButtonBack,
                lblLeft,   numDInputButtonLeft,
                lblRight,  numDInputButtonRight,
                lblInputHint,
                grpTest
            });

            tabs.TabPages.AddRange(new[] { tabGeneral, tabPaths, tabMusic, tabInput, tabOptions });
            Controls.Add(tabs);

            // Bottom panel
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 50 };
            var btnDefaults = new Button { Text = "Valores por defecto", Width = 150, Height = 32, Location = new Point(16, 9) };
            btnDefaults.Click += BtnDefaults_Click;
            var btnClose = new Button { Text = "Close", Width = 100, Height = 32, Location = new Point(176, 9) };
            btnClose.Click += (_, _) => Close();
            var btnLaunch = new Button { Text = "Launch App", Width = 110, Height = 32, Location = new Point(286, 9) };
            btnLaunch.Click += BtnLaunch_Click;
            bottomPanel.Controls.AddRange(new Control[] { btnDefaults, btnClose, btnLaunch });
            Controls.Add(bottomPanel);

            // Wire up auto-save on every change
            void OnChanged(object? s, EventArgs a) { if (!_suppressDirty) AutoSave(); }
            txtTitle.TextChanged += OnChanged;
            chkTopMost.CheckedChanged += OnChanged;
            chkLogging.CheckedChanged += OnChanged;
            txtToolsRoot.TextChanged += OnChanged;
            txtImagesRoot.TextChanged += OnChanged;
            txtVideoBackground.TextChanged += OnChanged;
            chkMusicEnabled.CheckedChanged += OnChanged;
            txtMusicRoot.TextChanged += OnChanged;
            trkVolume.ValueChanged += OnChanged;
            txtAudioDevice.TextChanged += OnChanged;
            chkDInputEnabled.CheckedChanged += OnChanged;
            numDInputButtonSelect.ValueChanged += OnChanged;
            numDInputButtonBack.ValueChanged += OnChanged;
            numDInputButtonLeft.ValueChanged += OnChanged;
            numDInputButtonRight.ValueChanged += OnChanged;
            gridOptions.CellValueChanged += (s, a) => { if (!_suppressDirty) AutoSave(); };
            gridOptions.RowsAdded += (s, a) => { if (!_suppressDirty) AutoSave(); };
            gridOptions.RowsRemoved += (s, a) => { if (!_suppressDirty) AutoSave(); };
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
            lblVolumeValue.Text = trkVolume.Value.ToString();
            txtAudioDevice.Text = _config.Music.AudioDevice ?? "";

            // Input
            chkDInputEnabled.Checked = _config.Input.DInputEnabled;
            numDInputButtonSelect.Value = Math.Clamp(_config.Input.DInputButtonSelect, 1, 32);
            numDInputButtonBack.Value   = Math.Clamp(_config.Input.DInputButtonBack,   1, 32);
            numDInputButtonLeft.Value   = Math.Clamp(_config.Input.DInputButtonLeft,   0, 32);
            numDInputButtonRight.Value  = Math.Clamp(_config.Input.DInputButtonRight,  0, 32);

            // Options
            gridOptions.Rows.Clear();
            foreach (var opt in _config.Options)
            {
                var thumb = LoadThumbnail(opt.Image);
                var vidThumb = LoadVideoThumbnail(opt.ThumbVideo);
                gridOptions.Rows.Add(opt.Label, opt.Exe, "...", thumb, opt.Image, "...", vidThumb, opt.ThumbVideo ?? "", "...");
            }

            _suppressDirty = false;
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
            _config.Music.AudioDevice = string.IsNullOrWhiteSpace(txtAudioDevice.Text) ? null : txtAudioDevice.Text;

            _config.Input.DInputEnabled      = chkDInputEnabled.Checked;
            _config.Input.DInputButtonSelect = (int)numDInputButtonSelect.Value;
            _config.Input.DInputButtonBack   = (int)numDInputButtonBack.Value;
            _config.Input.DInputButtonLeft   = (int)numDInputButtonLeft.Value;
            _config.Input.DInputButtonRight  = (int)numDInputButtonRight.Value;

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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving config:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            var bkgDir = Path.Combine(solutionDir, "Bkg");
            try { Directory.CreateDirectory(bkgDir); } catch { }

            // Also deploy to the main app's output Bkg folder
            var binBkgDir = Path.Combine(solutionDir, "bin", "Release", "net10.0-windows", "Bkg");
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
            picVideoThumb.Image = LoadVideoThumbnail(txtVideoBackground.Text);
        }

        private void ScanDInputDevices()
        {
            _dinputDeviceList.Clear();
            lstDInputDevices.Items.Clear();
            lstDInputDevices.Items.Add("Escaneando\u2026");

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
                    lstDInputDevices.Items.Clear();
                    _dinputDeviceList.Clear();
                    if (error != null)
                    {
                        lstDInputDevices.Items.Add($"Error: {error}");
                    }
                    else if (found.Count == 0)
                    {
                        lstDInputDevices.Items.Add("(ningún dispositivo encontrado)");
                    }
                    else
                    {
                        foreach (var d in found)
                        {
                            _dinputDeviceList.Add(d);
                            var (vid, pid) = ExtractVidPid(d.ProductGuid);
                            lstDInputDevices.Items.Add($"[VID:{vid:X4} PID:{pid:X4}]  {d.ProductName}");
                        }
                        lstDInputDevices.SelectedIndex = 0;
                    }
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
                    if (firstConnected >= 0) lstXInputSlots.SelectedIndex = firstConnected;
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

            int sel = lstDInputDevices.SelectedIndex;
            var deviceInfo = _dinputDeviceList[sel >= 0 && sel < _dinputDeviceList.Count ? sel : 0];
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
        }

        private void BtnTestXInput_Click(object? sender, EventArgs e)
        {
            if (_xinputTestTimer != null)
            {
                StopXInputTest();
                btnTestXInput.Text    = "\u25b6 Iniciar";
                lblXInputStatus.Text  = "Activo: \u2014";
                lblXInputButtons.Text = "Botones: \u2014";
                lblXInputAxes.Text    = "LX: \u2014 | LY: \u2014 | LT: \u2014 | RT: \u2014";
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
                lblXInputAxes.Text    = "LX: \u2014 | LY: \u2014 | LT: \u2014 | RT: \u2014";
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
                $"LX: {gp.LeftThumbX,6} | LY: {gp.LeftThumbY,6} | LT: {gp.LeftTrigger,3} | RT: {gp.RightTrigger,3}";
        }

        private void StopXInputTest()
        {
            _xinputTestTimer?.Stop();
            _xinputTestTimer?.Dispose();
            _xinputTestTimer = null;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopDInputTest();
            StopXInputTest();
            base.OnFormClosed(e);
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
