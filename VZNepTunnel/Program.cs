using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
    private readonly TextBox placeBox = new() { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = "Choose a .rbxl or .rbxlx place file" };
    private readonly TextBox studioBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox relayCodeBox = new() { Multiline = true, Height = 72, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Paste your Porthole relay connection code (porthole1_…)" };
    private readonly TextBox localPortBox = new() { Text = "55555", Width = 100, Enabled = false };
    private readonly ComboBox protocolBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly CheckBox encryptBox = new() { Text = "Studio UDP mode (low latency)", Checked = true, AutoSize = true, Enabled = false };
    private readonly TextBox inviteBox = new() { Multiline = true, ReadOnly = true, Height = 72, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox joinCodeBox = new() { Multiline = true, Height = 72, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Paste a VZ Studio invite code (VZ3_…)" };
    private readonly Label hostStatus = new() { AutoSize = true, Text = "Not hosting", ForeColor = Color.Khaki };
    private readonly Label publicEndpoint = new() { AutoSize = true, Text = "No public endpoint yet", ForeColor = Color.LightSkyBlue, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
    private readonly Label joinEndpoint = new() { AutoSize = true, Text = "No session loaded", ForeColor = Color.LightSkyBlue, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
    private readonly TextBox logBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 16, 25), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.FixedSingle };
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private string? placePath;
    private string? studioPath;
    private Process? tunnelProcess;
    private string? activeEndpoint;
    private string? activeProtocol;
    private int apiPort;
    private HostSessionUdpRelay? hostRelay;
    private JoinSessionUdpProxy? joinProxy;
    private Process? studioServerProcess;
    private Process? studioClientProcess;
    private bool closing;

    public MainForm()
    {
        Text = "VZ — Tunnel Host";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 760);
        Size = new Size(980, 820);
        BackColor = Color.FromArgb(18, 22, 32);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        protocolBox.Items.AddRange(new object[] { "TCP + UDP", "TCP only", "UDP only" });
        protocolBox.SelectedIndex = 2;
        protocolBox.Enabled = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "VZ",
            AutoSize = true,
            Font = new Font("Segoe UI", 34, FontStyle.Bold),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 0, 0)
        });
        root.Controls.Add(new Label
        {
            Text = "ROBLOX STUDIO LAUNCHER  •  SELF-HOSTED TCP / UDP TUNNEL",
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
        placePanel.Controls.Add(new Label { Text = " ", AutoSize = true }, 0, 2);
        var openActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        openActions.Controls.Add(PrimaryButton("OPEN IN STUDIO", OpenPlace));
        openActions.Controls.Add(StyledButton("LOCAL TEST GUIDE", OpenGuide));
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
        Log("VZ ready. Tunnel engine: Porthole (MIT, self-hosted).");
        Log("A public relay is required. Use the Oracle Always Free setup script included with this build.");
    }

    private TabPage BuildHostTab()
    {
        var page = new TabPage("HOST SESSION") { BackColor = Color.FromArgb(22, 27, 39), ForeColor = Color.White, Padding = new Padding(16) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 10, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(FieldLabel("RELAY CONNECTION CODE"), 0, 0);
        layout.Controls.Add(relayCodeBox, 1, 0);
        layout.Controls.Add(FieldLabel("LOCAL SERVER PORT"), 0, 1);
        var localRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        localRow.Controls.Add(localPortBox);
        localRow.Controls.Add(new Label { Text = "Protocol", AutoSize = true, ForeColor = Color.Silver, Padding = new Padding(10, 6, 0, 0) });
        localRow.Controls.Add(protocolBox);
        localRow.Controls.Add(encryptBox);
        layout.Controls.Add(localRow, 1, 1);
        layout.Controls.Add(new Label
        {
            Text = "VZ starts Studio's local test server on UDP 55555 and protects it with a session token. Porthole forwards UDP 55556; friends use the invite to start matching Studio test clients.",
            AutoSize = true,
            MaximumSize = new Size(730, 0),
            ForeColor = Color.Khaki,
            Margin = new Padding(0, 7, 0, 10)
        }, 1, 2);
        layout.Controls.Add(FieldLabel("TUNNEL STATUS"), 0, 3);
        layout.Controls.Add(hostStatus, 1, 3);
        layout.Controls.Add(FieldLabel("PUBLIC ADDRESS"), 0, 4);
        layout.Controls.Add(publicEndpoint, 1, 4);
        layout.Controls.Add(FieldLabel("VZ INVITE"), 0, 5);
        layout.Controls.Add(inviteBox, 1, 5);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 10) };
        buttons.Controls.Add(PrimaryButton("START HOST SESSION", StartHostTunnel));
        buttons.Controls.Add(StyledButton("STOP TUNNEL", StopTunnel));
        buttons.Controls.Add(StyledButton("COPY INVITE", CopyInvite));
        layout.Controls.Add(buttons, 1, 6);
        layout.Controls.Add(new Label
        {
            Text = "The relay connection code is a secret: it grants access to your relay. Do not post it publicly. VZ stores the active client config locally under %LOCALAPPDATA%\\VZ\\Tunnel.",
            AutoSize = true,
            MaximumSize = new Size(740, 0),
            ForeColor = Color.Silver,
            Margin = new Padding(0, 10, 0, 0)
        }, 1, 7);
        layout.Controls.Add(new Label
        {
            Text = "Roblox compatibility note: this forwards a raw TCP/UDP port. It does not turn Studio's local test server into a Roblox-hosted server, and it cannot force ordinary Roblox Player clients to accept a custom host:port. The exact Studio test port and client protocol must support direct forwarding.",
            AutoSize = true,
            MaximumSize = new Size(740, 0),
            ForeColor = Color.Khaki,
            Margin = new Padding(0, 10, 0, 0)
        }, 1, 8);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildJoinTab()
    {
        var page = new TabPage("JOIN SESSION") { BackColor = Color.FromArgb(22, 27, 39), ForeColor = Color.White, Padding = new Padding(16) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(FieldLabel("VZ INVITE CODE"), 0, 0);
        layout.Controls.Add(joinCodeBox, 1, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 10) };
        buttons.Controls.Add(PrimaryButton("CONNECT & START CLIENT", ConnectSession));
        buttons.Controls.Add(StyledButton("CHECK INVITE", LoadInvite));
        buttons.Controls.Add(StyledButton("COPY ADDRESS", CopyJoinAddress));
        buttons.Controls.Add(StyledButton("STOP SESSION", StopTunnel));
        layout.Controls.Add(buttons, 1, 1);
        layout.Controls.Add(FieldLabel("HOST ADDRESS"), 0, 2);
        layout.Controls.Add(joinEndpoint, 1, 2);
        layout.Controls.Add(new Label
        {
            Text = "Friends do not need Porthole or a local copy of the map. Paste the host's VZ invite; VZ creates a local UDP proxy and starts Roblox Studio in StartClient mode.",
            AutoSize = true,
            MaximumSize = new Size(730, 0),
            ForeColor = Color.Silver,
            Margin = new Padding(0, 10, 0, 0)
        }, 1, 3);
        layout.Controls.Add(new Label
        {
            Text = "The host must keep VZ, Roblox Studio and the relay running. Joining uses Studio's local test-client startup flow; the place remains unpublished and VZ does not upload it to Roblox.",
            AutoSize = true,
            MaximumSize = new Size(730, 0),
            ForeColor = Color.Khaki,
            Margin = new Padding(0, 10, 0, 0)
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
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(13, 8, 13, 8), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(48, 57, 74), ForeColor = Color.White, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 9, 0) };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) => action();
        return b;
    }

    private Button PrimaryButton(string text, Action action)
    {
        var b = StyledButton(text, action);
        b.BackColor = Color.FromArgb(26, 105, 184);
        return b;
    }

    private void ChoosePlace()
    {
        using var dialog = new OpenFileDialog { Title = "Select a Roblox place", Filter = "Roblox place files (*.rbxl;*.rbxlx)|*.rbxl;*.rbxlx", CheckFileExists = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        placePath = dialog.FileName;
        placeBox.Text = placePath;
        Log("Selected local place: " + placePath);
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
            MessageBox.Show(this, "Choose an existing .rbxl or .rbxlx place file first.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private void OpenPlace()
    {
        if (!ValidateStudio()) return;
        try
        {
            LaunchPlaceForSession();
            Log("Opened the selected local place in Roblox Studio. No upload or publishing was performed.");
            MessageBox.Show(this, "Your place is opening in Studio.\n\nUse VZ > Host Session to start the shared Studio test server and create an invite. Friends use Join Session.", "VZ — Studio test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("Studio launch failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "VZ launch error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenGuide()
    {
        Process.Start(new ProcessStartInfo("https://create.roblox.com/docs/studio/testing-modes") { UseShellExecute = true });
    }

    private void StopTunnel()
    {
        try { hostRelay?.Dispose(); } catch (Exception ex) { Log("Host relay stop warning: " + ex.Message); }
        hostRelay = null;
        try { joinProxy?.Dispose(); } catch (Exception ex) { Log("Join proxy stop warning: " + ex.Message); }
        joinProxy = null;

        StopChild(ref studioServerProcess);
        StopChild(ref studioClientProcess);
        try
        {
            if (tunnelProcess is { HasExited: false })
            {
                tunnelProcess.Kill(true);
                tunnelProcess.WaitForExit(3000);
            }
        }
        catch (Exception ex) { Log("Porthole stop warning: " + ex.Message); }
        tunnelProcess?.Dispose();
        tunnelProcess = null;
        activeEndpoint = null;
        inviteBox.Clear();
        publicEndpoint.Text = "No public endpoint yet";
        joinEndpoint.Text = "No session loaded";
        SetHostStatus("Not hosting", Color.Khaki);
        Log("VZ session stopped.");
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
        catch (Exception ex) { Log("Process stop warning: " + ex.Message); }
        process?.Dispose();
        process = null;
    }

    private void LaunchPlaceForSession()
    {
        var start = new ProcessStartInfo { FileName = studioPath!, UseShellExecute = false };
        start.ArgumentList.Add(placePath!);
        Process.Start(start);
    }

    private Process LaunchStudioServer(string parentGuid, string playGuid)
    {
        var start = new ProcessStartInfo { FileName = studioPath!, UseShellExecute = false };
        string[] args =
        {
            "-task", "StartServer",
            "-placeId", "0", "-universeId", "0", "-placeVersion", "0",
            "-port", HostSessionUdpRelay.StudioServerPort.ToString(),
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
            "-server", "127.0.0.1", "-port", JoinSessionUdpProxy.LocalClientPort.ToString(),
            "-parentSessionGuid", parentGuid,
            "-playTestSessionGuid", playGuid,
            "-instanceId", "StudioPlayer"
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Roblox Studio StartClient process could not be started.");
    }

    private void SetHostStatus(string value, Color color)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => SetHostStatus(value, color))); return; }
        hostStatus.Text = value;
        hostStatus.ForeColor = color;
    }

    private void SafeLog(string message)
    {
        if (closing || IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(new Action(() => Log(message))); } catch { } }
        else Log(message);
    }

    private void Log(string message)
    {
        if (logBox.IsDisposed) return;
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        closing = true;
        try { StopTunnel(); } catch { }
        http.Dispose();
        base.OnFormClosing(e);
    }

    private sealed record BindingSnapshot(string Public, int RemotePort);
    private sealed record TunnelSnapshot(string Name, bool Up, string? Error, List<BindingSnapshot> Bindings);
    private sealed record PortholeStatus(bool Connected, List<TunnelSnapshot> Tunnels);
}
