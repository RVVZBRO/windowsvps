using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    private readonly TextBox placeBox = new() { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = "Choose a local .rbxl or .rbxlx file" };
    private readonly TextBox studioBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox hostInviteBox = new() { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox joinInviteBox = new() { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Paste a VZ4 invite from the host" };
    private readonly Label hostStatus = new() { AutoSize = true, Text = "Not hosting", ForeColor = Color.Khaki };
    private readonly Label joinStatus = new() { AutoSize = true, Text = "Not connected", ForeColor = Color.Khaki };
    private readonly Label relayStatus = new() { AutoSize = true, Text = "Checking VZ relay configuration…", ForeColor = Color.LightSkyBlue };
    private readonly TextBox logBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 16, 25), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.FixedSingle };

    private string? placePath;
    private string? studioPath;
    private HostSessionUdpGateway? hostGateway;
    private UpnpPortMapping? portMapping;
    private DirectJoinUdpProxy? joinProxy;
    private Process? studioServerProcess;
    private Process? studioClientProcess;
    private bool closing;

    public MainForm()
    {
        Text = "VZ — Multiplayer";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(820, 690);
        Size = new Size(950, 760);
        BackColor = Color.FromArgb(18, 22, 32);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 6 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        Controls.Add(root);

        root.Controls.Add(new Label { Text = "VZ", AutoSize = true, Font = new Font("Segoe UI", 34, FontStyle.Bold), ForeColor = Color.White, Margin = new Padding(0) });
        root.Controls.Add(new Label
        {
            Text = "PRIVATE STUDIO TEST SESSIONS  •  HOST OR JOIN WITH A CODE",
            AutoSize = true, ForeColor = Color.FromArgb(111, 185, 255),
            Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(2, 0, 0, 12)
        });

        var placePanel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        placePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        placePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        placePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        placePanel.Controls.Add(FieldLabel("PLACE FILE"), 0, 0);
        placePanel.Controls.Add(placeBox, 1, 0);
        placePanel.Controls.Add(StyledButton("Browse…", ChoosePlace), 2, 0);
        placePanel.Controls.Add(FieldLabel("ROBLOX STUDIO"), 0, 1);
        placePanel.Controls.Add(studioBox, 1, 1);
        placePanel.Controls.Add(StyledButton("Locate…", LocateStudio), 2, 1);
        root.Controls.Add(placePanel);

        var relayRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        relayRow.Controls.Add(new Label { Text = "VZ RELAY", AutoSize = true, ForeColor = Color.Silver, Font = new Font("Segoe UI", 9, FontStyle.Bold), Padding = new Padding(0, 3, 8, 0) });
        relayRow.Controls.Add(relayStatus);
        root.Controls.Add(relayRow);

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
        Log("VZ is ready. Players need VZ and Roblox Studio only.");
        SetRelayStatus("Automatic UDP port mapping — UPnP + public IPv4 required", Color.LightSkyBlue);
    }

    private TabPage BuildHostTab()
    {
        var page = new TabPage("HOST SESSION") { BackColor = Color.FromArgb(22, 27, 39), ForeColor = Color.White, Padding = new Padding(16) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 7, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(FieldLabel("STATUS"), 0, 0);
        layout.Controls.Add(hostStatus, 1, 0);
        layout.Controls.Add(FieldLabel("INVITE CODE"), 0, 1);
        layout.Controls.Add(hostInviteBox, 1, 1);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 12) };
        buttons.Controls.Add(PrimaryButton("HOST SESSION", StartHostSession));
        buttons.Controls.Add(StyledButton("COPY INVITE", CopyHostInvite));
        buttons.Controls.Add(StyledButton("STOP SESSION", StopSession));
        layout.Controls.Add(buttons, 1, 2);

        layout.Controls.Add(new Label
        {
            Text = "Choose a local .rbxl/.rbxlx file and click HOST SESSION. VZ starts the selected place directly as a Studio test server, launches your own player client, and requests a temporary UDP port mapping from a compatible router.",
            AutoSize = true, MaximumSize = new Size(710, 0), ForeColor = Color.Silver, Margin = new Padding(0, 8, 0, 8)
        }, 1, 3);
        layout.Controls.Add(new Label
        {
            Text = "Share the VZ5 invite with friends. They only need VZ and Roblox Studio — no tunnel app, account, VPS or copy of your map. Your router must allow UPnP and your ISP must provide a public IPv4.",
            AutoSize = true, MaximumSize = new Size(710, 0), ForeColor = Color.Khaki, Margin = new Padding(0, 8, 0, 8)
        }, 1, 4);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildJoinTab()
    {
        var page = new TabPage("JOIN SESSION") { BackColor = Color.FromArgb(22, 27, 39), ForeColor = Color.White, Padding = new Padding(16) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6, Padding = new Padding(10) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(FieldLabel("VZ INVITE"), 0, 0);
        layout.Controls.Add(joinInviteBox, 1, 0);
        layout.Controls.Add(FieldLabel("STATUS"), 0, 1);
        layout.Controls.Add(joinStatus, 1, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 12) };
        buttons.Controls.Add(PrimaryButton("JOIN SESSION", JoinSession));
        buttons.Controls.Add(StyledButton("STOP / DISCONNECT", StopSession));
        layout.Controls.Add(buttons, 1, 2);
        layout.Controls.Add(new Label
        {
            Text = "Paste the host's VZ5 invite and click JOIN SESSION. VZ connects to the host's public UDP endpoint and launches the Studio test client. No tunnel account or separate app is needed on your PC.",
            AutoSize = true, MaximumSize = new Size(710, 0), ForeColor = Color.Silver, Margin = new Padding(0, 8, 0, 8)
        }, 1, 3);
        page.Controls.Add(layout);
        return page;
    }

    private static Label FieldLabel(string value) => new()
    {
        Text = value, AutoSize = true, ForeColor = Color.Silver,
        Font = new Font("Segoe UI", 9, FontStyle.Bold),
        Margin = new Padding(0, 7, 14, 7), Anchor = AnchorStyles.Left
    };

    private Button StyledButton(string text, Action action)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, Padding = new Padding(13, 8, 13, 8),
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(48, 57, 74),
            ForeColor = Color.White, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 9, 0)
        };
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

    private bool ValidateStudio(bool requirePlace)
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

    private async void StartHostSession()
    {
        if (hostGateway != null || portMapping != null || joinProxy != null ||
            (studioServerProcess is { HasExited: false }) || (studioClientProcess is { HasExited: false }))
        {
            MessageBox.Show(this, "A VZ session is already active. Stop it first.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!ValidateStudio(requirePlace: true)) return;

        try
        {
            StopSession();
            SetHostStatus("Starting the selected Studio playtest…", Color.Khaki);
            hostInviteBox.Clear();

            byte[] roomId = RandomNumberGenerator.GetBytes(16);
            byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
            string serverParentGuid = Guid.NewGuid().ToString().ToUpperInvariant();
            string serverPlayGuid = Guid.NewGuid().ToString().ToUpperInvariant();

            Log("Starting Roblox Studio directly with the selected local place file…");
            studioServerProcess = LaunchStudioServer(serverParentGuid, serverPlayGuid);
            await Task.Delay(5000);
            if (studioServerProcess.HasExited)
                throw new InvalidOperationException("Roblox Studio's StartServer process exited. Check the selected file and Studio installation.");

            Log("Launching your own Studio playtest client…");
            studioClientProcess = LaunchStudioClient(
                Guid.NewGuid().ToString().ToUpperInvariant(),
                Guid.NewGuid().ToString().ToUpperInvariant());
            await Task.Delay(800);

            SetHostStatus("LOCAL PLAYTEST RUNNING — MAPPING ROUTER PORT…", Color.Khaki);
            try
            {
                hostGateway = new HostSessionUdpGateway(roomId, sessionKey);
                hostGateway.Start();

                Log("Checking Windows Firewall for the VZ UDP gateway…");
                await WindowsFirewall.EnsureUdpRuleAsync(HostSessionUdpGateway.PublicPort, Log);

                Log("Requesting a temporary UDP port mapping from your router…");
                portMapping = await UpnpPortMapping.CreateAsync((ushort)HostSessionUdpGateway.PublicPort);
                string publicHost = portMapping.PublicAddress.ToString();
                int publicPort = portMapping.ExternalPort;
                string endpoint = $"{publicHost}:{publicPort}";
                hostInviteBox.Text = VzInvite.Encode(publicHost, publicPort, roomId, sessionKey);

                SetHostStatus("PLAYTEST + DIRECT UDP HOSTING LIVE — SHARE INVITE", Color.LightGreen);
                SetRelayStatus("Direct UDP endpoint: " + endpoint, Color.LightGreen);
                Log("Router mapping is active at " + endpoint + ". VZ5 invite ready.");
                Log("Keep VZ and Roblox Studio running. The router mapping is removed when you stop the session.");
            }
            catch (Exception networkError)
            {
                try { portMapping?.Dispose(); } catch { }
                portMapping = null;
                try { hostGateway?.Dispose(); } catch { }
                hostGateway = null;

                SetHostStatus("LOCAL PLAYTEST ONLY — DIRECT HOSTING UNAVAILABLE", Color.Khaki);
                SetRelayStatus("Direct UDP unavailable — UPnP/public IPv4 required", Color.Khaki);
                Log("Local Studio playtest is running, but direct remote access is unavailable: " + networkError.Message);
                MessageBox.Show(this,
                    "Your selected place and your own Studio test client are running locally.\n\nRemote hosting needs a router that supports UPnP and a public IPv4 from your ISP. If your ISP uses CGNAT, this direct method cannot work.\n\n" + networkError.Message,
                    "VZ — Local playtest is running",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            Log("Host failed: " + ex.Message);
            StopSession();
            SetHostStatus("Could not start playtest", Color.OrangeRed);
            MessageBox.Show(this, ex.Message, "VZ host error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void JoinSession()
    {
        if (hostGateway != null || portMapping != null || joinProxy != null ||
            (studioServerProcess is { HasExited: false }) || (studioClientProcess is { HasExited: false }))
        {
            MessageBox.Show(this, "A VZ session is already active. Stop it first.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!ValidateStudio(requirePlace: false)) return;

        try
        {
            var invite = VzInvite.Decode(joinInviteBox.Text.Trim());
            SetJoinStatus("Connecting through the host's UDP tunnel…", Color.Khaki);
            joinProxy = new DirectJoinUdpProxy(invite.Host, invite.Port, invite.RoomId, invite.SessionKey);
            await joinProxy.StartAsync();

            Log("Host tunnel verified. Launching the Studio test client…");
            studioClientProcess = LaunchStudioClient(
                Guid.NewGuid().ToString().ToUpperInvariant(),
                Guid.NewGuid().ToString().ToUpperInvariant());
            SetJoinStatus("CONNECTED — STUDIO TEST CLIENT STARTED", Color.LightGreen);
            Log("Joined the host's VZ session. The place remains on the host PC; it was not uploaded.");
        }
        catch (Exception ex)
        {
            Log("Join failed: " + ex.Message);
            joinProxy?.Dispose();
            joinProxy = null;
            StopChild(ref studioClientProcess);
            SetJoinStatus("Could not join session", Color.OrangeRed);
            MessageBox.Show(this, ex.Message, "VZ join error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private Process LaunchStudioServer(string parentGuid, string playGuid)
    {
        if (string.IsNullOrWhiteSpace(placePath) || !File.Exists(placePath))
            throw new FileNotFoundException("The selected Roblox place file was not found.", placePath);

        var start = new ProcessStartInfo { FileName = studioPath!, UseShellExecute = false };
        string[] args =
        {
            "-task", "StartServer",
            "-localPlaceFile", placePath!,
            "-placeId", "0", "-universeId", "0", "-placeVersion", "0",
            "-port", HostSessionUdpGateway.StudioPort.ToString(),
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
            "-task", "StartClient", "-placeId", "0", "-universeId", "0", "-placeVersion", "0",
            "-server", "127.0.0.1", "-port", DirectJoinUdpProxy.LocalPort.ToString(),
            "-parentSessionGuid", parentGuid, "-playTestSessionGuid", playGuid, "-instanceId", "StudioPlayer"
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        return Process.Start(start) ?? throw new InvalidOperationException("Roblox Studio StartClient process could not be started.");
    }

    private void CopyHostInvite()
    {
        if (string.IsNullOrWhiteSpace(hostInviteBox.Text)) return;
        Clipboard.SetText(hostInviteBox.Text);
        Log("VZ invite copied.");
    }

    private void StopSession()
    {
        try { hostGateway?.Dispose(); } catch (Exception ex) { Log("Host gateway cleanup warning: " + ex.Message); }
        hostGateway = null;
        try { portMapping?.Dispose(); } catch (Exception ex) { Log("Router mapping cleanup warning: " + ex.Message); }
        portMapping = null;
        try { joinProxy?.Dispose(); } catch (Exception ex) { Log("Join proxy cleanup warning: " + ex.Message); }
        joinProxy = null;

        StopChild(ref studioServerProcess);
        StopChild(ref studioClientProcess);

        if (!closing)
        {
            hostInviteBox.Clear();
            SetHostStatus("Not hosting", Color.Khaki);
            SetJoinStatus("Not connected", Color.Khaki);
            SetRelayStatus("Automatic UDP port mapping — UPnP + public IPv4 required", Color.LightSkyBlue);
            Log("VZ session stopped.");
        }
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

    private void SetRelayStatus(string value, Color color)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetRelayStatus(value, color))); } catch { } return; }
        relayStatus.Text = value;
        relayStatus.ForeColor = color;
    }

    private void SetHostStatus(string value, Color color)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetHostStatus(value, color))); } catch { } return; }
        hostStatus.Text = value;
        hostStatus.ForeColor = color;
    }

    private void SetJoinStatus(string value, Color color)
    {
        if (InvokeRequired) { try { BeginInvoke(new Action(() => SetJoinStatus(value, color))); } catch { } return; }
        joinStatus.Text = value;
        joinStatus.ForeColor = color;
    }

    private void Log(string message)
    {
        if (closing || logBox.IsDisposed) return;
        if (InvokeRequired) { try { BeginInvoke(new Action(() => Log(message))); } catch { } return; }
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try { StopSession(); } catch { }
        closing = true;
        base.OnFormClosing(e);
    }
}

internal sealed record OpenPortEndpoint(string Host, int Port);
internal sealed record VzInviteData(string Host, int Port, byte[] RoomId, byte[] SessionKey);

internal static class VzInvite
{
    public static string Encode(string host, int port, byte[] roomId, byte[] sessionKey)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535 || roomId.Length != 16 || sessionKey.Length != 32)
            throw new ArgumentException("Invalid VZ tunnel invite data.");

        byte[] data = JsonSerializer.SerializeToUtf8Bytes(new
        {
            v = 5,
            host,
            port,
            room = Convert.ToBase64String(roomId),
            key = Convert.ToBase64String(sessionKey)
        });
        return "VZ5_" + Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static VzInviteData Decode(string invite)
    {
        if (!invite.StartsWith("VZ5_", StringComparison.Ordinal))
            throw new FormatException("This is not a VZ5 invite. Ask the host to copy a current invite.");

        string encoded = invite[4..].Replace('-', '+').Replace('_', '/');
        encoded += new string('=', (4 - encoded.Length % 4) % 4);
        using var doc = JsonDocument.Parse(Convert.FromBase64String(encoded));
        var root = doc.RootElement;
        if (!root.TryGetProperty("v", out var version) || version.GetInt32() != 5)
            throw new FormatException("Unsupported VZ invite version.");

        string host = root.GetProperty("host").GetString() ?? "";
        int port = root.GetProperty("port").GetInt32();
        byte[] roomId = Convert.FromBase64String(root.GetProperty("room").GetString() ?? "");
        byte[] sessionKey = Convert.FromBase64String(root.GetProperty("key").GetString() ?? "");
        if (string.IsNullOrWhiteSpace(host) || host.Any(char.IsWhiteSpace) ||
            Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown ||
            port is < 1 or > 65535 || roomId.Length != 16 || sessionKey.Length != 32)
            throw new FormatException("The VZ5 invite is invalid or incomplete.");

        return new VzInviteData(host, port, roomId, sessionKey);
    }
}

internal static class VzTunnelProtocol
{
    public const byte JoinData = 1;
    public const byte HostData = 2;
    public const byte KeepAlive = 3;
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("VZT5");

    public static byte[] BuildEncrypted(byte type, byte[] roomId, byte[] clientId, byte[] key, byte[] payload)
    {
        byte[] encrypted = Encrypt(key, payload);
        byte[] packet = new byte[37 + encrypted.Length];
        Buffer.BlockCopy(Magic, 0, packet, 0, 4);
        packet[4] = type;
        Buffer.BlockCopy(roomId, 0, packet, 5, 16);
        Buffer.BlockCopy(clientId, 0, packet, 21, 16);
        Buffer.BlockCopy(encrypted, 0, packet, 37, encrypted.Length);
        return packet;
    }

    public static bool TryRead(byte[] packet, byte expectedType, byte[] roomId, out byte[] clientId, out byte[] encrypted)
    {
        clientId = Array.Empty<byte>();
        encrypted = Array.Empty<byte>();
        if (packet.Length < 65 || packet[4] != expectedType ||
            !packet.AsSpan(0, 4).SequenceEqual(Magic) ||
            !CryptographicOperations.FixedTimeEquals(packet.AsSpan(5, 16), roomId))
            return false;

        clientId = packet[21..37];
        encrypted = packet[37..];
        return true;
    }

    public static byte[] Encrypt(byte[] key, byte[] payload)
    {
        if (payload.Length > 64000) throw new InvalidOperationException("Studio UDP packet is too large.");
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cipher = new byte[payload.Length];
        byte[] tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, payload, cipher, tag);

        byte[] frame = new byte[12 + cipher.Length + 16];
        Buffer.BlockCopy(nonce, 0, frame, 0, 12);
        Buffer.BlockCopy(cipher, 0, frame, 12, cipher.Length);
        Buffer.BlockCopy(tag, 0, frame, 12 + cipher.Length, 16);
        return frame;
    }

    public static byte[]? Decrypt(byte[] key, byte[] frame)
    {
        if (frame.Length < 28) return null;
        int cipherLength = frame.Length - 28;
        byte[] plain = new byte[cipherLength];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(frame.AsSpan(0, 12), frame.AsSpan(12, cipherLength), frame.AsSpan(12 + cipherLength, 16), plain);
            return plain;
        }
        catch (CryptographicException) { return null; }
    }
}

internal sealed class HostSessionUdpGateway : IDisposable
{
    public const int StudioPort = 55555;
    public const int PublicPort = 55556;
    private const int MaxPlayers = 16;
    private static readonly byte[] ReadyPayload = Encoding.ASCII.GetBytes("VZOK");

    private sealed class PeerRoute
    {
        public required byte[] ClientId { get; init; }
        public required UdpClient StudioSocket { get; init; }
        public required IPEndPoint TunnelEndpoint { get; init; }
        public DateTime LastSeenUtc;
    }

    private readonly byte[] _roomId;
    private readonly byte[] _sessionKey;
    private readonly UdpClient _publicSocket;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _peersLock = new();
    private readonly Dictionary<string, PeerRoute> _peers = new();
    private Task? _receiveTask;
    private Task? _cleanupTask;

    public HostSessionUdpGateway(byte[] roomId, byte[] sessionKey)
    {
        _roomId = roomId;
        _sessionKey = sessionKey;
        _publicSocket = new UdpClient(new IPEndPoint(IPAddress.Any, PublicPort));
    }

    public void Start()
    {
        _receiveTask = Task.Run(ReceiveLoopAsync);
        _cleanupTask = Task.Run(CleanupLoopAsync);
    }

    private async Task ReceiveLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await _publicSocket.ReceiveAsync(_stop.Token);
                byte[] clientId, encrypted;
                byte type;
                if (received.Buffer.Length < 5) continue;
                type = received.Buffer[4];
                if (type != VzTunnelProtocol.JoinData && type != VzTunnelProtocol.KeepAlive) continue;
                if (!VzTunnelProtocol.TryRead(received.Buffer, type, _roomId, out clientId, out encrypted)) continue;

                byte[]? plain = VzTunnelProtocol.Decrypt(_sessionKey, encrypted);
                if (plain == null) continue;

                string id = Convert.ToHexString(clientId);
                PeerRoute? peer;
                lock (_peersLock)
                {
                    if (!_peers.TryGetValue(id, out peer))
                    {
                        if (_peers.Count >= MaxPlayers) continue;
                        peer = new PeerRoute
                        {
                            ClientId = clientId,
                            StudioSocket = new UdpClient(AddressFamily.InterNetwork),
                            TunnelEndpoint = received.RemoteEndPoint,
                            LastSeenUtc = DateTime.UtcNow
                        };
                        _peers[id] = peer;
                        _ = Task.Run(() => ReceiveStudioRepliesAsync(peer));
                    }
                    else if (!SameEndpoint(peer.TunnelEndpoint, received.RemoteEndPoint))
                    {
                        // A joining client ID is pinned to the OpenPort UDP channel
                        // that first presented it; do not silently rebind it.
                        continue;
                    }
                    peer.LastSeenUtc = DateTime.UtcNow;
                }

                if (type == VzTunnelProtocol.KeepAlive)
                {
                    byte[] ack = VzTunnelProtocol.BuildEncrypted(
                        VzTunnelProtocol.HostData, _roomId, clientId, _sessionKey, ReadyPayload);
                    await _publicSocket.SendAsync(ack, received.RemoteEndPoint, _stop.Token);
                    continue;
                }

                if (plain.Length > 0)
                    await peer!.StudioSocket.SendAsync(plain, new IPEndPoint(IPAddress.Loopback, StudioPort), _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ReceiveStudioRepliesAsync(PeerRoute peer)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var reply = await peer.StudioSocket.ReceiveAsync(_stop.Token);
                byte[] packet = VzTunnelProtocol.BuildEncrypted(
                    VzTunnelProtocol.HostData, _roomId, peer.ClientId, _sessionKey, reply.Buffer);
                await _publicSocket.SendAsync(packet, peer.TunnelEndpoint, _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task CleanupLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(20), _stop.Token);
                List<PeerRoute> expired = new();
                lock (_peersLock)
                {
                    DateTime cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(90);
                    foreach (var entry in _peers.ToArray())
                    {
                        if (entry.Value.LastSeenUtc < cutoff)
                        {
                            _peers.Remove(entry.Key);
                            expired.Add(entry.Value);
                        }
                    }
                }
                foreach (var peer in expired) peer.StudioSocket.Dispose();
            }
        }
        catch (OperationCanceledException) { }
    }

    private static bool SameEndpoint(IPEndPoint a, IPEndPoint b) =>
        a.Port == b.Port && a.Address.Equals(b.Address);

    public void Dispose()
    {
        _stop.Cancel();
        _publicSocket.Dispose();
        lock (_peersLock)
        {
            foreach (var peer in _peers.Values) peer.StudioSocket.Dispose();
            _peers.Clear();
        }
    }
}

internal sealed class DirectJoinUdpProxy : IDisposable
{
    public const int LocalPort = HostSessionUdpGateway.StudioPort;
    private static readonly byte[] ReadyPayload = Encoding.ASCII.GetBytes("VZOK");

    private readonly IPEndPoint _remoteEndpoint;
    private readonly byte[] _roomId;
    private readonly byte[] _sessionKey;
    private readonly byte[] _clientId = RandomNumberGenerator.GetBytes(16);
    private readonly UdpClient _remote = new(AddressFamily.InterNetwork);
    private readonly UdpClient _local = new(new IPEndPoint(IPAddress.Loopback, LocalPort));
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private IPEndPoint? _studioClient;
    private Task? _remoteTask;
    private Task? _localTask;
    private Task? _heartbeatTask;

    public DirectJoinUdpProxy(string host, int port, byte[] roomId, byte[] sessionKey)
    {
        _roomId = roomId;
        _sessionKey = sessionKey;
        var addresses = Dns.GetHostAddresses(host);
        var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? throw new SocketException((int)SocketError.HostNotFound);
        _remoteEndpoint = new IPEndPoint(address, port);
    }

    public async Task StartAsync()
    {
        _remote.Connect(_remoteEndpoint);
        _remoteTask = Task.Run(ReceiveRemoteAsync);
        _localTask = Task.Run(ReceiveStudioClientAsync);
        await SendKeepAliveAsync();
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(12));
        _heartbeatTask = Task.Run(HeartbeatAsync);
    }

    private async Task SendKeepAliveAsync()
    {
        byte[] packet = VzTunnelProtocol.BuildEncrypted(
            VzTunnelProtocol.KeepAlive, _roomId, _clientId, _sessionKey, Array.Empty<byte>());
        await _sendLock.WaitAsync(_stop.Token);
        try { await _remote.SendAsync(packet, _stop.Token); }
        finally { _sendLock.Release(); }
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), _stop.Token);
                await SendKeepAliveAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ReceiveStudioClientAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await _local.ReceiveAsync(_stop.Token);
                _studioClient = received.RemoteEndPoint;
                byte[] packet = VzTunnelProtocol.BuildEncrypted(
                    VzTunnelProtocol.JoinData, _roomId, _clientId, _sessionKey, received.Buffer);
                await _sendLock.WaitAsync(_stop.Token);
                try { await _remote.SendAsync(packet, _stop.Token); }
                finally { _sendLock.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ReceiveRemoteAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await _remote.ReceiveAsync(_stop.Token);
                byte[] data = received.Buffer;
                if (!VzTunnelProtocol.TryRead(data, VzTunnelProtocol.HostData, _roomId, out var clientId, out var encrypted) ||
                    !CryptographicOperations.FixedTimeEquals(clientId, _clientId))
                    continue;

                byte[]? plain = VzTunnelProtocol.Decrypt(_sessionKey, encrypted);
                if (plain == null) continue;
                if (plain.AsSpan().SequenceEqual(ReadyPayload))
                {
                    _ready.TrySetResult(true);
                    continue;
                }

                var target = _studioClient;
                if (target != null && plain.Length > 0)
                    await _local.SendAsync(plain, target, _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { _ready.TrySetException(ex); }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _remote.Dispose();
        _local.Dispose();
        _sendLock.Dispose();
    }
}
internal static class WindowsFirewall
{
    private const string RuleName = "VZ Studio Session UDP";

    public static async Task EnsureUdpRuleAsync(int port, Action<string> log)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var check = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = $"advfirewall firewall show rule name=\"{RuleName}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using (var proc = Process.Start(check) ?? throw new InvalidOperationException("Could not check Windows Firewall rules."))
        {
            string output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (proc.ExitCode == 0 && output.Contains(RuleName, StringComparison.OrdinalIgnoreCase))
            {
                log("Windows Firewall rule already exists.");
                return;
            }
        }

        var elevated = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow protocol=UDP localport={port} profile=private,public",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        try
        {
            using var proc = Process.Start(elevated) ?? throw new InvalidOperationException("Windows did not start the firewall rule helper.");
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0) throw new InvalidOperationException("Windows Firewall did not add the inbound VZ UDP rule.");
            log("Windows Firewall now allows UDP " + port + ".");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Windows Firewall approval was cancelled. Allow the VZ UDP rule when Windows asks, then start Host again.", ex);
        }
    }
}
