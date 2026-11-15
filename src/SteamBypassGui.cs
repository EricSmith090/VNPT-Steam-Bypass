using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Steam Bypass")]
[assembly: AssemblyProduct("Steam Bypass")]
[assembly: AssemblyDescription("VNPT split-DNS or Campus/HCMUS relay, selectable in one app.")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace VnptSteamBypass
{
    internal static class Program
    {
        internal const string Marker = "VNPT-Steam-Bypass";
        internal const string HostsBeginMarker = "# BEGIN VNPT-Steam-Bypass (managed)";
        internal const string HostsEndMarker = "# END VNPT-Steam-Bypass (managed)";

        internal static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "VNPT-Steam-Bypass");

        internal static readonly string ScriptDir = Path.Combine(DataDir, "scripts");
        internal static readonly string LogDir = Path.Combine(DataDir, "logs");
        internal static readonly string HostsPath = Path.Combine(Environment.SystemDirectory, @"drivers\etc\hosts");

        private static readonly string[] ScriptFiles = new string[]
        {
            "VnptBypass.Common.ps1",
            "Enable-VnptBypass.ps1",
            "Disable-VnptBypass.ps1",
            "Test-VnptBypass.ps1"
        };

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "--relay", StringComparison.OrdinalIgnoreCase))
            {
                string[] relayArgs = new string[Math.Max(0, args.Length - 1)];
                if (relayArgs.Length > 0)
                {
                    Array.Copy(args, 1, relayArgs, 0, relayArgs.Length);
                }
                Environment.ExitCode = SteamSnIRelayProgram.Run(relayArgs);
                return;
            }

            string screenshotPath = null;
            bool elevated = false;
            bool autoApply = false;
            string initialMode = null;

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i] == null ? string.Empty : args[i].ToLowerInvariant();
                    if (arg == "--screenshot" && i + 1 < args.Length)
                    {
                        screenshotPath = args[++i];
                    }
                    else if (arg == "--elevated")
                    {
                        elevated = true;
                    }
                    else if (arg == "--auto")
                    {
                        autoApply = true;
                    }
                    else if (arg == "--mode" && i + 1 < args.Length)
                    {
                        initialMode = args[++i];
                    }
                }
            }

            string runtimeError = null;
            try
            {
                EnsureRuntime();
            }
            catch (Exception ex)
            {
                runtimeError = ex.Message;
            }

            bool createdNew = false;
            string mutexName = elevated
                ? @"Local\SteamBypass-GUI-Elevated"
                : @"Local\SteamBypass-GUI";

            using (Mutex mutex = new Mutex(true, mutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "The Steam Bypass window is already open.",
                        "Steam Bypass",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                if (screenshotPath != null)
                {
                    CaptureScreenshot(screenshotPath, initialMode);
                    return;
                }

                Application.Run(new MainForm(initialMode, autoApply, runtimeError));
            }
        }

        internal static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        internal static void EnsureRuntime()
        {
            Directory.CreateDirectory(DataDir);

            if (IsAdministrator())
            {
                HardenDataDirectory();
            }

            Directory.CreateDirectory(ScriptDir);
            Directory.CreateDirectory(LogDir);

            Assembly assembly = Assembly.GetExecutingAssembly();
            foreach (string file in ScriptFiles)
            {
                string resourceName = "Scripts." + file;
                string destination = Path.Combine(ScriptDir, file);

                // A standard-user launch can still use the GUI and Campus mode.
                // VNPT mode is elevated, and that launch refreshes the scripts.
                if (!IsAdministrator() && File.Exists(destination))
                {
                    continue;
                }

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException("Embedded resource not found: " + resourceName);
                    }

                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        string content = reader.ReadToEnd();
                        File.WriteAllText(destination, content, new UTF8Encoding(false));
                    }
                }
            }
        }

        private static void HardenDataDirectory()
        {
            // Scripts are re-extracted on every launch, but keep the folder
            // writable only by administrators so a non-elevated process cannot
            // tamper with what the elevated app is about to run.
            try
            {
                DirectoryInfo info = new DirectoryInfo(DataDir);
                DirectorySecurity security = new DirectorySecurity();
                security.SetAccessRuleProtection(true, false);

                SecurityIdentifier system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                SecurityIdentifier administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                SecurityIdentifier users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

                InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
                security.AddAccessRule(new FileSystemAccessRule(
                    system, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(
                    administrators, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
                security.AddAccessRule(new FileSystemAccessRule(
                    users, FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory,
                    inherit, PropagationFlags.None, AccessControlType.Allow));

                try
                {
                    security.SetOwner(administrators);
                }
                catch
                {
                    // Ownership change is best effort.
                }

                info.SetAccessControl(security);
            }
            catch
            {
                // ACL hardening is best effort; the scripts are still rewritten
                // immediately before each run.
            }
        }

        internal static int CountNrptRules()
        {
            const string keyPath = @"SYSTEM\CurrentControlSet\Services\Dnscache\Parameters\DnsPolicyConfig";
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (root == null)
                    {
                        return 0;
                    }

                    int count = 0;
                    foreach (string subKeyName in root.GetSubKeyNames())
                    {
                        using (RegistryKey rule = root.OpenSubKey(subKeyName))
                        {
                            if (rule == null)
                            {
                                continue;
                            }

                            object commentValue = rule.GetValue("Comment");
                            object displayValue = rule.GetValue("DisplayName");

                            bool match = commentValue != null &&
                                string.Equals(Convert.ToString(commentValue), Marker, StringComparison.OrdinalIgnoreCase);

                            if (!match && displayValue != null)
                            {
                                string display = Convert.ToString(displayValue);
                                match = display != null &&
                                    display.StartsWith("Steam bypass:", StringComparison.OrdinalIgnoreCase);
                            }

                            if (match)
                            {
                                count++;
                            }
                        }
                    }

                    return count;
                }
            }
            catch
            {
                return -1;
            }
        }

        internal static bool TryReadHosts(out bool hasBlock, out int pinCount)
        {
            hasBlock = false;
            pinCount = 0;

            try
            {
                string content = File.ReadAllText(HostsPath);
                hasBlock = content.IndexOf(HostsBeginMarker, StringComparison.OrdinalIgnoreCase) >= 0;

                bool insideBlock = false;
                string[] lines = content.Replace("\r\n", "\n").Split('\n');
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();

                    if (line.StartsWith(HostsBeginMarker, StringComparison.OrdinalIgnoreCase))
                    {
                        insideBlock = true;
                        continue;
                    }

                    if (line.StartsWith(HostsEndMarker, StringComparison.OrdinalIgnoreCase))
                    {
                        insideBlock = false;
                        continue;
                    }

                    if (!insideBlock || line.Length == 0 || line.StartsWith("#"))
                    {
                        continue;
                    }

                    string[] parts = line.Split(
                        new char[] { ' ', '\t' },
                        StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length >= 2)
                    {
                        pinCount++;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint flags);

        private static void CaptureScreenshot(string path, string initialMode)
        {
            using (MainForm form = new MainForm(initialMode, false, null))
            {
                form.Show();
                form.Activate();
                for (int i = 0; i < 12; i++)
                {
                    Application.DoEvents();
                    Thread.Sleep(80);
                }

                Rectangle bounds = form.Bounds;
                using (Bitmap bitmap = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height)))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        IntPtr hdc = graphics.GetHdc();
                        try
                        {
                            PrintWindow(form.Handle, hdc, 2);
                        }
                        finally
                        {
                            graphics.ReleaseHdc(hdc);
                        }
                    }

                    using (MemoryStream stream = new MemoryStream())
                    {
                        bitmap.Save(stream, ImageFormat.Png);
                        File.WriteAllBytes(path, stream.ToArray());
                    }
                }

                form.Close();
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private enum BypassState
        {
            Off,
            On,
            Partial
        }

        private enum BypassMode
        {
            Vnpt,
            Campus
        }

        private static readonly Color WindowBack = Color.FromArgb(15, 23, 42);
        private static readonly Color PanelBack = Color.FromArgb(30, 41, 59);
        private static readonly Color PanelBorder = Color.FromArgb(51, 65, 85);
        private static readonly Color TextMain = Color.FromArgb(226, 232, 240);
        private static readonly Color TextMuted = Color.FromArgb(148, 163, 184);
        private static readonly Color Green = Color.FromArgb(34, 197, 94);
        private static readonly Color BrightGreen = Color.FromArgb(22, 163, 74);
        private static readonly Color Red = Color.FromArgb(239, 68, 68);
        private static readonly Color BrightRed = Color.FromArgb(220, 38, 38);
        private static readonly Color Amber = Color.FromArgb(245, 158, 11);
        private static readonly Color BrightAmber = Color.FromArgb(217, 119, 6);
        private static readonly Color Blue = Color.FromArgb(59, 130, 246);

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attribute,
            ref int value,
            int size);

        private readonly bool _isAdministrator;
        private readonly string _initialMode;
        private readonly string _runtimeError;
        private readonly bool _hasInitialMode;
        private bool _autoApply;
        private bool _modeInitialized;
        private BypassState _state = BypassState.Off;
        private BypassMode _mode = BypassMode.Vnpt;
        private BypassMode? _activeMode;
        private bool _busy;

        private CampusStatus _Campus = new CampusStatus();

        private RoundedPanel _statusCard;
        private Label _statusLabel;
        private Label _detailLabel;
        private Label _adminLabel;
        private Label _lastTestLabel;
        private RoundedButton _actionButton;
        private RoundedButton _testButton;
        private RoundedButton _refreshButton;
        private RoundedButton _folderButton;
        private RoundedButton _VnptModeButton;
        private RoundedButton _CampusModeButton;
        private RichTextBox _logBox;
        private LinkLabel _clearLink;
        private StringBuilder _runOutput;

        public MainForm()
            : this(null, false, null)
        {
        }

        public MainForm(string initialMode, bool autoApply, string runtimeError)
        {
            _isAdministrator = Program.IsAdministrator();
            _initialMode = initialMode;
            _hasInitialMode = !string.IsNullOrEmpty(initialMode);
            _autoApply = autoApply;
            _runtimeError = runtimeError;
            _mode = _hasInitialMode ? ParseMode(initialMode) : LoadSavedMode();
            InitializeWindow();
            BuildLayout();
            Shown += MainForm_Shown;
            FormClosing += MainForm_FormClosing;
        }

        private void InitializeWindow()
        {
            Text = "Steam Bypass";
            ClientSize = new Size(880, 700);
            MinimumSize = new Size(760, 630);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = WindowBack;
            ForeColor = TextMain;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            DoubleBuffered = true;

            try
            {
                this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                // A missing icon is harmless.
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            try
            {
                int enabled = 1;
                int result = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
                if (result != 0)
                {
                    DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
                }
            }
            catch
            {
                // Dark title bars are not available on older Windows versions.
            }
        }

        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 3;
            root.Margin = new Padding(0);
            root.Padding = new Padding(0);
            root.BackColor = WindowBack;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildContent(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);

            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            HeaderPanel header = new HeaderPanel();
            header.Dock = DockStyle.Fill;
            header.Margin = new Padding(0);

            PictureBox iconBox = new PictureBox();
            iconBox.Location = new Point(24, 23);
            iconBox.Size = new Size(44, 44);
            iconBox.SizeMode = PictureBoxSizeMode.Zoom;
            iconBox.BackColor = Color.Transparent;
            if (Icon != null)
            {
                iconBox.Image = Icon.ToBitmap();
            }

            Label title = new Label();
            title.AutoSize = true;
            title.Location = new Point(84, 17);
            title.BackColor = Color.Transparent;
            title.ForeColor = TextMain;
            title.Font = new Font("Segoe UI", 17F, FontStyle.Bold);
            title.Text = "Steam Bypass";

            Label subtitle = new Label();
            subtitle.AutoSize = true;
            subtitle.Location = new Point(86, 52);
            subtitle.BackColor = Color.Transparent;
            subtitle.ForeColor = TextMuted;
            subtitle.Font = new Font("Segoe UI", 9.25F, FontStyle.Regular);
            subtitle.Text = "Choose VNPT split-DNS or Campus/HCMUS local relay";

            header.Controls.Add(iconBox);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            return header;
        }

        private Control BuildContent()
        {
            TableLayoutPanel content = new TableLayoutPanel();
            content.Dock = DockStyle.Fill;
            content.ColumnCount = 1;
            content.RowCount = 5;
            content.Margin = new Padding(0);
            content.Padding = new Padding(24, 12, 24, 12);
            content.BackColor = WindowBack;
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 138F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            content.Controls.Add(BuildModeRow(), 0, 0);
            content.Controls.Add(BuildStatusCard(), 0, 1);
            content.Controls.Add(BuildButtonRow(), 0, 2);
            content.Controls.Add(BuildActivityRow(), 0, 3);
            content.Controls.Add(BuildLogCard(), 0, 4);
            return content;
        }

        private Control BuildModeRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.Margin = new Padding(0);
            row.Padding = new Padding(0, 6, 0, 2);
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = false;
            row.BackColor = WindowBack;

            Label label = new Label();
            label.Width = 112;
            label.Height = 34;
            label.Margin = new Padding(0);
            label.BackColor = Color.Transparent;
            label.ForeColor = TextMuted;
            label.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Text = "MODE";

            _VnptModeButton = CreateButton("VNPT (split DNS)", 162);
            _VnptModeButton.Click += ModeButton_Click;

            _CampusModeButton = CreateButton("Campus (HCMUS relay)", 190);
            _CampusModeButton.Click += ModeButton_Click;

            Label hint = new Label();
            hint.AutoSize = true;
            hint.Height = 34;
            hint.Margin = new Padding(14, 0, 0, 0);
            hint.BackColor = Color.Transparent;
            hint.ForeColor = TextMuted;
            hint.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular);
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Text = "VNPT: DNS + hosts   |   Campus: local TLS relay + PAC";

            row.Controls.Add(label);
            row.Controls.Add(_VnptModeButton);
            row.Controls.Add(_CampusModeButton);
            row.Controls.Add(hint);
            return row;
        }

        private Control BuildStatusCard()
        {
            _statusCard = new RoundedPanel();
            _statusCard.Dock = DockStyle.Fill;
            _statusCard.Margin = new Padding(0);
            _statusCard.BackColor = PanelBack;
            _statusCard.BorderColor = PanelBorder;
            _statusCard.CornerRadius = 14;

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 2;
            grid.RowCount = 2;
            grid.Margin = new Padding(0);
            grid.Padding = new Padding(28, 14, 22, 16);
            grid.BackColor = Color.Transparent;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 196F));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _statusLabel = new Label();
            _statusLabel.AutoSize = false;
            _statusLabel.Dock = DockStyle.Fill;
            _statusLabel.Margin = new Padding(0);
            _statusLabel.BackColor = Color.Transparent;
            _statusLabel.ForeColor = TextMuted;
            _statusLabel.Font = new Font("Segoe UI", 17F, FontStyle.Bold);
            _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            _statusLabel.Text = "\u25CF  CHECKING...";

            _detailLabel = new Label();
            _detailLabel.AutoSize = false;
            _detailLabel.Dock = DockStyle.Fill;
            _detailLabel.Margin = new Padding(0);
            _detailLabel.BackColor = Color.Transparent;
            _detailLabel.ForeColor = TextMuted;
            _detailLabel.Font = new Font("Segoe UI", 9.25F, FontStyle.Regular);
            _detailLabel.TextAlign = ContentAlignment.TopLeft;
            _detailLabel.AutoEllipsis = true;

            _actionButton = new RoundedButton();
            _actionButton.Dock = DockStyle.Fill;
            _actionButton.Margin = new Padding(12, 12, 0, 12);
            _actionButton.CornerRadius = 9;
            _actionButton.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _actionButton.BackColor = PanelBorder;
            _actionButton.ForeColor = TextMain;
            _actionButton.HoverBackColor = Color.FromArgb(71, 85, 105);
            _actionButton.PressedBackColor = Color.FromArgb(51, 65, 85);
            _actionButton.BorderColor = Color.FromArgb(30, 41, 59);
            _actionButton.Text = "Checking...";
            _actionButton.Enabled = false;
            _actionButton.Click += ActionButton_Click;

            grid.Controls.Add(_statusLabel, 0, 0);
            grid.Controls.Add(_detailLabel, 0, 1);
            grid.Controls.Add(_actionButton, 1, 0);
            grid.SetRowSpan(_actionButton, 2);

            _statusCard.Controls.Add(grid);
            return _statusCard;
        }

        private Control BuildButtonRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.Dock = DockStyle.Fill;
            row.Margin = new Padding(0);
            row.Padding = new Padding(0, 10, 0, 0);
            row.FlowDirection = FlowDirection.LeftToRight;
            row.WrapContents = false;
            row.BackColor = WindowBack;

            _testButton = CreateButton("Test connection", 150);
            _testButton.BackColor = Color.FromArgb(23, 37, 84);
            _testButton.ForeColor = Color.FromArgb(147, 197, 253);
            _testButton.HoverBackColor = Color.FromArgb(30, 58, 138);
            _testButton.PressedBackColor = Color.FromArgb(23, 37, 84);
            _testButton.BorderColor = Color.FromArgb(29, 78, 216);
            _testButton.Click += TestButton_Click;

            _refreshButton = CreateButton("Refresh", 96);
            _refreshButton.Click += delegate
            {
                RefreshStatus();
                AppendLog("Status refreshed.", TextMuted);
            };

            _folderButton = CreateButton("Open data folder", 148);
            _folderButton.Click += delegate
            {
                OpenFolder(Program.DataDir);
            };

            row.Controls.Add(_testButton);
            row.Controls.Add(_refreshButton);
            row.Controls.Add(_folderButton);
            return row;
        }

        private RoundedButton CreateButton(string text, int width)
        {
            RoundedButton button = new RoundedButton();
            button.Text = text;
            button.Width = width;
            button.Height = 38;
            button.Margin = new Padding(0, 0, 10, 0);
            button.CornerRadius = 9;
            button.Font = new Font("Segoe UI", 9.5F, FontStyle.Regular);
            button.BackColor = PanelBack;
            button.ForeColor = TextMain;
            button.HoverBackColor = Color.FromArgb(39, 52, 73);
            button.PressedBackColor = Color.FromArgb(30, 41, 59);
            button.BorderColor = PanelBorder;
            return button;
        }

        private Control BuildActivityRow()
        {
            Panel row = new Panel();
            row.Dock = DockStyle.Fill;
            row.Margin = new Padding(0);
            row.BackColor = WindowBack;

            Label label = new Label();
            label.Dock = DockStyle.Left;
            label.Width = 120;
            label.Margin = new Padding(0);
            label.BackColor = Color.Transparent;
            label.ForeColor = TextMuted;
            label.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Text = "ACTIVITY";

            _clearLink = new LinkLabel();
            _clearLink.Dock = DockStyle.Right;
            _clearLink.Width = 52;
            _clearLink.Margin = new Padding(0);
            _clearLink.BackColor = Color.Transparent;
            _clearLink.ForeColor = TextMuted;
            _clearLink.LinkColor = TextMuted;
            _clearLink.ActiveLinkColor = Blue;
            _clearLink.VisitedLinkColor = TextMuted;
            _clearLink.LinkBehavior = LinkBehavior.HoverUnderline;
            _clearLink.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular);
            _clearLink.TextAlign = ContentAlignment.MiddleRight;
            _clearLink.Text = "Clear";
            _clearLink.LinkClicked += delegate
            {
                if (_logBox != null && !_logBox.IsDisposed)
                {
                    _logBox.Clear();
                    AppendLog("Activity log cleared.", TextMuted);
                }
            };

            row.Controls.Add(label);
            row.Controls.Add(_clearLink);
            return row;
        }

        private Control BuildLogCard()
        {
            RoundedPanel card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);
            card.BackColor = Color.FromArgb(9, 15, 28);
            card.BorderColor = PanelBorder;
            card.CornerRadius = 14;
            card.Padding = new Padding(14, 12, 8, 12);

            _logBox = new RichTextBox();
            _logBox.Dock = DockStyle.Fill;
            _logBox.Margin = new Padding(0);
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.BackColor = Color.FromArgb(9, 15, 28);
            _logBox.ForeColor = Color.FromArgb(203, 213, 225);
            _logBox.Font = new Font("Consolas", 9F, FontStyle.Regular);
            _logBox.ReadOnly = true;
            _logBox.WordWrap = false;
            _logBox.DetectUrls = false;
            _logBox.HideSelection = false;
            _logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            _logBox.TabStop = false;

            card.Controls.Add(_logBox);
            return card;
        }

        private Control BuildFooter()
        {
            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0);
            footer.BackColor = Color.FromArgb(2, 6, 23);

            TableLayoutPanel grid = new TableLayoutPanel();
            grid.Dock = DockStyle.Fill;
            grid.ColumnCount = 3;
            grid.RowCount = 1;
            grid.Margin = new Padding(0);
            grid.Padding = new Padding(24, 0, 24, 0);
            grid.BackColor = Color.Transparent;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _adminLabel = new Label();
            _adminLabel.AutoSize = true;
            _adminLabel.Anchor = AnchorStyles.Left;
            _adminLabel.Margin = new Padding(0);
            _adminLabel.BackColor = Color.Transparent;
            _adminLabel.ForeColor = TextMuted;
            _adminLabel.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular);
            _adminLabel.Text = "Checking privileges...";

            _lastTestLabel = new Label();
            _lastTestLabel.Dock = DockStyle.Fill;
            _lastTestLabel.Margin = new Padding(0);
            _lastTestLabel.BackColor = Color.Transparent;
            _lastTestLabel.ForeColor = TextMuted;
            _lastTestLabel.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular);
            _lastTestLabel.TextAlign = ContentAlignment.MiddleCenter;
            _lastTestLabel.Text = "Last test: not run";

            Label version = new Label();
            version.AutoSize = true;
            version.Anchor = AnchorStyles.Right;
            version.Margin = new Padding(0);
            version.BackColor = Color.Transparent;
            version.ForeColor = Color.FromArgb(100, 116, 139);
            version.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular);
            version.Text = "v2.0";

            grid.Controls.Add(_adminLabel, 0, 0);
            grid.Controls.Add(_lastTestLabel, 1, 0);
            grid.Controls.Add(version, 2, 0);
            footer.Controls.Add(grid);
            return footer;
        }

        private async void MainForm_Shown(object sender, EventArgs e)
        {
            RefreshStatus();
            AppendLog("Steam Bypass is ready.", TextMain);
            AppendLog("Mode: " + ModeName(_mode), TextMuted);
            AppendLog("Data folder: " + Program.DataDir, TextMuted);
            AppendLog("All changes are local to this PC.", TextMuted);
            AppendLog("Current state: " + StateName(_state), TextMuted);
            AppendLog("", TextMuted);

            if (!string.IsNullOrEmpty(_runtimeError))
            {
                AddOutputLine("ERROR: " + _runtimeError, true);
                MessageBox.Show(
                    this,
                    "The embedded scripts could not be prepared.\r\n\r\n" + _runtimeError,
                    "Steam Bypass",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            if (_autoApply)
            {
                _autoApply = false;
                await AutoEnableSelectedModeAsync();
            }
        }

        private async Task AutoEnableSelectedModeAsync()
        {
            if (_mode == BypassMode.Campus)
            {
                if (!_Campus.On)
                {
                    await EnableCampusAsync(false);
                }
                return;
            }

            if (_state == BypassState.On)
            {
                return;
            }

            int exitCode = await RunScriptAsync(
                "Enable-VnptBypass.ps1",
                null,
                "Turning the VNPT bypass ON",
                false);
            if (exitCode == 0)
            {
                ResetLastTest();
                AppendLog("VNPT bypass is ON.", Green);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                MessageBox.Show(
                    this,
                    "An operation is still running. Please wait for it to finish.",
                    "Steam Bypass",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private void RefreshStatus()
        {
            if (_busy)
            {
                return;
            }

            int rules = Program.CountNrptRules();
            bool hasBlock;
            int pins;
            bool hostsReadable = Program.TryReadHosts(out hasBlock, out pins);
            bool known = rules >= 0 && hostsReadable;

            if (rules < 0)
            {
                rules = 0;
            }

            CampusStatus Campus = CampusMode.GetStatus();
            _Campus = Campus;

            BypassMode? active = null;
            if (Campus.AnyState)
            {
                active = BypassMode.Campus;
            }
            else if (known && (rules > 0 || hasBlock))
            {
                active = BypassMode.Vnpt;
            }
            _activeMode = active;

            if (!_modeInitialized)
            {
                if (!_hasInitialMode && active.HasValue)
                {
                    _mode = active.Value;
                    SaveMode();
                }
                _modeInitialized = true;
            }

            UpdateModeButtons();

            if (_mode == BypassMode.Campus)
            {
                RefreshCampusStatus();
            }
            else
            {
                RefreshVnptStatus(rules, hasBlock, pins, known);
            }

            if (_activeMode.HasValue && _activeMode.Value != _mode)
            {
                SetDetail(_detailLabel.Text +
                    " The " + ModeName(_activeMode.Value) +
                    " mode is currently active; it is turned off automatically before this mode is enabled.");
            }

            if (!_isAdministrator)
            {
                _detailLabel.Text = _detailLabel.Text + " Administrator rights are required to change it.";
                _adminLabel.Text = "Not elevated";
                _adminLabel.ForeColor = Red;
                _actionButton.Enabled = false;
            }
            else
            {
                _adminLabel.Text = "Administrator";
                _adminLabel.ForeColor = Green;
                _actionButton.Enabled = true;
            }

            Text = "Steam Bypass - " + ModeShortName(_mode) + " " + StateName(_state);
        }

        private void RefreshVnptStatus(int rules, bool hasBlock, int pins, bool known)
        {
            if (!known)
            {
                _state = BypassState.Partial;
                SetStatus("VNPT MODE IS UNKNOWN", Amber);
                SetDetail("Could not read the current NRPT or hosts state. Click Repair bypass to reapply.");
                SetAction("Repair bypass", BrightAmber, Color.White);
            }
            else if (rules > 0 && hasBlock)
            {
                _state = BypassState.On;
                SetStatus("VNPT MODE IS ON", Green);
                if (pins > 0)
                {
                    SetDetail(string.Format(
                        "{0} NRPT rules and {1} host pins. Steam DNS uses Google; pinned names skip DNS.",
                        rules, pins));
                }
                else
                {
                    SetDetail(string.Format(
                        "{0} NRPT rules. DNS-only mode (no hosts pins).", rules));
                }
                SetAction("Turn OFF", BrightRed, Color.White);
            }
            else if (rules == 0 && !hasBlock)
            {
                _state = BypassState.Off;
                SetStatus("VNPT MODE IS OFF", TextMuted);
                SetDetail("Nothing is installed. Steam uses your normal resolver and hosts file.");
                SetAction("Turn ON", BrightGreen, Color.White);
            }
            else
            {
                _state = BypassState.Partial;
                SetStatus("VNPT MODE IS PARTIAL", Amber);
                if (rules > 0)
                {
                    SetDetail(string.Format(
                        "{0} NRPT rules but no hosts block. Click Repair bypass.", rules));
                }
                else
                {
                    SetDetail(string.Format(
                        "Hosts block present ({0} pins) but no NRPT rules. Click Repair bypass.", pins));
                }
                SetAction("Repair bypass", BrightAmber, Color.White);
            }
        }

        private void RefreshCampusStatus()
        {
            if (_Campus.On)
            {
                _state = BypassState.On;
                SetStatus("CAMPUS MODE IS ON", Green);
                SetDetail(string.Format(
                    "Relay 127.0.0.1:{0}; PAC http://127.0.0.1:{1}/steam.pac. " +
                    "Steam/Valve traffic is fragmented locally; everything else stays direct.",
                    _Campus.RelayPort, _Campus.PacPort));
                SetAction("Turn OFF", BrightRed, Color.White);
            }
            else if (_Campus.AnyState)
            {
                _state = BypassState.Partial;
                SetStatus("CAMPUS MODE IS PARTIAL", Amber);
                SetDetail(BuildCampusPartialDetail());
                SetAction("Repair mode", BrightAmber, Color.White);
            }
            else
            {
                _state = BypassState.Off;
                SetStatus("CAMPUS MODE IS OFF", TextMuted);
                SetDetail("Nothing is installed. Turn it ON to start the local TLS relay and HTTP PAC.");
                SetAction("Turn ON", BrightGreen, Color.White);
            }
        }

        private string BuildCampusPartialDetail()
        {
            List<string> missing = new List<string>();
            if (!_Campus.RelayRunning)
            {
                missing.Add("the relay is not running");
            }
            if (!_Campus.ProxyConfigured)
            {
                missing.Add("the PAC proxy is not configured");
            }
            else if (!_Campus.PacAvailable)
            {
                missing.Add("the PAC file is missing");
            }
            else if (!_Campus.PacContentOk)
            {
                missing.Add("the PAC file is invalid");
            }
            if (_Campus.RelayPort <= 0)
            {
                missing.Add("the relay port is unknown");
            }
            if (_Campus.PacPort <= 0)
            {
                missing.Add("the PAC port is unknown");
            }

            string detail = missing.Count == 0
                ? "The relay and PAC are running, but the mode is not fully configured."
                : "Detected leftover Campus/HCMUS state: " + string.Join(", ", missing.ToArray()) + ".";
            return detail + " Click Repair mode to rebuild the relay and PAC.";
        }

        private void UpdateModeButtons()
        {
            StyleModeButton(_VnptModeButton, _mode == BypassMode.Vnpt);
            StyleModeButton(_CampusModeButton, _mode == BypassMode.Campus);
        }

        private static void StyleModeButton(RoundedButton button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            if (selected)
            {
                button.BackColor = Color.FromArgb(37, 99, 235);
                button.ForeColor = Color.White;
                button.HoverBackColor = Color.FromArgb(29, 78, 216);
                button.PressedBackColor = Color.FromArgb(30, 64, 175);
                button.BorderColor = Color.FromArgb(59, 130, 246);
            }
            else
            {
                button.BackColor = PanelBack;
                button.ForeColor = TextMuted;
                button.HoverBackColor = Color.FromArgb(39, 52, 73);
                button.PressedBackColor = Color.FromArgb(30, 41, 59);
                button.BorderColor = PanelBorder;
            }

            button.Invalidate();
        }

        private static string ModeName(BypassMode mode)
        {
            return mode == BypassMode.Campus ? "Campus/HCMUS" : "VNPT";
        }

        private static string ModeShortName(BypassMode mode)
        {
            return mode == BypassMode.Campus ? "Campus" : "VNPT";
        }

        private static BypassMode ParseMode(string value)
        {
            if (!string.IsNullOrEmpty(value) &&
                (value.IndexOf("campus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 value.IndexOf("hcmus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 value.IndexOf("relay", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 value.IndexOf("viettel", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return BypassMode.Campus;
            }
            return BypassMode.Vnpt;
        }

        private static string ModeFilePath
        {
            get { return Path.Combine(Program.DataDir, "mode.txt"); }
        }

        private static BypassMode LoadSavedMode()
        {
            try
            {
                if (File.Exists(ModeFilePath))
                {
                    return ParseMode(File.ReadAllText(ModeFilePath).Trim());
                }
            }
            catch
            {
                // A missing or unreadable preference falls back to VNPT.
            }
            return BypassMode.Vnpt;
        }

        private static void SaveMode(BypassMode mode)
        {
            try
            {
                Directory.CreateDirectory(Program.DataDir);
                File.WriteAllText(
                    ModeFilePath,
                    mode == BypassMode.Campus ? "campus" : "vnpt",
                    new UTF8Encoding(false));
            }
            catch
            {
                // Remembering the last mode is a convenience, not a requirement.
            }
        }

        private void SaveMode()
        {
            SaveMode(_mode);
        }

        private BypassMode? GetActiveModeNow()
        {
            try
            {
                if (CampusMode.GetStatus().AnyState)
                {
                    return BypassMode.Campus;
                }
            }
            catch
            {
                // Fall through to the VNPT check.
            }

            int rules = Program.CountNrptRules();
            bool hasBlock;
            int pins;
            if (Program.TryReadHosts(out hasBlock, out pins) && (rules > 0 || hasBlock))
            {
                return BypassMode.Vnpt;
            }
            return null;
        }

        private void EngineLog(string message)
        {
            AddOutputLine(message == null ? string.Empty : message, false);
        }

        private async void ModeButton_Click(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }

            BypassMode target = ReferenceEquals(sender, _CampusModeButton)
                ? BypassMode.Campus
                : BypassMode.Vnpt;
            if (target == _mode)
            {
                return;
            }

            BypassMode? active = GetActiveModeNow();
            if (active.HasValue && active.Value != target)
            {
                string question = active.Value == BypassMode.Campus
                    ? "Switching to VNPT mode will stop the Campus/HCMUS relay and restore your proxy settings.\r\n\r\nContinue?"
                    : "Switching to Campus mode will turn off the VNPT split-DNS bypass and restore DNS and hosts.\r\n\r\nContinue?";
                DialogResult answer = MessageBox.Show(
                    this,
                    question,
                    "Switch mode",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    return;
                }

                if (!await DisableModeAsync(active.Value))
                {
                    RefreshStatus();
                    return;
                }
            }

            _mode = target;
            _modeInitialized = true;
            SaveMode();
            RefreshStatus();
            AppendLog("Mode selected: " + ModeName(_mode) + ".", TextMain);
        }

        private async Task<bool> DisableModeAsync(BypassMode mode)
        {
            if (mode == BypassMode.Campus)
            {
                SetBusy(true, "Turning Campus/HCMUS mode OFF");
                try
                {
                    await Task.Run(delegate
                    {
                        CampusMode.Disable(EngineLog);
                    });
                    ResetLastTest();
                    AppendLog("Campus/HCMUS mode is OFF. Proxy settings restored.", Green);
                    return true;
                }
                catch (Exception ex)
                {
                    AddOutputLine("ERROR: " + ex.Message, true);
                    MessageBox.Show(
                        this,
                        "Could not turn Campus/HCMUS mode off.\r\n\r\n" + ex.Message,
                        "Steam Bypass",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return false;
                }
                finally
                {
                    SetBusy(false, null);
                }
            }

            int exitCode = await RunScriptAsync(
                "Disable-VnptBypass.ps1",
                null,
                "Turning the VNPT bypass OFF",
                true);
            return exitCode == 0;
        }

        private async Task<bool> PrepareModeEnableAsync(BypassMode mode)
        {
            BypassMode? active = GetActiveModeNow();
            if (!active.HasValue || active.Value == mode)
            {
                return true;
            }

            string question = active.Value == BypassMode.Campus
                ? "The Campus/HCMUS relay is active. Turn it off and restore proxy settings before enabling VNPT mode?"
                : "The VNPT bypass is active. Turn it off and restore DNS and hosts before enabling Campus mode?";
            DialogResult answer = MessageBox.Show(
                this,
                question,
                "Switch mode",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                return false;
            }

            return await DisableModeAsync(active.Value);
        }

        private async Task EnableCampusAsync(bool interactive)
        {
            if (!await PrepareModeEnableAsync(BypassMode.Campus))
            {
                return;
            }

            SetBusy(true, "Turning Campus/HCMUS mode ON");
            CampusStatus status = null;
            try
            {
                status = await Task.Run(delegate
                {
                    return CampusMode.Enable(EngineLog);
                });
                ResetLastTest();
                AppendLog("Campus/HCMUS mode is ON.", Green);

                if (interactive && status != null && status.On)
                {
                    MessageBox.Show(
                        this,
                        "Campus/HCMUS mode enabled.\r\n\r\n" +
                        "Fully quit Steam (including the tray icon), then start it again.",
                        "Steam Bypass",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else if (interactive)
                {
                    MessageBox.Show(
                        this,
                        "The relay or PAC did not become fully active.\r\n\r\n" +
                        "Click Test connection for details, or click Repair mode to try again.",
                        "Steam Bypass",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                AddOutputLine("ERROR: " + ex.Message, true);
                if (interactive)
                {
                    MessageBox.Show(
                        this,
                        "Could not turn Campus/HCMUS mode on.\r\n\r\n" + ex.Message,
                        "Steam Bypass",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
            finally
            {
                SetBusy(false, null);
            }
        }

        private async Task DisableCampusAsync(bool interactive)
        {
            if (interactive)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    "Turn Campus/HCMUS mode OFF and restore the previous proxy settings?",
                    "Turn off Campus mode",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    return;
                }
            }

            await DisableModeAsync(BypassMode.Campus);
        }

        private void ShowTestResult(string verdict, int exitCode)
        {
            UpdateLastTest(verdict);

            if (string.IsNullOrEmpty(verdict))
            {
                AppendLog("The test did not return a PASS/PARTIAL/FAIL verdict.", Red);
                MessageBox.Show(
                    this,
                    "The test did not finish with a verdict.\r\n\r\n" +
                    "Exit code: " + exitCode + "\r\n" +
                    "Check the activity log for details.",
                    "Connection test",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string word = verdictWord(verdict);
            Color accentColor = verdictColor(word);
            AppendLog("verdict: " + verdict, accentColor);

            MessageBoxIcon icon = MessageBoxIcon.Information;
            if (word == "PARTIAL")
            {
                icon = MessageBoxIcon.Warning;
            }
            else if (word == "FAIL")
            {
                icon = MessageBoxIcon.Error;
            }

            MessageBox.Show(
                this,
                verdict + "\r\n\r\nFull details are in the activity log.",
                "Connection test",
                MessageBoxButtons.OK,
                icon);
        }

        private void SetStatus(string text, Color color)
        {
            if (_statusLabel == null)
            {
                return;
            }

            _statusLabel.Text = "\u25CF  " + text;
            _statusLabel.ForeColor = color;

            if (_statusCard != null)
            {
                _statusCard.AccentColor = color;
                _statusCard.Invalidate();
            }
        }

        private void SetDetail(string text)
        {
            if (_detailLabel != null)
            {
                _detailLabel.Text = text;
            }
        }

        private void SetAction(string text, Color background, Color foreground)
        {
            if (_actionButton == null)
            {
                return;
            }

            _actionButton.Text = text;
            _actionButton.BackColor = background;
            _actionButton.ForeColor = foreground;
            _actionButton.HoverBackColor = darken(background, 0.84);
            _actionButton.PressedBackColor = darken(background, 0.70);
            _actionButton.BorderColor = darken(background, 0.62);
            _actionButton.Invalidate();
        }

        private static Color darken(Color color, double factor)
        {
            int red = (int)Math.Max(0, Math.Min(255, color.R * factor));
            int green = (int)Math.Max(0, Math.Min(255, color.G * factor));
            int blue = (int)Math.Max(0, Math.Min(255, color.B * factor));
            return Color.FromArgb(color.A, red, green, blue);
        }

        private void SetBusy(bool busy, string actionLabel)
        {
            _busy = busy;
            UseWaitCursor = busy;

            if (_testButton != null)
            {
                _testButton.Enabled = !busy;
            }
            if (_refreshButton != null)
            {
                _refreshButton.Enabled = !busy;
            }
            if (_folderButton != null)
            {
                _folderButton.Enabled = !busy;
            }
            if (_clearLink != null)
            {
                _clearLink.Enabled = !busy;
            }
            if (_VnptModeButton != null)
            {
                _VnptModeButton.Enabled = !busy;
            }
            if (_CampusModeButton != null)
            {
                _CampusModeButton.Enabled = !busy;
            }

            if (busy)
            {
                if (_actionButton != null)
                {
                    _actionButton.Enabled = false;
                    _actionButton.Text = "Working...";
                    _actionButton.BackColor = PanelBorder;
                    _actionButton.ForeColor = TextMuted;
                    _actionButton.HoverBackColor = PanelBorder;
                    _actionButton.PressedBackColor = PanelBorder;
                    _actionButton.BorderColor = Color.FromArgb(30, 41, 59);
                    _actionButton.Invalidate();
                }

                SetStatus("WORKING", Blue);
                SetDetail(actionLabel + ". This can take a while; the window stays responsive...");
            }
            else
            {
                RefreshStatus();
            }
        }

        private async void ActionButton_Click(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }

            if (!_isAdministrator)
            {
                MessageBox.Show(
                    this,
                    "This action requires administrator rights. Close the app and start it again as administrator.",
                    "Steam Bypass",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (_mode == BypassMode.Campus)
            {
                if (_Campus.On)
                {
                    await DisableCampusAsync(true);
                }
                else
                {
                    await EnableCampusAsync(true);
                }
                return;
            }

            if (_state == BypassState.On)
            {
                DialogResult answer = MessageBox.Show(
                    this,
                    "Turn the VNPT bypass OFF and restore the previous DNS and hosts state?\r\n\r\n" +
                    "Steam will go back to using the VNPT resolver.",
                    "Turn off VNPT bypass",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (answer != DialogResult.Yes)
                {
                    return;
                }

                if (await DisableModeAsync(BypassMode.Vnpt))
                {
                    ResetLastTest();
                    AppendLog("VNPT bypass is OFF. DNS and hosts are restored.", Green);

                    if (_state != BypassState.Off)
                    {
                        MessageBox.Show(
                            this,
                            "The disable script finished, but some VNPT bypass state remains.\r\n\r\n" +
                            "Check the activity log or run the disable script again.",
                            "Steam Bypass",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }
            else
            {
                if (!await PrepareModeEnableAsync(BypassMode.Vnpt))
                {
                    return;
                }

                int exitCode = await RunScriptAsync(
                    "Enable-VnptBypass.ps1",
                    null,
                    "Turning the VNPT bypass ON",
                    true);

                if (exitCode == 0)
                {
                    ResetLastTest();

                    if (_state == BypassState.On)
                    {
                        bool verificationFailed = _runOutput != null &&
                            _runOutput.ToString().IndexOf("FAIL:", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (!verificationFailed)
                        {
                            MessageBox.Show(
                                this,
                                "VNPT bypass enabled.\r\n\r\n" +
                                "Fully quit Steam (including the tray icon), then start it again.",
                                "Steam Bypass",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                        else
                        {
                            MessageBox.Show(
                                this,
                                "The bypass rules are active, but the enable script reported failed endpoint checks.\r\n\r\n" +
                                "Click Test connection for details, or click Repair bypass when the network is healthy.",
                                "Steam Bypass",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            this,
                            "The enable script finished, but the VNPT bypass is not fully active.\r\n\r\n" +
                            "Click Test connection to check it, or click Repair bypass to try again.",
                            "Steam Bypass",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }
        }

        private async void TestButton_Click(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }

            if (_lastTestLabel != null)
            {
                _lastTestLabel.Text = "Last test: running...";
                _lastTestLabel.ForeColor = Blue;
            }

            string verdict = null;
            int exitCode = -1;

            if (_mode == BypassMode.Campus)
            {
                SetBusy(true, "Testing Campus/HCMUS mode");
                try
                {
                    CampusTestResult result = await Task.Run(delegate
                    {
                        return CampusMode.Test(EngineLog);
                    });
                    if (result != null)
                    {
                        verdict = result.verdict;
                    }
                }
                catch (Exception ex)
                {
                    AddOutputLine("ERROR: " + ex.Message, true);
                }
                finally
                {
                    SetBusy(false, null);
                }
            }
            else
            {
                exitCode = await RunScriptAsync(
                    "Test-VnptBypass.ps1",
                    "-TimeoutSec 8",
                    "Testing the VNPT connection",
                    false);

                string output = _runOutput == null ? string.Empty : _runOutput.ToString();
                verdict = FindVerdict(output);
            }

            ShowTestResult(verdict, exitCode);
        }

        private async Task<int> RunScriptAsync(
            string scriptName,
            string extraArguments,
            string actionLabel,
            bool showFailureDialog)
        {
            if (_busy)
            {
                return -1;
            }

            _runOutput = new StringBuilder();
            SetBusy(true, actionLabel);

            string prefix = "run";
            if (scriptName.IndexOf("Enable", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                prefix = "enable";
            }
            else if (scriptName.IndexOf("Disable", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                prefix = "disable";
            }
            else if (scriptName.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                prefix = "test";
            }

            string logPath = Path.Combine(
                Program.LogDir,
                prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");

            StreamWriter writer = null;
            bool logSaved = false;
            int exitCode = -1;

            AppendLog("", TextMuted);
            AppendLog("== " + actionLabel + " ==", Blue);
            AppendLog(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), TextMuted);

            try
            {
                writer = new StreamWriter(logPath, false, new UTF8Encoding(false));
                logSaved = true;
                writer.WriteLine("== " + actionLabel + " ==");
                writer.WriteLine("Started: " + DateTime.Now.ToString("o"));
                writer.WriteLine("Script: " + Path.Combine(Program.ScriptDir, scriptName));
                writer.WriteLine();
                writer.Flush();

                string scriptPath = Path.Combine(Program.ScriptDir, scriptName);
                if (!File.Exists(scriptPath))
                {
                    throw new FileNotFoundException("Script not found: " + scriptPath);
                }

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = Path.Combine(
                    Environment.SystemDirectory,
                    @"WindowsPowerShell\v1.0\powershell.exe");
                string arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + scriptPath + "\"";
                if (!string.IsNullOrEmpty(extraArguments))
                {
                    arguments = arguments + " " + extraArguments;
                }
                startInfo.Arguments = arguments;
                startInfo.WorkingDirectory = Program.ScriptDir;
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;

                object writerLock = new object();

                using (Process process = new Process())
                {
                    process.StartInfo = startInfo;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
                    {
                        if (eventArgs.Data == null)
                        {
                            return;
                        }

                        lock (writerLock)
                        {
                            if (writer != null)
                            {
                                try
                                {
                                    writer.WriteLine(eventArgs.Data);
                                    writer.Flush();
                                }
                                catch
                                {
                                    // The activity log on screen still has the line.
                                }
                            }
                        }

                        AddOutputLine(eventArgs.Data, false);
                    };

                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
                    {
                        if (eventArgs.Data == null)
                        {
                            return;
                        }

                        lock (writerLock)
                        {
                            if (writer != null)
                            {
                                try
                                {
                                    writer.WriteLine("[stderr] " + eventArgs.Data);
                                    writer.Flush();
                                }
                                catch
                                {
                                    // The activity log on screen still has the line.
                                }
                            }
                        }

                        AddOutputLine(eventArgs.Data, true);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await Task.Run(delegate
                    {
                        process.WaitForExit();
                    });

                    process.WaitForExit();
                    exitCode = process.ExitCode;
                }

                writer.WriteLine();
                writer.WriteLine("Exit code: " + exitCode);
                writer.Flush();
            }
            catch (Exception ex)
            {
                AddOutputLine("ERROR: " + ex.Message, true);
                if (writer != null)
                {
                    try
                    {
                        writer.WriteLine();
                        writer.WriteLine("ERROR: " + ex);
                        writer.Flush();
                    }
                    catch
                    {
                        // The original exception is already being reported.
                    }
                }
            }
            finally
            {
                if (writer != null)
                {
                    try
                    {
                        writer.Flush();
                        writer.Dispose();
                    }
                    catch
                    {
                        // Log cleanup must not hide the operation result.
                    }
                }

                SetBusy(false, null);

                if (logSaved)
                {
                    AppendLog("Log saved: " + logPath, TextMuted);
                }
            }

            if (exitCode != 0 && showFailureDialog)
            {
                MessageBox.Show(
                    this,
                    actionLabel + " did not complete successfully (exit code " + exitCode + ").\r\n\r\n" +
                    "See the activity log above for details.",
                    "Steam Bypass",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            AppendLog(
                exitCode == 0 ? "Done." : "Finished with errors.",
                exitCode == 0 ? Green : Red);

            return exitCode;
        }

        private void AddOutputLine(string line, bool isError)
        {
            StringBuilder buffer = _runOutput;
            if (buffer != null)
            {
                lock (buffer)
                {
                    buffer.AppendLine(line);
                }
            }

            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            Color color = isError ? Red : TextMain;
            string trimmed = line.TrimStart();

            if (!isError)
            {
                if (trimmed.StartsWith("PASS:", StringComparison.OrdinalIgnoreCase))
                {
                    color = Green;
                }
                else if (trimmed.StartsWith("PARTIAL:", StringComparison.OrdinalIgnoreCase))
                {
                    color = Amber;
                }
                else if (trimmed.StartsWith("FAIL:", StringComparison.OrdinalIgnoreCase))
                {
                    color = Red;
                }
                else if (trimmed.IndexOf("WARNING", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    color = Amber;
                }
                else if (trimmed.IndexOf("FAIL:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         trimmed.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    color = Red;
                }
            }

            try
            {
                BeginInvoke(new Action(delegate
                {
                    AppendLog(line, color);
                }));
            }
            catch (ObjectDisposedException)
            {
                // The window is closing.
            }
            catch (InvalidOperationException)
            {
                // The window is closing.
            }
        }

        private void AppendLog(string text, Color color)
        {
            if (_logBox == null || _logBox.IsDisposed)
            {
                return;
            }

            if (text == null)
            {
                text = string.Empty;
            }

            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.SelectionLength = 0;
            _logBox.SelectionColor = color;
            _logBox.AppendText(text + Environment.NewLine);
            _logBox.SelectionColor = _logBox.ForeColor;
            _logBox.ScrollToCaret();
        }

        private static string FindVerdict(string output)
        {
            if (string.IsNullOrEmpty(output))
            {
                return null;
            }

            MatchCollection matches = Regex.Matches(
                output,
                @"^(PASS|PARTIAL|FAIL):\s*(.+)$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);

            if (matches.Count == 0)
            {
                return null;
            }

            return matches[matches.Count - 1].Value.Trim();
        }

        private static string verdictWord(string verdict)
        {
            if (string.IsNullOrEmpty(verdict))
            {
                return string.Empty;
            }

            int colon = verdict.IndexOf(':');
            if (colon <= 0)
            {
                return string.Empty;
            }

            return verdict.Substring(0, colon).Trim().ToUpperInvariant();
        }

        private static Color verdictColor(string word)
        {
            if (word == "PASS")
            {
                return Green;
            }
            if (word == "PARTIAL")
            {
                return Amber;
            }
            return Red;
        }

        private void UpdateLastTest(string verdict)
        {
            if (_lastTestLabel == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(verdict))
            {
                _lastTestLabel.Text = "Last test: no verdict - " + DateTime.Now.ToString("HH:mm");
                _lastTestLabel.ForeColor = Red;
                return;
            }

            string word = verdictWord(verdict);
            _lastTestLabel.Text = "Last test: " + word + " - " + DateTime.Now.ToString("HH:mm");
            _lastTestLabel.ForeColor = verdictColor(word);
        }

        private void ResetLastTest()
        {
            if (_lastTestLabel != null)
            {
                _lastTestLabel.Text = "Last test: not run";
                _lastTestLabel.ForeColor = TextMuted;
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = path;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    "Could not open the data folder.\r\n\r\n" + ex.Message,
                    "Steam Bypass",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static string StateName(BypassState state)
        {
            if (state == BypassState.On)
            {
                return "ON";
            }
            if (state == BypassState.Off)
            {
                return "OFF";
            }
            return "PARTIAL";
        }
    }
}

