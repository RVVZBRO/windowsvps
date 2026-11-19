using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.Http.Json;
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
    private readonly TextBox placeBox = new() { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = "Choose a .rbxl or .rbxlx place file" };
    private readonly TextBox studioBox = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly TextBox relayCodeBox = new() { Multiline = true, Height = 72, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Paste your Porthole relay connection code (porthole1_…)" };
    private readonly TextBox localPortBox = new() { Text = "53640", Width = 100 };
    private readonly ComboBox protocolBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly CheckBox encryptBox = new() { Text = "Encrypt tunnel data", Checked = true, AutoSize = true };
    private readonly TextBox inviteBox = new() { Multiline = true, ReadOnly = true, Height = 72, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox joinCodeBox = new() { Multiline = true, Height = 72, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Paste a VZ invite code (VZ2_…)" };
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
        protocolBox.SelectedIndex = 0;

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
            Text = "Set this to the local TCP/UDP port of the server you want to expose. Studio's Test tab must be running a server on that port.",
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
        buttons.Controls.Add(PrimaryButton("START HOST TUNNEL", StartHostTunnel));
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
        buttons.Controls.Add(PrimaryButton("LOAD INVITE", LoadInvite));
        buttons.Controls.Add(StyledButton("COPY ADDRESS", CopyJoinAddress));
        layout.Controls.Add(buttons, 1, 1);
        layout.Controls.Add(FieldLabel("HOST ADDRESS"), 0, 2);
        layout.Controls.Add(joinEndpoint, 1, 2);
        layout.Controls.Add(new Label
        {
            Text = "The invite contains the relay's public endpoint, not the relay's private connection code. Friends do not need Porthole installed to connect to a public TCP/UDP service. They do need a compatible game client that accepts this endpoint.",
            AutoSize = true,
            MaximumSize = new Size(730, 0),
            ForeColor = Color.Silver,
            Margin = new Padding(0, 10, 0, 0)
        }, 1, 3);
        layout.Controls.Add(new Label
        {
            Text = "For Roblox Studio specifically, this is a network endpoint only. Studio's local multi-client test workflow does not expose a supported public join dialog, so VZ cannot guarantee remote Studio clients can join through this generic tunnel.",
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

    private bool ValidateStudio()
    {
        if (string.IsNullOrWhiteSpace(placePath) || !File.Exists(placePath))
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
            var start = new ProcessStartInfo { FileName = studioPath!, UseShellExecute = false };
            start.ArgumentList.Add("--task");
            start.ArgumentList.Add("EditFile");
            start.ArgumentList.Add("--localPlaceFile");
            start.ArgumentList.Add(placePath!);
            Process.Start(start);
            Log("Opened the selected local place in Roblox Studio. No upload or publishing was performed.");
            MessageBox.Show(this, "Your place is opening in Studio.\n\nTo start local multiplayer testing, open the Test tab and use Start Server / Start Player. Enter the actual listening port in VZ's Host tab before starting the tunnel.", "VZ — Studio test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("Studio launch failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "VZ launch error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void StartHostTunnel()
    {
        if (tunnelProcess is { HasExited: false })
        {
            MessageBox.Show(this, "A VZ tunnel is already running. Stop it before starting another.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var code = relayCodeBox.Text.Trim();
        if (!code.StartsWith("porthole1_", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Paste the Porthole relay connection code. It starts with porthole1_.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!ushort.TryParse(localPortBox.Text.Trim(), out var localPort) || localPort == 0)
        {
            MessageBox.Show(this, "Enter a valid local port from 1 to 65535.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string protocol = protocolBox.SelectedIndex switch { 1 => "tcp", 2 => "udp", _ => "both" };
        try
        {
            var info = DecodePortholeCode(code);
            string porthole = await PreparePortholeAsync();
            apiPort = GetFreeTcpPort();
            string webBind = $"127.0.0.1:{apiPort}";
            var start = new ProcessStartInfo
            {
                FileName = porthole,
                WorkingDirectory = Path.GetDirectoryName(porthole)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--no-banner");
            start.ArgumentList.Add("join");
            start.ArgumentList.Add(code);
            start.ArgumentList.Add("--web-bind");
            start.ArgumentList.Add(webBind);

            StopTunnel();
            var configPath = Path.Combine(Path.GetDirectoryName(porthole)!, "porthole-client.toml");
            try { if (File.Exists(configPath)) File.Delete(configPath); } catch { }
            tunnelProcess = new Process { StartInfo = start, EnableRaisingEvents = true };
            tunnelProcess.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) SafeLog("[Porthole] " + e.Data); };
            tunnelProcess.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) SafeLog("[Porthole] " + e.Data); };
            if (!tunnelProcess.Start()) throw new InvalidOperationException("Porthole could not be started.");
            tunnelProcess.BeginOutputReadLine();
            tunnelProcess.BeginErrorReadLine();
            Log("Connecting to relay " + info.Host + ":" + info.Port + " …");
            SetHostStatus("Connecting to relay…", Color.Khaki);

            await WaitForApiAsync(tunnelProcess, webBind, TimeSpan.FromSeconds(45));
            var before = await GetStatusAsync();
            if (!before.Connected) throw new InvalidOperationException("The relay control connection did not become ready. Check the relay address, connection code, firewall and relay service.");

            var request = new
            {
                name = "vz-session",
                proto = protocol,
                local = $"127.0.0.1:{localPort}",
                remote_port = 0,
                encrypted = encryptBox.Checked,
                udp_mtu = 1200
            };
            using var response = await http.PostAsJsonAsync($"http://{webBind}/api/tunnels", request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Porthole rejected the tunnel: " + body);

            TunnelSnapshot? ready = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            string? relayError = null;
            while (DateTime.UtcNow < deadline)
            {
                if (tunnelProcess == null || tunnelProcess.HasExited) throw new InvalidOperationException("Porthole exited while creating the tunnel.");
                var snapshot = await GetStatusAsync();
                ready = snapshot.Tunnels.FirstOrDefault(t => t.Name == "vz-session");
                if (ready?.Error is { Length: > 0 } err) relayError = err;
                if (ready is { Up: true } && ready.Bindings.Count > 0) break;
                await Task.Delay(1000);
            }
            if (ready is not { Up: true } || ready.Bindings.Count == 0)
                throw new TimeoutException(relayError ?? "The relay did not assign a public port within 60 seconds. Check that the relay allows the selected protocol and port range.");

            activeEndpoint = ready.Bindings[0].Public;
            activeProtocol = protocol;
            publicEndpoint.Text = activeEndpoint;
            var invite = EncodeVzInvite(activeEndpoint, protocol);
            inviteBox.Text = invite;
            SetHostStatus("Tunnel is live", Color.LightGreen);
            Log("Public tunnel is live: " + activeEndpoint + " (" + protocol.ToUpperInvariant() + ")");
            Log("VZ invite generated. Share the VZ2_ code, never the porthole1_ relay code.");
            Log("Local destination: 127.0.0.1:" + localPort);
        }
        catch (Exception ex)
        {
            SetHostStatus("Tunnel failed: " + ex.Message, Color.OrangeRed);
            Log("Host failed: " + ex);
            StopTunnel();
            MessageBox.Show(this, ex.Message, "VZ tunnel error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private Task<string> PreparePortholeAsync()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "porthole.exe");
        if (!File.Exists(bundled)) throw new FileNotFoundException("The bundled Porthole tunnel engine is missing. Re-download the complete VZ build ZIP and keep the tools folder next to VZ.exe.", bundled);
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VZ", "Tunnel");
        Directory.CreateDirectory(home);
        var target = Path.Combine(home, "porthole.exe");
        if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(bundled).Length)
            File.Copy(bundled, target, true);
        return Task.FromResult(target);
    }

    private async Task WaitForApiAsync(Process process, string webBind, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException("Porthole exited before its local control API started.");
            try
            {
                using var response = await http.GetAsync($"http://{webBind}/api/status");
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(500);
        }
        throw new TimeoutException("Porthole's local control API did not start.");
    }

    private async Task<PortholeStatus> GetStatusAsync()
    {
        using var response = await http.GetAsync($"http://127.0.0.1:{apiPort}/api/status");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        var tunnels = new List<TunnelSnapshot>();
        if (root.TryGetProperty("tunnels", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var bindings = new List<BindingSnapshot>();
                if (item.TryGetProperty("bindings", out var bindingItems) && bindingItems.ValueKind == JsonValueKind.Array)
                {
                    foreach (var binding in bindingItems.EnumerateArray())
                    {
                        bindings.Add(new BindingSnapshot(
                            binding.TryGetProperty("public", out var pub) ? pub.GetString() ?? "" : "",
                            binding.TryGetProperty("remote_port", out var rp) && rp.TryGetInt32(out var port) ? port : 0));
                    }
                }
                tunnels.Add(new TunnelSnapshot(
                    item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    item.TryGetProperty("up", out var up) && up.GetBoolean(),
                    item.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String ? err.GetString() : null,
                    bindings));
            }
        }
        return new PortholeStatus(root.TryGetProperty("connected", out var connected) && connected.GetBoolean(), tunnels);
    }

    private static (string Host, int Port) DecodePortholeCode(string code)
    {
        string encoded = code["porthole1_".Length..].Replace('-', '+').Replace('_', '/');
        encoded += new string('=', (4 - encoded.Length % 4) % 4);
        using var doc = JsonDocument.Parse(Convert.FromBase64String(encoded));
        string host = doc.RootElement.GetProperty("host").GetString() ?? throw new FormatException("Relay code has no host.");
        int port = doc.RootElement.GetProperty("port").GetInt32();
        if (port is < 1 or > 65535) throw new FormatException("Relay code has an invalid control port.");
        return (host, port);
    }

    private static string EncodeVzInvite(string endpoint, string protocol)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { v = 2, endpoint, protocol });
        return "VZ2_" + Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static (string Endpoint, string Protocol) DecodeVzInvite(string invite)
    {
        if (!invite.StartsWith("VZ2_", StringComparison.Ordinal)) throw new FormatException("This is not a VZ2 invite.");
        string encoded = invite[4..].Replace('-', '+').Replace('_', '/');
        encoded += new string('=', (4 - encoded.Length % 4) % 4);
        using var doc = JsonDocument.Parse(Convert.FromBase64String(encoded));
        var root = doc.RootElement;
        if (!root.TryGetProperty("v", out var version) || version.GetInt32() != 2) throw new FormatException("Unsupported VZ invite version.");
        string endpoint = root.GetProperty("endpoint").GetString() ?? "";
        string protocol = root.GetProperty("protocol").GetString() ?? "";
        if (!endpoint.Contains(':') || protocol is not ("tcp" or "udp" or "both")) throw new FormatException("The VZ invite is missing a valid endpoint or protocol.");
        return (endpoint, protocol);
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private void LoadInvite()
    {
        try
        {
            var (endpoint, protocol) = DecodeVzInvite(joinCodeBox.Text.Trim());
            activeEndpoint = endpoint;
            activeProtocol = protocol;
            joinEndpoint.Text = endpoint + "  •  " + protocol.ToUpperInvariant();
            Log("Loaded VZ invite endpoint: " + endpoint + " (" + protocol + ")");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Invalid VZ invite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopyInvite()
    {
        if (string.IsNullOrWhiteSpace(inviteBox.Text)) return;
        Clipboard.SetText(inviteBox.Text);
        Log("VZ invite copied to clipboard.");
    }

    private void CopyJoinAddress()
    {
        if (string.IsNullOrWhiteSpace(activeEndpoint)) return;
        Clipboard.SetText(activeEndpoint);
        Log("Public address copied to clipboard.");
    }

    private void OpenGuide()
    {
        Process.Start(new ProcessStartInfo("https://create.roblox.com/docs/studio/testing-modes") { UseShellExecute = true });
    }

    private void StopTunnel()
    {
        try
        {
            if (tunnelProcess is { HasExited: false })
            {
                tunnelProcess.Kill(true);
                tunnelProcess.WaitForExit(3000);
            }
        }
        catch (Exception ex) { Log("Tunnel stop warning: " + ex.Message); }
        tunnelProcess?.Dispose();
        tunnelProcess = null;
        activeEndpoint = null;
        inviteBox.Clear();
        publicEndpoint.Text = "No public endpoint yet";
        SetHostStatus("Not hosting", Color.Khaki);
        Log("Tunnel stopped.");
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
        try
        {
            if (tunnelProcess is { HasExited: false }) tunnelProcess.Kill(true);
            tunnelProcess?.Dispose();
        }
        catch { }
        http.Dispose();
        base.OnFormClosing(e);
    }

    private sealed record BindingSnapshot(string Public, int RemotePort);
    private sealed record TunnelSnapshot(string Name, bool Up, string? Error, List<BindingSnapshot> Bindings);
    private sealed record PortholeStatus(bool Connected, List<TunnelSnapshot> Tunnels);
}
