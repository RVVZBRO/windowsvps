using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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
    private HostSessionUdpRelay? hostRelay;
    private JoinSessionUdpProxy? joinProxy;
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
        Log("VZ is ready. Players only need this app and a VZ invite.");
        _ = CheckRelayAsync();
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
            Text = "Select your .rbxl/.rbxlx file above, then click HOST SESSION. VZ opens the place in Studio, starts the local test server and connects to the shared VZ relay automatically.",
            AutoSize = true, MaximumSize = new Size(710, 0), ForeColor = Color.Silver, Margin = new Padding(0, 8, 0, 8)
        }, 1, 3);
        layout.Controls.Add(new Label
        {
            Text = "Share only the VZ4 invite. Do not share files from VZ's private data folder. Keep VZ and Roblox Studio running while friends are connected.",
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
            Text = "Paste the host's VZ4 invite and click JOIN SESSION. You do not need the host's map file, a Porthole install, a VPS account or router configuration.",
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

    private async Task CheckRelayAsync()
    {
        try
        {
            var endpoint = await RelayEndpointResolver.ResolveAsync();
            SetRelayStatus("Online address configured: " + endpoint, Color.LightGreen);
        }
        catch (Exception ex)
        {
            SetRelayStatus("Not configured yet", Color.Khaki);
            Log("Relay setup is not complete: " + ex.Message);
        }
    }

    private async void StartHostSession()
    {
        if (hostRelay != null || joinProxy != null)
        {
            MessageBox.Show(this, "A VZ session is already active. Stop it first.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!ValidateStudio(requirePlace: true)) return;

        try
        {
            StopSession();
            SetHostStatus("Starting Studio…", Color.Khaki);
            byte[] roomId = RandomNumberGenerator.GetBytes(16);
            byte[] hostKey = RandomNumberGenerator.GetBytes(16);
            byte[] sessionKey = RandomNumberGenerator.GetBytes(32);
            string parentGuid = Guid.NewGuid().ToString().ToUpperInvariant();
            string playGuid = Guid.NewGuid().ToString().ToUpperInvariant();

            Log("Opening the selected place in Studio…");
            LaunchPlace();
            await Task.Delay(2500);

            Log("Starting the Studio local test server…");
            studioServerProcess = LaunchStudioServer(parentGuid, playGuid);
            await Task.Delay(4000);
            if (studioServerProcess.HasExited)
                throw new InvalidOperationException("Roblox Studio's StartServer process exited. Check the Studio installation and selected place file.");

            Log("Starting your local Studio playtest client…");
            string hostClientParentGuid = Guid.NewGuid().ToString().ToUpperInvariant();
            string hostClientPlayGuid = Guid.NewGuid().ToString().ToUpperInvariant();
            studioClientProcess = LaunchStudioClient(hostClientParentGuid, hostClientPlayGuid);
            await Task.Delay(800);

            HostSessionUdpRelay? candidate = null;
            try
            {
                var endpoint = await RelayEndpointResolver.ResolveAsync();
                Log("Connecting to the VZ relay at " + endpoint + "…");
                candidate = new HostSessionUdpRelay(endpoint, roomId, hostKey, sessionKey);
                await candidate.StartAsync();
                hostRelay = candidate;
                hostInviteBox.Text = VzInvite.Encode(roomId, sessionKey);
                SetHostStatus("PLAYTEST RUNNING — SHARE THE INVITE", Color.LightGreen);
                Log("Your local Studio playtest is running and the VZ relay is connected. Send the VZ4 invite to friends.");
            }
            catch (Exception relayError)
            {
                try { candidate?.Dispose(); } catch { }
                hostRelay = null;
                hostInviteBox.Clear();
                SetHostStatus("LOCAL PLAYTEST ONLY — RELAY OFFLINE", Color.Khaki);
                Log("Local Studio playtest is running, but remote hosting is unavailable: " + relayError.Message);
                MessageBox.Show(this,
                    "Your selected place is now running in a local Studio playtest.\n\nFriends cannot join remotely until the shared VZ relay has been deployed and configured.\n\n" + relayError.Message,
                    "VZ — Local playtest started",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            Log("Host failed: " + ex.Message);
            StopSession();
            SetHostStatus("Could not start session", Color.OrangeRed);
            MessageBox.Show(this, ex.Message, "VZ host error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void JoinSession()
    {
        if (hostRelay != null || joinProxy != null)
        {
            MessageBox.Show(this, "A VZ session is already active. Stop it first.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!ValidateStudio(requirePlace: false)) return;

        try
        {
            var invite = VzInvite.Decode(joinInviteBox.Text.Trim());
            SetJoinStatus("Connecting to VZ relay…", Color.Khaki);
            var endpoint = await RelayEndpointResolver.ResolveAsync();
            joinProxy = new JoinSessionUdpProxy(endpoint, invite.RoomId, invite.SessionKey);
            await joinProxy.StartAsync();

            string parentGuid = Guid.NewGuid().ToString().ToUpperInvariant();
            string playGuid = Guid.NewGuid().ToString().ToUpperInvariant();
            studioClientProcess = LaunchStudioClient(parentGuid, playGuid);
            SetJoinStatus("CONNECTED — STUDIO CLIENT STARTED", Color.LightGreen);
            Log("Joined VZ relay room and launched Studio StartClient.");
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

    private void LaunchPlace()
    {
        if (string.IsNullOrWhiteSpace(placePath) || !File.Exists(placePath))
            throw new FileNotFoundException("The selected Roblox place file was not found.", placePath);

        var start = new ProcessStartInfo { FileName = studioPath!, UseShellExecute = false };
        start.ArgumentList.Add(placePath);
        _ = Process.Start(start) ?? throw new InvalidOperationException("Roblox Studio could not open the selected place.");
    }

    private Process LaunchStudioServer(string parentGuid, string playGuid)
    {
        var start = new ProcessStartInfo { FileName = studioPath!, UseShellExecute = false };
        string[] args =
        {
            "-task", "StartServer",
            "-placeId", "0", "-universeId", "0", "-placeVersion", "0",
            "-port", HostSessionUdpRelay.StudioPort.ToString(),
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
            "-server", "127.0.0.1", "-port", JoinSessionUdpProxy.LocalPort.ToString(),
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
        try { hostRelay?.Dispose(); } catch { }
        hostRelay = null;
        try { joinProxy?.Dispose(); } catch { }
        joinProxy = null;
        StopChild(ref studioServerProcess);
        StopChild(ref studioClientProcess);

        if (!closing)
        {
            hostInviteBox.Clear();
            SetHostStatus("Not hosting", Color.Khaki);
            SetJoinStatus("Not connected", Color.Khaki);
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

internal static class RelayEndpointResolver
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private const string EndpointUrl = "https://raw.githubusercontent.com/RVVZBRO/windowsvps/vz-neptunnel-dotnet/relay/endpoint.txt";

    public static async Task<IPEndPoint> ResolveAsync()
    {
        string configured = await Http.GetStringAsync(EndpointUrl);
        configured = configured.Trim();
        if (configured.Length == 0 || configured.StartsWith("#", StringComparison.Ordinal) || configured.Contains("NOT_CONFIGURED", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The VZ relay server is not deployed yet. The VZ server owner must finish the one-time Oracle VM setup.");
        int split = configured.LastIndexOf(':');
        if (split <= 0 || !int.TryParse(configured[(split + 1)..], out int port) || port is < 1 or > 65535)
            throw new FormatException("The VZ relay endpoint configuration is invalid.");
        string host = configured[..split].Trim('[', ']');
        var addresses = await Dns.GetHostAddressesAsync(host);
        var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? throw new SocketException((int)SocketError.HostNotFound);
        return new IPEndPoint(ip, port);
    }
}

internal sealed record VzInviteData(byte[] RoomId, byte[] SessionKey);

internal static class VzInvite
{
    public static string Encode(byte[] roomId, byte[] sessionKey)
    {
        if (roomId.Length != 16 || sessionKey.Length != 32) throw new ArgumentException("Invalid VZ session key material.");
        byte[] data = new byte[48];
        Buffer.BlockCopy(roomId, 0, data, 0, 16);
        Buffer.BlockCopy(sessionKey, 0, data, 16, 32);
        return "VZ4_" + Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static VzInviteData Decode(string invite)
    {
        if (!invite.StartsWith("VZ4_", StringComparison.Ordinal)) throw new FormatException("This is not a VZ4 invite.");
        string encoded = invite[4..].Replace('-', '+').Replace('_', '/');
        encoded += new string('=', (4 - encoded.Length % 4) % 4);
        byte[] data = Convert.FromBase64String(encoded);
        if (data.Length != 48) throw new FormatException("The VZ4 invite is damaged or incomplete.");
        return new VzInviteData(data[..16], data[16..48]);
    }
}

internal static class RelayProtocol
{
    public const int HeaderLength = 5;
    public const byte HostRegister = 1;
    public const byte JoinRegister = 2;
    public const byte JoinData = 3;
    public const byte HostData = 4;
    public const byte ToHost = 5;
    public const byte ToJoin = 6;
    public const byte Status = 7;
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("VZR1");

    public static byte[] Header(byte type)
    {
        var packet = new byte[HeaderLength];
        Buffer.BlockCopy(Magic, 0, packet, 0, 4);
        packet[4] = type;
        return packet;
    }

    public static bool IsPacket(byte[] packet, byte type) =>
        packet.Length >= HeaderLength && packet.AsSpan(0, 4).SequenceEqual(Magic) && packet[4] == type;

    public static byte[] Encrypt(byte[] key, byte[] payload)
    {
        if (payload.Length > 64000) throw new InvalidOperationException("Studio UDP packet is too large for the VZ relay.");
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] cipher = new byte[payload.Length];
        byte[] tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, payload, cipher, tag);
        byte[] output = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, output, 12, cipher.Length);
        Buffer.BlockCopy(tag, 0, output, 12 + cipher.Length, tag.Length);
        return output;
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

    public static byte[] BuildRegister(byte type, byte[] roomId, byte[] secret)
    {
        var packet = new byte[37];
        Buffer.BlockCopy(Header(type), 0, packet, 0, 5);
        Buffer.BlockCopy(roomId, 0, packet, 5, 16);
        Buffer.BlockCopy(secret, 0, packet, 21, 16);
        return packet;
    }

    public static byte[] BuildJoinRegister(byte[] roomId, byte[] clientId, byte[] clientKey)
    {
        var packet = new byte[53];
        Buffer.BlockCopy(Header(JoinRegister), 0, packet, 0, 5);
        Buffer.BlockCopy(roomId, 0, packet, 5, 16);
        Buffer.BlockCopy(clientId, 0, packet, 21, 16);
        Buffer.BlockCopy(clientKey, 0, packet, 37, 16);
        return packet;
    }
}

internal sealed class HostSessionUdpRelay : IDisposable
{
    public const int StudioPort = 55555;
    private readonly IPEndPoint _relayEndpoint;
    private readonly byte[] _roomId;
    private readonly byte[] _hostKey;
    private readonly byte[] _sessionKey;
    private readonly UdpClient _relay = new(AddressFamily.InterNetwork);
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, PeerRoute> _peers = new();
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _receiveTask;
    private Task? _heartbeatTask;

    private sealed record PeerRoute(byte[] ClientId, UdpClient Socket);

    public HostSessionUdpRelay(IPEndPoint relayEndpoint, byte[] roomId, byte[] hostKey, byte[] sessionKey)
    {
        _relayEndpoint = relayEndpoint;
        _roomId = roomId;
        _hostKey = hostKey;
        _sessionKey = sessionKey;
    }

    public async Task StartAsync()
    {
        _relay.Connect(_relayEndpoint);
        _receiveTask = Task.Run(ReceiveRelayAsync);
        await SendHostRegisterAsync();
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _heartbeatTask = Task.Run(HeartbeatAsync);
    }

    private async Task SendHostRegisterAsync()
    {
        await _sendLock.WaitAsync(_stop.Token);
        try { await _relay.SendAsync(RelayProtocol.BuildRegister(RelayProtocol.HostRegister, _roomId, _hostKey), _stop.Token); }
        finally { _sendLock.Release(); }
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(8), _stop.Token);
                await SendHostRegisterAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ReceiveRelayAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await _relay.ReceiveAsync(_stop.Token);
                byte[] data = received.Buffer;
                if (RelayProtocol.IsPacket(data, RelayProtocol.Status) && data.Length >= 6)
                {
                    if (data[5] == 1) _ready.TrySetResult(true);
                    else if (data[5] == 2) _ready.TrySetException(new InvalidOperationException(Encoding.UTF8.GetString(data, 6, data.Length - 6)));
                    continue;
                }
                if (!RelayProtocol.IsPacket(data, RelayProtocol.ToHost) || data.Length < 49) continue;

                byte[] clientId = data[5..21];
                string id = Convert.ToHexString(clientId);
                var route = _peers.GetOrAdd(id, peerKey =>
                {
                    var socket = new UdpClient(AddressFamily.InterNetwork);
                    var peer = new PeerRoute(clientId, socket);
                    _ = Task.Run(() => ReceiveStudioRepliesAsync(peer));
                    return peer;
                });

                byte[]? plain = RelayProtocol.Decrypt(_sessionKey, data[21..]);
                if (plain == null || plain.Length == 0) continue;
                await route.Socket.SendAsync(plain, new IPEndPoint(IPAddress.Loopback, StudioPort), _stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
        catch (Exception ex) { _ready.TrySetException(ex); }
    }

    private async Task ReceiveStudioRepliesAsync(PeerRoute peer)
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var reply = await peer.Socket.ReceiveAsync(_stop.Token);
                byte[] encrypted = RelayProtocol.Encrypt(_sessionKey, reply.Buffer);
                byte[] packet = new byte[53 + encrypted.Length];
                Buffer.BlockCopy(RelayProtocol.Header(RelayProtocol.HostData), 0, packet, 0, 5);
                Buffer.BlockCopy(_roomId, 0, packet, 5, 16);
                Buffer.BlockCopy(_hostKey, 0, packet, 21, 16);
                Buffer.BlockCopy(peer.ClientId, 0, packet, 37, 16);
                Buffer.BlockCopy(encrypted, 0, packet, 53, encrypted.Length);
                await _sendLock.WaitAsync(_stop.Token);
                try { await _relay.SendAsync(packet, _stop.Token); }
                finally { _sendLock.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _relay.Dispose();
        foreach (var peer in _peers.Values) peer.Socket.Dispose();
        _peers.Clear();
        _sendLock.Dispose();
        _stop.Dispose();
    }
}

internal sealed class JoinSessionUdpProxy : IDisposable
{
    public const int LocalPort = HostSessionUdpRelay.StudioPort;
    private readonly IPEndPoint _relayEndpoint;
    private readonly byte[] _roomId;
    private readonly byte[] _sessionKey;
    private readonly byte[] _clientId = RandomNumberGenerator.GetBytes(16);
    private readonly byte[] _clientKey = RandomNumberGenerator.GetBytes(16);
    private readonly UdpClient _relay = new(AddressFamily.InterNetwork);
    private readonly UdpClient _local = new(new IPEndPoint(IPAddress.Loopback, LocalPort));
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IPEndPoint? _studioClient;
    private Task? _relayReceiveTask;
    private Task? _localReceiveTask;
    private Task? _heartbeatTask;

    public JoinSessionUdpProxy(IPEndPoint relayEndpoint, byte[] roomId, byte[] sessionKey)
    {
        _relayEndpoint = relayEndpoint;
        _roomId = roomId;
        _sessionKey = sessionKey;
    }

    public async Task StartAsync()
    {
        _relay.Connect(_relayEndpoint);
        _relayReceiveTask = Task.Run(ReceiveRelayAsync);
        await SendRegisterAsync();
        await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _localReceiveTask = Task.Run(ReceiveStudioClientAsync);
        _heartbeatTask = Task.Run(HeartbeatAsync);
    }

    private async Task SendRegisterAsync()
    {
        await _sendLock.WaitAsync(_stop.Token);
        try { await _relay.SendAsync(RelayProtocol.BuildJoinRegister(_roomId, _clientId, _clientKey), _stop.Token); }
        finally { _sendLock.Release(); }
    }

    private async Task HeartbeatAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(8), _stop.Token);
                await SendRegisterAsync();
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
                var packet = await _local.ReceiveAsync(_stop.Token);
                _studioClient = packet.RemoteEndPoint;
                byte[] encrypted = RelayProtocol.Encrypt(_sessionKey, packet.Buffer);
                byte[] data = new byte[53 + encrypted.Length];
                Buffer.BlockCopy(RelayProtocol.Header(RelayProtocol.JoinData), 0, data, 0, 5);
                Buffer.BlockCopy(_roomId, 0, data, 5, 16);
                Buffer.BlockCopy(_clientId, 0, data, 21, 16);
                Buffer.BlockCopy(_clientKey, 0, data, 37, 16);
                Buffer.BlockCopy(encrypted, 0, data, 53, encrypted.Length);
                await _sendLock.WaitAsync(_stop.Token);
                try { await _relay.SendAsync(data, _stop.Token); }
                finally { _sendLock.Release(); }
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ReceiveRelayAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var received = await _relay.ReceiveAsync(_stop.Token);
                byte[] data = received.Buffer;
                if (RelayProtocol.IsPacket(data, RelayProtocol.Status) && data.Length >= 6)
                {
                    if (data[5] == 1) _ready.TrySetResult(true);
                    else if (data[5] == 2) _ready.TrySetException(new InvalidOperationException(Encoding.UTF8.GetString(data, 6, data.Length - 6)));
                    continue;
                }
                if (!RelayProtocol.IsPacket(data, RelayProtocol.ToJoin) || data.Length <= 5) continue;
                var target = _studioClient;
                if (target == null) continue;
                byte[]? plain = RelayProtocol.Decrypt(_sessionKey, data[5..]);
                if (plain == null || plain.Length == 0) continue;
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
        _relay.Dispose();
        _local.Dispose();
        _sendLock.Dispose();
        _stop.Dispose();
    }
}
