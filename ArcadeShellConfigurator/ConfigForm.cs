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
        private NumericUpDown nudVolume = null!;
        private TextBox txtAudioDevice = null!;

        // Options tab
        private DataGridView gridOptions = null!;

        // Bottom panel
        private Button btnSave = null!;
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
        }

        private void InitializeUI()
        {
            Text = "Arcade Shell Configurator";
            Size = new Size(820, 520);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

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
            var tabPaths = new TabPage("Paths");
            var lblToolsRoot = new Label { Text = "Tools Root:", Location = new Point(16, 20), AutoSize = true };
            txtToolsRoot = new TextBox { Location = new Point(140, 17), Width = 370 };
            var btnToolsRoot = new Button { Text = "...", Location = new Point(516, 16), Width = 30, Height = 23 };
            btnToolsRoot.Click += (_, _) => BrowseFolder(txtToolsRoot);

            var lblImagesRoot = new Label { Text = "Images Root:", Location = new Point(16, 55), AutoSize = true };
            txtImagesRoot = new TextBox { Location = new Point(140, 52), Width = 370 };
            var btnImagesRoot = new Button { Text = "...", Location = new Point(516, 51), Width = 30, Height = 23 };
            btnImagesRoot.Click += (_, _) => BrowseFolder(txtImagesRoot);

            var lblVideo = new Label { Text = "Video Background:", Location = new Point(16, 90), AutoSize = true };
            txtVideoBackground = new TextBox { Location = new Point(140, 87), Width = 370 };
            var btnVideo = new Button { Text = "...", Location = new Point(516, 86), Width = 30, Height = 23 };
            btnVideo.Click += (_, _) =>
            {
                BrowseAndDeployVideo();
                RefreshVideoThumb();
            };
            txtVideoBackground.TextChanged += (_, _) => RefreshVideoThumb();
            picVideoThumb = new PictureBox
            {
                Location = new Point(552, 82),
                Size = new Size(80, 48),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
            };

            tabPaths.Controls.AddRange(new Control[] {
                lblToolsRoot, txtToolsRoot, btnToolsRoot,
                lblImagesRoot, txtImagesRoot, btnImagesRoot,
                lblVideo, txtVideoBackground, btnVideo, picVideoThumb
            });

            // === Music tab ===
            var tabMusic = new TabPage("Music");
            chkMusicEnabled = new CheckBox { Text = "Music enabled", Location = new Point(16, 20), AutoSize = true };

            var lblMusicRoot = new Label { Text = "Music Folder:", Location = new Point(16, 55), AutoSize = true };
            txtMusicRoot = new TextBox { Location = new Point(140, 52), Width = 370 };
            var btnMusicRoot = new Button { Text = "...", Location = new Point(516, 51), Width = 30, Height = 23 };
            btnMusicRoot.Click += (_, _) => BrowseFolder(txtMusicRoot);

            var lblVolume = new Label { Text = "Volume:", Location = new Point(16, 90), AutoSize = true };
            nudVolume = new NumericUpDown { Location = new Point(140, 87), Width = 80, Minimum = 0, Maximum = 100 };

            var lblAudioDev = new Label { Text = "Audio Device:", Location = new Point(16, 125), AutoSize = true };
            txtAudioDevice = new TextBox { Location = new Point(140, 122), Width = 400 };

            tabMusic.Controls.AddRange(new Control[] {
                chkMusicEnabled, lblMusicRoot, txtMusicRoot, btnMusicRoot,
                lblVolume, nudVolume, lblAudioDev, txtAudioDevice
            });

            // === Options (Apps) tab ===
            var tabOptions = new TabPage("App Options");
            gridOptions = new DataGridView
            {
                Location = new Point(10, 10),
                Size = new Size(760, 300),
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

            tabs.TabPages.AddRange(new[] { tabGeneral, tabPaths, tabMusic, tabOptions });
            Controls.Add(tabs);

            // Bottom panel with Save/Cancel
            var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 50 };
            btnSave = new Button { Text = "Save", Width = 100, Height = 32, Location = new Point(600, 9), Enabled = false };
            btnSave.Click += BtnSave_Click;
            var btnCancel = new Button { Text = "Cancel", Width = 100, Height = 32, Location = new Point(490, 9) };
            btnCancel.Click += (_, _) => Close();
            var btnReload = new Button { Text = "Reload", Width = 100, Height = 32, Location = new Point(16, 9) };
            btnReload.Click += (_, _) => LoadConfig();
            var btnDefaults = new Button { Text = "Defaults", Width = 100, Height = 32, Location = new Point(126, 9) };
            btnDefaults.Click += BtnDefaults_Click;
            var btnLaunch = new Button { Text = "Launch App", Width = 110, Height = 32, Location = new Point(710, 9) };
            btnLaunch.Click += BtnLaunch_Click;
            bottomPanel.Controls.AddRange(new Control[] { btnSave, btnCancel, btnReload, btnDefaults, btnLaunch });
            Controls.Add(bottomPanel);

            // Wire up change tracking on all editable controls
            void MarkDirty(object? s, EventArgs a) { if (!_suppressDirty) btnSave.Enabled = true; }
            txtTitle.TextChanged += MarkDirty;
            chkTopMost.CheckedChanged += MarkDirty;
            chkLogging.CheckedChanged += MarkDirty;
            txtToolsRoot.TextChanged += MarkDirty;
            txtImagesRoot.TextChanged += MarkDirty;
            txtVideoBackground.TextChanged += MarkDirty;
            chkMusicEnabled.CheckedChanged += MarkDirty;
            txtMusicRoot.TextChanged += MarkDirty;
            nudVolume.ValueChanged += MarkDirty;
            txtAudioDevice.TextChanged += MarkDirty;
            gridOptions.CellValueChanged += (s, a) => { if (!_suppressDirty) btnSave.Enabled = true; };
            gridOptions.RowsAdded += (s, a) => { if (!_suppressDirty) btnSave.Enabled = true; };
            gridOptions.RowsRemoved += (s, a) => { if (!_suppressDirty) btnSave.Enabled = true; };
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
            nudVolume.Value = Math.Clamp(_config.Music.Volume, 0, 100);
            txtAudioDevice.Text = _config.Music.AudioDevice ?? "";

            // Options
            gridOptions.Rows.Clear();
            foreach (var opt in _config.Options)
            {
                var thumb = LoadThumbnail(opt.Image);
                var vidThumb = LoadVideoThumbnail(opt.ThumbVideo);
                gridOptions.Rows.Add(opt.Label, opt.Exe, "...", thumb, opt.Image, "...", vidThumb, opt.ThumbVideo ?? "", "...");
            }

            _suppressDirty = false;
            btnSave.Enabled = false;
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
            _config.Music.Volume = (int)nudVolume.Value;
            _config.Music.AudioDevice = string.IsNullOrWhiteSpace(txtAudioDevice.Text) ? null : txtAudioDevice.Text;

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

        private void BtnSave_Click(object? sender, EventArgs e)
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

                // Write to ALL discovered config.json locations so the main app
                // picks up changes immediately (it reads from its output folder)
                var paths = _allConfigPaths.Count > 0 ? _allConfigPaths : new List<string> { _configPath };
                foreach (var path in paths)
                    File.WriteAllText(path, json);

                MessageBox.Show($"Configuration saved to {paths.Count} location(s).", "Saved",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                btnSave.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving config:\n{ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnDefaults_Click(object? sender, EventArgs e)
        {
            if (MessageBox.Show("Reset all settings to defaults?\nUnsaved changes will be lost.",
                    "Restore Defaults", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _config = new AppConfig();
            PopulateUI();
            btnSave.Enabled = true;
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

        private static void BrowseFolder(TextBox target)
        {
            using var dlg = new FolderBrowserDialog { SelectedPath = target.Text };
            if (dlg.ShowDialog() == DialogResult.OK)
                target.Text = dlg.SelectedPath;
        }

        private static void BrowseFile(TextBox target, string filter)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = filter,
                FileName = target.Text
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                target.Text = dlg.FileName;
        }

        private void BrowseAndDeployVideo()
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Video files|*.mp4;*.avi;*.mkv;*.wmv|All files|*.*",
                FileName = txtVideoBackground.Text
            };
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
                using var dlg = new OpenFileDialog { Filter = "Executables|*.exe|All files|*.*" };
                if (dlg.ShowDialog() == DialogResult.OK)
                    row.Cells["Exe"].Value = dlg.FileName;
            }
            else if (colName == "BrowseImage")
            {
                using var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.bmp;*.gif|All files|*.*" };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    row.Cells["ImagePath"].Value = dlg.FileName;
                    row.Cells["ImageThumb"].Value = LoadThumbnail(dlg.FileName);
                }
            }
            else if (colName == "BrowseThumbVideo")
            {
                using var dlg = new OpenFileDialog { Filter = "Video files|*.mp4;*.avi;*.mkv;*.wmv|All files|*.*" };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    row.Cells["ThumbVideoPath"].Value = dlg.FileName;
                    row.Cells["VideoThumb"].Value = LoadVideoThumbnail(dlg.FileName);
                }
            }
        }

        private Image LoadThumbnail(string? imagePath)
        {
            var blank = new Bitmap(1, 1);
            if (string.IsNullOrWhiteSpace(imagePath)) return blank;

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
                if (!File.Exists(fullPath)) return blank;
                using var img = Image.FromFile(fullPath);
                return (Image)img.GetThumbnailImage(48, 48, () => false, IntPtr.Zero);
            }
            catch { return blank; }
        }

        private Image LoadVideoThumbnail(string? videoPath)
        {
            var blank = new Bitmap(1, 1);
            if (string.IsNullOrWhiteSpace(videoPath)) return blank;

            var fullPath = videoPath;
            if (!Path.IsPathRooted(fullPath))
            {
                var baseDir = Path.GetDirectoryName(_configPath) ?? ".";
                var candidate = Path.Combine(baseDir, fullPath);
                if (File.Exists(candidate)) fullPath = candidate;
            }

            try
            {
                if (!File.Exists(fullPath)) return blank;
                return GetShellThumbnail(fullPath, 80, 48) ?? blank;
            }
            catch { return blank; }
        }

        private void RefreshVideoThumb()
        {
            picVideoThumb.Image = LoadVideoThumbnail(txtVideoBackground.Text);
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
