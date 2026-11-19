using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace VZNepTunnel;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private const int StudioPort = 55555;
    private const int RelayPort = 55556;

    private readonly TextBox placeBox = new() { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = "Choose a local .rbxl or .rbxlx file" };
    private readonly TextBox studioBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox hostAddressBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox hostSessionBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox joinAddressBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Public address and port, e.g. 203.0.113.10:55556" };
    private readonly TextBox joinSessionBox = new() { Dock = DockStyle.Fill, PlaceholderText = "Paste the host's session ID" };
    private readonly Label hostStatus = new() { AutoSize = true, Text = "Not hosting", ForeColor = Color.Khaki };
    private readonly Label joinStatus = new() { AutoSize = true, Text = "Not connected", ForeColor = Color.Khaki };
    private readonly TextBox logBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 16, 25), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.FixedSingle };

    private string? placePath;
    private string? studioPath;
    private HostSessionUdpRelay? hostRelay;
    private JoinSessionUdpProxy? joinProxy;
    private UpnpPortMapping? portMapping;
    private Process? studioServerProcess;
    private Process? studioClientProcess;
    private bool closing;

    public MainForm()
    {
        Text = "VZ — Multiplayer";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(800, 700);
        Size = new Size(940, 780);
        BackColor = Color.FromArgb(18, 22, 32);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "VZ",
            AutoSize = true,
            Font = new Font("Segoe UI", 34, FontStyle.Bold),
            ForeColor = Color.White,
            Margin = new Padding(0)
        });
        root.Controls.Add(new Label
        {
            Text = "CHOOSE A PLACE  •  HOST A SESSION  •  SHARE THE ADDRESS",
            AutoSize = true,
            ForeColor = Color.FromArgb(111, 185, 255),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = new Padding(2, 0, 0, 16)
        });

        var placePanel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
        placePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        placePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        placePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        placePanel.Controls.Add(FieldLabel("PLACE FILE"), 0, 0);
        placePanel.Controls.Add(placeBox, 1, 0);
        placePanel.Controls.Add(StyledButton("Browse…", ChoosePlace), 2, 0);
        placePanel.Controls.Add(FieldLabel("ROBLOX STUDIO"), 0, 1);
        placePanel.Controls.Add(studioBox, 1, 1);
        placePanel.Controls.Add(StyledButton("Locate…", LocateStudio), 2, 1);
        var openActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        openActions.Controls.Add(PrimaryButton("OPEN IN STUDIO", OpenPlace));
        openActions.Controls.Add(StyledButton("STOP SESSION", StopSession));
        placePanel.Controls.Add(openActions, 1, 2);
        placePanel.SetColumnSpan(openActions, 2);
        root.Controls.Add(placePanel);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 5) };
        tabs.TabPages.Add(BuildHostTab());
        tabs.TabPages.Add(BuildJoinTab());
        root.Controls.Add(tabs);

        var logPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        logPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        logPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logPanel.Controls.Add(new Label { Text = "ACTIVITY", AutoSize = true, ForeColor = Color.Silver, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 4, 0, 7) });
        logPanel.Controls.Add(logBox);
        root.Controls.Add(logPanel);

        studioPath = FindStudio();
        if (studioPath != null) studioBox.Text = studioPath;
        Log("VZ ready. Direct hosting uses your router's UPnP feature; no relay account or port-forwarding website is required.");
        Log("Direct hosting requires a public IPv4 connection. CGNAT or a router with UPnP disabled can prevent remote connections.");
    }

    private TabPage BuildHostTab()
    {
        var page = new TabPage("HOST SESSION") { BackColor = Color.FromArgb(22, 27, 39), ForeColor = Color.White, Padding = new Padding(16) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 8; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(FieldLabel("SESSION STATUS"), 0, 0);
        layout.Controls.Add(hostStatus, 1, 0);
        layout.Controls.Add(FieldLabel("TUNNEL ADDRESS"), 0, 1);
        layout.Controls.Add(hostAddressBox, 1, 1);
        layout.Controls.Add(FieldLabel("SESSION ID"), 0, 2);
        layout.Controls.Add(hostSessionBox, 1, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 12) };
        buttons.Controls.Add(PrimaryButton("HOST SESSION", StartHostSession));
        buttons.Controls.Add(StyledButton("COPY JOIN DETAILS", CopyJoinDetails));
        buttons.Controls.Add(StyledButton("STOP HOSTING", StopSession));
        layout.Controls.Add(buttons, 1, 3);

        layout.Controls.Add(new Label
        {
            Text = "VZ automatically asks your router to open a UDP port and detects your public IP. The session ID is generated automatically. Players only need these two values to join.",
            AutoSize = true,
            MaximumSize = new Size(730, 0),
            ForeColor = Color.Silver,
            Margin = new Padding(0, 8, 0, 10)
        }, 1, 4);
        layout.Controls.Add(new Label
        {
            Text = "If hosting fails, your router may have UPnP disabled or your ISP may use CGNAT. A direct connection cannot work through CGNAT without an external relay. VZ does not silently pretend such a relay exists.",
            AutoSize = true,
            MaximumSize = new Size(730, 0),
            ForeColor = Color.Khaki,
            Margin = new Padding(0, 8, 0, 10)
        }, 1, 5);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildJoinTab()
    {
        var page = new TabPage("JOIN SESSION") { BackColor = Color.FromArgb(22, 27, 39), ForeColor = Color.White, Padding = new Padding(16) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(FieldLabel("HOST ADDRESS"), 0, 0);
        layout.Controls.Add(joinAddressBox, 1, 0);
        layout.Controls.Add(FieldLabel("SESSION ID"), 0, 1);
        layout.Controls.Add(joinSessionBox, 1, 1);
        layout.Controls.Add(FieldLabel("CONNECTION"), 0, 2);
        layout.Controls.Add(joinStatus, 1, 2);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 12) };
        buttons.Controls.Add(PrimaryButton("JOIN SESSION", JoinSession));
        buttons.Controls.Add(StyledButton("STOP / DISCONNECT", StopSession));
        layout.Controls.Add(buttons, 1, 3);
        layout.Controls.Add(new Label
        {
            Text = "Enter the host's public address including port, then enter the session ID. You do not need the host's map file or any relay software.",
            AutoSize = true,
            MaximumSize = new Size(730, 0),
            ForeColor = Color.Silver,
            Margin = new Padding(0, 8, 0, 10)
        }, 1, 4);
        page.Controls.Add(layout);
        return page;
    }

    private static Label FieldLabel(string value) => new()
    {
        Text = value,
        AutoSize = true,
        ForeColor = Color.Silver,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        Margin = new Padding(0, 7, 14, 7),
        Anchor = AnchorStyles.Left
    };

    private Button StyledButton(string text, Action action)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(13, 8, 13, 8), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(48, 57, 74), ForeColor = Color.White, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 9, 0) };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) => action();
        return button;
    }

    private Button PrimaryButton(string text, Action action)
    {
        var button = StyledButton(text, action);
        button.BackColor = Color.FromArgb(26, 105, 184);
        return button;
    }

    private void ChoosePlace()
    {
        using var dialog = new OpenFileDialog { Title = "Select a Roblox place", Filter = "Roblox place files (*.rbxl;*.rbxlx)|*.rbxl;*.rbxlx", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        placePath = dialog.FileName;
        placeBox.Text = placePath;
        Log("Selected place: " + placePath);
    }

    private void LocateStudio()
    {
        using var dialog = new OpenFileDialog { Title = "Locate RobloxStudioBeta.exe", Filter = "Roblox Studio|RobloxStudioBeta.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        studioPath = dialog.FileName;
        studioBox.Text = studioPath;
    }

    private string? FindStudio()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Roblox", "Versions"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Roblox", "Versions")
        };
        return roots.Where(Directory.Exists)
            .SelectMany(root => Directory.GetDirectories(root))
            .Select(folder => Path.Combine(folder, "RobloxStudioBeta.exe"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private bool ValidateStudio(bool requirePlace = true)
    {
        if (requirePlace && (string.IsNullOrWhiteSpace(placePath) || !File.Exists(placePath)))
        {
            MessageBox.Show(this, "Choose your local .rbxl or .rbxlx file first.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        studioPath ??= FindStudio();
        if (string.IsNullOrWhiteSpace(studioPath) || !File.Exists(studioPath))
        {
            MessageBox.Show(this, "Roblox Studio was not found. Use Locate… to select RobloxStudioBeta.exe.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        studioBox.Text = studioPath;
        return true;
    }

    private void LaunchPlace()
    {
        var start = new ProcessStartInfo { FileName = studioPath!, UseShellExecute = false };
        start.ArgumentList.Add(placePath!);
        Process.Start(start);
    }

    private void OpenPlace()
    {
        if (!ValidateStudio()) return;
        try
        {
            LaunchPlace();
            Log("Opened the selected local place in Roblox Studio. No upload or publishing was performed.");
        }
        catch (Exception ex)
        {
            Log("Studio launch failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "VZ launch error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void StartHostSession()
    {
        if (hostRelay != null || portMapping != null || joinProxy != null)
        {
            MessageBox.Show(this, "A VZ session is already active. Stop it first.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!ValidateStudio()) return;

        try
        {
            StopSession();
            string sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            string parentGuid = Guid.NewGuid().ToString().ToUpperInvariant();
            string playGuid = Guid.NewGuid().ToString().ToUpperInvariant();

            Log("Opening the local place in Studio...");
            LaunchPlace();
            await Task.Delay(2500);

            Log("Starting the Studio test server on UDP " + StudioPort + "...");
            studioServerProcess = LaunchStudioServer(parentGuid, playGuid);
            await Task.Delay(4000);
            if (studioServerProcess.HasExited)
                throw new InvalidOperationException("Roblox Studio's StartServer process exited. Check your Studio installation and place file.");

            Log("Starting the protected VZ UDP session bridge...");
            hostRelay = new HostSessionUdpRelay(sessionId);
            hostRelay.Start();

            SetHostStatus("Opening router port automatically…", Color.Khaki);
            portMapping = await UpnpPortMapping.CreateAsync(RelayPort);
            string endpoint = $"{portMapping.PublicAddress}:{portMapping.ExternalPort}";

            hostAddressBox.Text = endpoint;
            hostSessionBox.Text = sessionId;
            SetHostStatus("HOSTING — DIRECT CONNECTION", Color.LightGreen);
            Log("Direct UDP tunnel is live at " + endpoint);
            Log("Share the address and session ID with friends. Keep VZ and the Studio server running.");
        }
        catch (Exception ex)
        {
            Log("Host failed: " + ex.Message);
            StopSession();
            SetHostStatus("Could not create direct connection", Color.OrangeRed);
            MessageBox.Show(this, ex.Message, "VZ direct hosting unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void JoinSession()
    {
        if (hostRelay != null || portMapping != null || joinProxy != null)
        {
            MessageBox.Show(this, "A VZ session is already active. Stop it first.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!ValidateStudio(requirePlace: false)) return;

        try
        {
            string endpoint = joinAddressBox.Text.Trim();
            int separator = endpoint.LastIndexOf(':');
            if (separator <= 0 || !int.TryParse(endpoint[(separator + 1)..], out int port) || port is < 1 or > 65535)
                throw new FormatException("Enter the host address as hostname-or-IP:port.");
            string host = endpoint[..separator].Trim('[', ']');
            string sessionId = joinSessionBox.Text.Trim().ToUpperInvariant();
            if (sessionId.Length != 32 || sessionId.Any(c => !Uri.IsHexDigit(c)))
                throw new FormatException("The session ID should be the 32-character ID provided by the host.");

            Log("Connecting to " + endpoint + "...");
            joinProxy = new JoinSessionUdpProxy(host, port, sessionId);
            joinProxy.Start();

            string parentGuid = Guid.NewGuid().ToString().ToUpperInvariant();
            string playGuid = Guid.NewGuid().ToString().ToUpperInvariant();
            studioClientProcess = LaunchStudioClient(parentGuid, playGuid);
            joinStatus.Text = "CLIENT STARTED";
            joinStatus.ForeColor = Color.LightGreen;
            Log("Roblox Studio StartClient launched through the local UDP proxy.");
        }
        catch (Exception ex)
        {
            joinProxy?.Dispose();
            joinProxy = null;
            StopChild(ref studioClientProcess);
            joinStatus.Text = "Connection failed";
            joinStatus.ForeColor = Color.OrangeRed;
            Log("Join failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "VZ join error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private Process LaunchStudioServer(string parentGuid, string playGuid)
    {
        var start = new ProcessStartInfo { FileName = studioPath!, UseShellExecute = false };
        string[] args =
        {
            "-task", "StartServer",
            "-placeId", "0", "-universeId", "0", "-placeVersion", "0",
            "-port", StudioPort.ToString(),
            "-creatorId", "0", "-creatorType", "0", "-userid", "0",
            "-numTestServerPlayersUponStartup", "0",
            "-parentSessionGuid", parentGuid,
            "-playTestSessionGuid", playGuid,
            "-instanceId", "StudioServer"
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Roblox Studio StartServer process could not be started.");
    }

    private Process LaunchStudioClient(string parentGuid, string playGuid)
    {
        var start = new ProcessStartInfo { FileName = studioPath!, UseShellExecute = false };
        string[] args =
        {
            "-task", "StartClient",
            "-placeId", "0", "-universeId", "0", "-placeVersion", "0",
            "-server", "127.0.0.1", "-port", StudioPort.ToString(),
            "-parentSessionGuid", parentGuid,
            "-playTestSessionGuid", playGuid,
            "-instanceId", "StudioPlayer"
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Roblox Studio StartClient process could not be started.");
    }

    private void CopyJoinDetails()
    {
        if (string.IsNullOrWhiteSpace(hostAddressBox.Text) || string.IsNullOrWhiteSpace(hostSessionBox.Text)) return;
        Clipboard.SetText($"Address: {hostAddressBox.Text}{Environment.NewLine}Session ID: {hostSessionBox.Text}");
        Log("Address and session ID copied.");
    }

    private void StopSession()
    {
        try { hostRelay?.Dispose(); } catch { }
        hostRelay = null;
        try { joinProxy?.Dispose(); } catch { }
        joinProxy = null;
        try { portMapping?.Dispose(); } catch { }
        portMapping = null;

        StopChild(ref studioServerProcess);
        StopChild(ref studioClientProcess);

        hostAddressBox.Clear();
        hostSessionBox.Clear();
        hostStatus.Text = "Not hosting";
        hostStatus.ForeColor = Color.Khaki;
        joinStatus.Text = "Not connected";
        joinStatus.ForeColor = Color.Khaki;
        Log("VZ session stopped and router port mapping removed.");
    }

    private void StopChild(ref Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(true);
                process.WaitForExit(2500);
            }
        }
        catch (Exception ex) { Log("Process cleanup warning: " + ex.Message); }
        process?.Dispose();
        process = null;
    }

    private void SetHostStatus(string value, Color color)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetHostStatus(value, color))); return; }
        hostStatus.Text = value;
        hostStatus.ForeColor = color;
    }

    private void Log(string message)
    {
        if (closing || logBox.IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(new Action(() => Log(message))); } catch { } return; }
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closing = true;
        try { StopSession(); } catch { }
        base.OnFormClosing(e);
    }
}
