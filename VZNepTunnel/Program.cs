using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using DiscordRPC;

namespace VZNepTunnel;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(18, 22, 31), ForeColor = Color.Gainsboro };
    readonly TextBox address = new() { Width = 300, PlaceholderText = "Friend's IP address" };
    readonly TextBox roomCode = new() { Width = 300, PlaceholderText = "Shared room code (8+ characters)" };
    readonly TextBox portwarpTunnel = new() { Width = 300, PlaceholderText = "Tunnel name from Portwarp dashboard" };
    readonly TextBox portwarpStatus = new() { Width = 520, ReadOnly = true, Text = "Portwarp is not configured yet." };
    readonly TextBox serverExe = new() { Width = 430, PlaceholderText = "Dedicated game-server executable (.exe)" };
    readonly TextBox serverArgs = new() { Width = 430, PlaceholderText = "Optional server arguments; {map} is replaced with selected map path" };
    readonly TextBox publicEndpoint = new() { Width = 430, PlaceholderText = "Public address from Portwarp dashboard, e.g. game.example.pwrp.cc:12345" };
    readonly TextBox inviteCode = new() { Width = 520, ReadOnly = true };
    readonly TextBox inviteInput = new() { Width = 520, PlaceholderText = "VZ1- invite code" };
    readonly Label joinStatus = new() { AutoSize = true, ForeColor = Color.LightGreen };
    readonly TextBox clientExe = new() { Width = 430, PlaceholderText = "Game client executable (.exe)" };
    readonly TextBox clientArgs = new() { Width = 430, PlaceholderText = "Optional client arguments; use {host} and {port}" };
    Process? gameServerProcess;
    
    
    readonly ComboBox relayRole = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly NumericUpDown hostPort = new() { Minimum = 1024, Maximum = 65535, Value = 53640, Width = 100 };
    readonly NumericUpDown joinPort = new() { Minimum = 1024, Maximum = 65535, Value = 53640, Width = 100 };
    readonly Label endpoint = new() { AutoSize = true, ForeColor = Color.LightGreen };
    readonly Label hostStatus = new() { AutoSize = true, ForeColor = Color.Orange, Font = new Font("Segoe UI", 12, FontStyle.Bold), Text = "NOT HOSTED" };
    readonly Label configuredPort = new() { AutoSize = true, ForeColor = Color.Gainsboro };
    readonly ListBox imports = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(18,22,31), ForeColor = Color.White };
    UdpClient? host;
    CancellationTokenSource? hostCts;
    string? mapPath;
    string? studioPath;
    readonly ComboBox themeChoice = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox appearanceChoice = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly CheckBox discordEnabled = new() { Text = "Show VZ on Discord (Rich Presence)", AutoSize = true };
    readonly TextBox discordAppId = new() { Width = 300, PlaceholderText = "Discord Application ID" };
    readonly Label discordState = new() { AutoSize = true, Text = "Discord presence is off." };
    DiscordRpcClient? discordClient;
    readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VZ NepTunnel", "settings.json");
    string selectedTheme = "Obsidian";
    string appearance = "System";

    public MainForm()
    {
        Text = "VZ NepTunnel";
        Width = 900; Height = 650; MinimumSize = new Size(760, 540);
        BackColor = Color.FromArgb(26, 31, 43); ForeColor = Color.White;
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(HomePage());
        tabs.TabPages.Add(JoinPage());
        tabs.TabPages.Add(ImporterPage());
        tabs.TabPages.Add(LogsPage());
        tabs.TabPages.Add(PortwarpPage());
        tabs.TabPages.Add(SettingsPage());
        Controls.Add(tabs);
        Log("VZ NepTunnel started. Portwarp is the tunnel provider; LAN-IP joining is disabled.");
        LoadSettings();
        discordEnabled.CheckedChanged += (_, _) => ApplyDiscord();
        FormClosing += (_, _) => { StopHost(); StopDiscord(); SaveSettings(); };
    }

    TabPage Page(string title) { var p = new TabPage(title) { BackColor = BackColor, ForeColor = ForeColor, Padding = new Padding(18) }; return p; }
    System.Windows.Forms.Button Btn(string text, Action action) { var b = new System.Windows.Forms.Button { Text = text, AutoSize = true, Padding = new Padding(12,7,12,7), BackColor = Color.FromArgb(49,93,160), ForeColor = Color.White, FlatStyle = FlatStyle.Flat }; b.Click += (_,_) => action(); return b; }
    Label Heading(string s) => new() { Text = s, AutoSize = true, Font = new Font("Segoe UI", 19, FontStyle.Bold), ForeColor = Color.White, Margin = new Padding(0,0,0,16) };
    void Log(string s) { if (log.InvokeRequired) { log.BeginInvoke(new Action(() => Log(s))); return; } log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}"); }

    TabPage HomePage()
    {
        var p = Page("Host");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        flow.Controls.Add(Heading("VZ  //  HOST A GAME"));
        flow.Controls.Add(new Label { Text = "Choose your local map, then select the dedicated server program that actually runs this game. VZ cannot run an .rbxl file by itself.", AutoSize = true, MaximumSize = new Size(780,0) });
        flow.Controls.Add(Btn("Choose map…", () => ChooseMap()));
        flow.Controls.Add(new Label { Text = "Selected map: " + (mapPath == null ? "None" : Path.GetFileName(mapPath)), AutoSize = true });
        flow.Controls.Add(new Label { Text = "Game server executable:", AutoSize = true });
        flow.Controls.Add(serverExe);
        flow.Controls.Add(Btn("Browse server executable…", () => PickExecutable(serverExe)));
        flow.Controls.Add(new Label { Text = "Server arguments (optional):", AutoSize = true });
        flow.Controls.Add(serverArgs);
        flow.Controls.Add(new Label { Text = "Local server port (must match your Portwarp tunnel):", AutoSize = true });
        flow.Controls.Add(hostPort);
        flow.Controls.Add(new Label { Text = "Portwarp tunnel name (create it once in your dashboard):", AutoSize = true });
        flow.Controls.Add(portwarpTunnel);
        flow.Controls.Add(new Label { Text = "Public address from Portwarp dashboard:", AutoSize = true });
        flow.Controls.Add(publicEndpoint);
        flow.Controls.Add(hostStatus);
        flow.Controls.Add(Btn("HOST GAME  •  Start server + Portwarp", async () => await StartGameSession()));
        flow.Controls.Add(new Label { Text = "Share this VZ invite with friends:", AutoSize = true });
        flow.Controls.Add(inviteCode);
        flow.Controls.Add(Btn("Create / refresh invite from public address", () => CreateInvite()));
        flow.Controls.Add(Btn("Copy invite code", () => { if (!string.IsNullOrWhiteSpace(inviteCode.Text)) { Clipboard.SetText(inviteCode.Text); Log("Invite code copied."); } }));
        flow.Controls.Add(Btn("STOP GAME", () => StopGameSession()));
        flow.Controls.Add(new Label { Text = "Important: Portwarp forwards traffic to your local server. The selected server executable must support the game protocol and listen on the configured port. Roblox Studio's local test session is not an Internet-capable standalone server.", AutoSize = true, MaximumSize = new Size(780,0), ForeColor = Color.Khaki, Margin = new Padding(0,16,0,0) });
        p.Controls.Add(flow); return p;
    }

    TabPage JoinPage()
    {
        var p = Page("Join");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        flow.Controls.Add(Heading("Join a VZ game"));
        flow.Controls.Add(new Label { Text = "Paste the VZ invite code shared by the host. VZ decodes the public Portwarp endpoint; it does not reveal the host's home IP.", AutoSize = true, MaximumSize = new Size(760,0) });
        flow.Controls.Add(inviteInput);
        flow.Controls.Add(new Label { Text = "Game client executable (if your game requires one):", AutoSize = true });
        flow.Controls.Add(clientExe);
        flow.Controls.Add(Btn("Browse game client…", () => PickExecutable(clientExe)));
        flow.Controls.Add(new Label { Text = "Client arguments (optional; use {host} and {port}):", AutoSize = true });
        flow.Controls.Add(clientArgs);
        flow.Controls.Add(Btn("JOIN GAME", () => JoinGameSession()));
        flow.Controls.Add(joinStatus);
        flow.Controls.Add(new Label { Text = "VZ invite codes contain the public endpoint, not a matchmaking account or access-control token. Your game server must enforce its own player authentication.", AutoSize = true, MaximumSize = new Size(760,0), ForeColor = Color.Khaki, Margin = new Padding(0,16,0,0) });
        p.Controls.Add(flow); return p;
    }

    TabPage ImporterPage()
    {
        var p = Page("RBXM Importer");
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        split.RowStyles.Add(new RowStyle(SizeType.AutoSize)); split.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        top.Controls.Add(Heading("RBXM assets"));
        top.Controls.Add(Btn("Add .rbxm files…", () => AddAssets()));
        top.Controls.Add(Btn("Open selected file", () => OpenSelectedAsset()));
        top.Controls.Add(Btn("Install Studio importer plugin", () => InstallPlugin()));
        split.Controls.Add(top,0,0); split.Controls.Add(imports,0,1); p.Controls.Add(split); return p;
    }

    TabPage LogsPage() { var p = Page("Activity"); p.Controls.Add(log); return p; }


    TabPage PortwarpPage()
    {
        var p = Page("Portwarp");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        flow.Controls.Add(Heading("VZ  //  PORTWARP CONNECTION"));
        flow.Controls.Add(new Label { Text = "VZ uses Portwarp to expose a local game-server port to friends. No router port forwarding or home-IP sharing is needed.", AutoSize = true, MaximumSize = new Size(760,0) });
        flow.Controls.Add(portwarpStatus);
        flow.Controls.Add(Btn("Install / Update Portwarp CLI", async () => await InstallPortwarp()));
        flow.Controls.Add(Btn("Sign in to Portwarp", async () => await PortwarpLogin()));
        flow.Controls.Add(Btn("Open Portwarp dashboard", () => Process.Start(new ProcessStartInfo("https://portwarp.com/dashboard") { UseShellExecute = true })));
        flow.Controls.Add(new Label { Text = "Create and enable a UDP or TCP tunnel in the Portwarp dashboard once. Set its local port to the same port your game server uses.", AutoSize = true, MaximumSize = new Size(760,0), ForeColor = Color.Khaki });
        flow.Controls.Add(new Label { Text = "Tunnel name:", AutoSize = true }); flow.Controls.Add(portwarpTunnel);
        flow.Controls.Add(Btn("Refresh Portwarp tunnels", async () => await RefreshPortwarp()));
        flow.Controls.Add(Btn("Connect selected tunnel", async () => await ConnectPortwarp()));
        flow.Controls.Add(Btn("Stop Portwarp tunnel", async () => await StopPortwarp()));
        flow.Controls.Add(new Label { Text = "The public address is shown in your Portwarp dashboard. VZ will not claim a playable session unless a compatible game server is running on the selected local port.", AutoSize = true, MaximumSize = new Size(760,0), ForeColor = Color.Khaki, Margin = new Padding(0,18,0,0) });
        p.Controls.Add(flow); return p;
    }

    void PickExecutable(TextBox target)
    {
        using var d = new OpenFileDialog { Filter = "Windows applications (*.exe)|*.exe|All files (*.*)|*.*", CheckFileExists = true };
        if (d.ShowDialog() == DialogResult.OK) target.Text = d.FileName;
    }

    async Task StartGameSession()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(serverExe.Text) || !File.Exists(serverExe.Text))
                throw new InvalidOperationException("Choose a real dedicated game-server .exe first. A Roblox .rbxl map file is not a server executable.");
            if (string.IsNullOrWhiteSpace(portwarpTunnel.Text))
                throw new InvalidOperationException("Enter the exact Portwarp tunnel name configured in your dashboard.");
            if (gameServerProcess is { HasExited: false })
                throw new InvalidOperationException("A VZ game server is already running.");
            var psi = new ProcessStartInfo { FileName = serverExe.Text, WorkingDirectory = Path.GetDirectoryName(serverExe.Text)!, UseShellExecute = false };
            string args = serverArgs.Text.Replace("{map}", mapPath ?? "", StringComparison.OrdinalIgnoreCase).Replace("{port}", ((int)hostPort.Value).ToString());
            if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args;
            gameServerProcess = Process.Start(psi) ?? throw new InvalidOperationException("Windows could not start the selected game server.");
            hostStatus.Text = "SERVER STARTED — CHECKING PORTWARP";
            hostStatus.ForeColor = Color.Khaki;
            Log("Game server process started: " + Path.GetFileName(serverExe.Text));
            await Task.Delay(1500);
            if (gameServerProcess.HasExited) throw new InvalidOperationException("The game server exited immediately. Check its arguments and logs.");
            string output = await PortwarpManager.RunAsync("connect " + QuoteArg(portwarpTunnel.Text.Trim()) + " --detach", TimeSpan.FromSeconds(30), line => Log("Portwarp: " + line));
            Log("Portwarp connect command accepted: " + output.Trim());
            if (!TryParseEndpoint(publicEndpoint.Text.Trim(), out var hostName, out var publicPort))
            {
                hostStatus.Text = "SERVER RUNNING — PORTWARP CONNECTED; ADD PUBLIC ADDRESS";
                hostStatus.ForeColor = Color.Khaki;
                MessageBox.Show("The local server and Portwarp connect command started. Copy the public hostname:port from your Portwarp dashboard into the Public address field, then click HOST GAME again to create the invite. Do not share your home IP.", "VZ — Public endpoint needed");
                return;
            }
            inviteCode.Text = InviteCodec.Create(hostName, publicPort);
            hostStatus.Text = "HOSTING — SHARE YOUR VZ INVITE";
            hostStatus.ForeColor = Color.LightGreen;
            Log("VZ invite created for Portwarp public endpoint " + hostName + ":" + publicPort);
        }
        catch (Exception ex)
        {
            hostStatus.Text = "HOST FAILED: " + ex.Message;
            hostStatus.ForeColor = Color.OrangeRed;
            Log(hostStatus.Text);
            MessageBox.Show(ex.Message, "VZ Host");
        }
    }

    void CreateInvite()
    {
        try
        {
            if (gameServerProcess is not { HasExited: false })
                throw new InvalidOperationException("Start the game server first.");
            if (!TryParseEndpoint(publicEndpoint.Text.Trim(), out var hostName, out var publicPort))
                throw new FormatException("Enter the public hostname:port shown by Portwarp, for example game.example:12345.");
            inviteCode.Text = InviteCodec.Create(hostName, publicPort);
            hostStatus.Text = "HOSTING — INVITE READY";
            hostStatus.ForeColor = Color.LightGreen;
            Log("Invite created for " + hostName + ":" + publicPort);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "VZ Invite"); }
    }

    void StopGameSession()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(portwarpTunnel.Text) && File.Exists(PortwarpManager.ExecutablePath))
                _ = PortwarpManager.RunAsync("stop " + QuoteArg(portwarpTunnel.Text.Trim()), TimeSpan.FromSeconds(15), line => Log("Portwarp: " + line));
            if (gameServerProcess is { HasExited: false }) gameServerProcess.Kill(true);
            gameServerProcess?.Dispose(); gameServerProcess = null;
            inviteCode.Clear();
            hostStatus.Text = "SESSION STOPPED"; hostStatus.ForeColor = Color.Gainsboro;
            Log("VZ session stopped.");
        }
        catch (Exception ex) { Log("Stop session: " + ex.Message); MessageBox.Show(ex.Message, "VZ Stop"); }
    }

    void JoinGameSession()
    {
        try
        {
            var (hostName, port) = InviteCodec.Read(inviteInput.Text.Trim());
            if (!string.IsNullOrWhiteSpace(clientExe.Text))
            {
                if (!File.Exists(clientExe.Text)) throw new FileNotFoundException("The selected game client executable was not found.");
                var psi = new ProcessStartInfo { FileName = clientExe.Text, WorkingDirectory = Path.GetDirectoryName(clientExe.Text)!, UseShellExecute = false };
                string args = clientArgs.Text.Replace("{host}", hostName, StringComparison.OrdinalIgnoreCase).Replace("{port}", port.ToString());
                if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args;
                Process.Start(psi);
                joinStatus.Text = "Client launched for " + hostName + ":" + port;
            }
            else
            {
                Clipboard.SetText(hostName + ":" + port);
                joinStatus.Text = "Endpoint copied: " + hostName + ":" + port + " — configure your game client to connect to it.";
            }
            Log("Invite decoded: " + hostName + ":" + port);
        }
        catch (Exception ex) { joinStatus.Text = "Join failed: " + ex.Message; Log(joinStatus.Text); MessageBox.Show(ex.Message, "VZ Join"); }
    }

    static bool TryParseEndpoint(string value, out string hostName, out int port)
    {
        hostName = ""; port = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string text = value.Trim();
        if (text.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase)) text = text[6..];
        if (text.StartsWith("udp://", StringComparison.OrdinalIgnoreCase)) text = text[6..];
        int colon = text.LastIndexOf(':');
        if (colon <= 0 || colon == text.Length - 1) return false;
        hostName = text[..colon].Trim().TrimEnd('.');
        return Uri.CheckHostName(hostName) != UriHostNameType.Unknown && int.TryParse(text[(colon + 1)..], out port) && port is > 0 and <= 65535;
    }

    async Task InstallPortwarp()
    {
        try { UseWaitCursor = true; string path = await PortwarpManager.InstallOrUpdateAsync(); portwarpStatus.Text = "Portwarp CLI installed: " + path; Log("Portwarp CLI copied into the VZ-managed tools folder."); }
        catch (Exception ex) { portwarpStatus.Text = "Portwarp installation failed: " + ex.Message; Log(portwarpStatus.Text); MessageBox.Show(ex.Message, "Portwarp"); }
        finally { UseWaitCursor = false; }
    }
    async Task PortwarpLogin()
    {
        try { string output = await PortwarpManager.RunAsync("login", TimeSpan.FromMinutes(3), line => Log("Portwarp: " + line)); portwarpStatus.Text = output.Length > 0 ? output : "Login command completed; approve the device in your browser."; }
        catch (Exception ex) { portwarpStatus.Text = "Login failed: " + ex.Message; Log(portwarpStatus.Text); MessageBox.Show(ex.Message, "Portwarp login"); }
    }
    async Task RefreshPortwarp()
    {
        try { string output = await PortwarpManager.RunAsync("tunnels", TimeSpan.FromSeconds(20), line => Log("Portwarp: " + line)); MessageBox.Show(output, "Configured Portwarp tunnels"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Portwarp"); }
    }
    async Task ConnectPortwarp()
    {
        try {
            if (string.IsNullOrWhiteSpace(portwarpTunnel.Text)) throw new InvalidOperationException("Enter the exact tunnel name shown by Portwarp.");
            string output = await PortwarpManager.RunAsync("connect " + QuoteArg(portwarpTunnel.Text.Trim()) + " --detach", TimeSpan.FromSeconds(30), line => Log("Portwarp: " + line));
            portwarpStatus.Text = "Connect command completed. Verify the tunnel is running in Portwarp.";
            Log(output); MessageBox.Show("Connect command completed. Check the Portwarp dashboard/session status before sharing the public endpoint.", "VZ Portwarp");
        } catch (Exception ex) { portwarpStatus.Text = "Tunnel connection failed: " + ex.Message; Log(portwarpStatus.Text); MessageBox.Show(ex.Message, "Portwarp"); }
    }
    async Task StopPortwarp()
    {
        try {
            if (string.IsNullOrWhiteSpace(portwarpTunnel.Text)) throw new InvalidOperationException("Enter the exact tunnel name first.");
            string output = await PortwarpManager.RunAsync("stop " + QuoteArg(portwarpTunnel.Text.Trim()), TimeSpan.FromSeconds(20), line => Log("Portwarp: " + line));
            portwarpStatus.Text = "Stop command completed."; Log(output);
        } catch (Exception ex) { MessageBox.Show(ex.Message, "Portwarp"); }
    }
    static string QuoteArg(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static class InviteCodec
    {
        public static string Create(string host, int port)
        {
            string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(host + "|" + port));
            string safePayload = payload.TrimEnd('=').Replace('+','-').Replace('/','_');
            string code = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(safePayload)))[..6];
            return "VZ1-" + code + "-" + safePayload;
        }

        public static (string Host, int Port) Read(string code)
        {
            if (!code.StartsWith("VZ1-", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("This is not a VZ1 invite code.");
            string[] parts = code.Split('-', 3);
            if (parts.Length != 3) throw new FormatException("Invite code is incomplete.");
            string encoded = parts[2].Replace('-','+').Replace('_','/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            string[] fields = decoded.Split('|', 2);
            if (fields.Length != 2 || !TryParseEndpoint(fields[0] + ":" + fields[1], out var host, out var port))
                throw new FormatException("Invite code contains an invalid endpoint.");
            string expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(parts[2])))[..6];
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(parts[1].ToUpperInvariant())))
                throw new FormatException("Invite code checksum is invalid.");
            return (host, port);
        }
    }

    TabPage SettingsPage()
    {
        var p = Page("Settings");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        flow.Controls.Add(new Label { Text = "VZ  //  NEPTUNNEL", AutoSize = true, Font = new Font("Segoe UI", 25, FontStyle.Bold), ForeColor = Color.Silver });
        flow.Controls.Add(Heading("Personalize your tunnel"));
        flow.Controls.Add(new Label { Text = "Choose one of five interface designs:", AutoSize = true });
        themeChoice.Items.AddRange(new object[] { "Chrome", "Midnight", "Neon", "Carbon", "Frost" });
        themeChoice.SelectedItem = selectedTheme;
        themeChoice.SelectedIndexChanged += (_, _) => { selectedTheme = themeChoice.Text; ApplyTheme(); SaveSettings(); };
        flow.Controls.Add(themeChoice);
        flow.Controls.Add(new Label { Text = "Appearance:", AutoSize = true });
        appearanceChoice.Items.AddRange(new object[] { "System", "Dark", "Light" });
        appearanceChoice.SelectedItem = appearance;
        appearanceChoice.SelectedIndexChanged += (_, _) => { appearance = appearanceChoice.Text; ApplyTheme(); SaveSettings(); };
        flow.Controls.Add(appearanceChoice);
        flow.Controls.Add(new Label { Text = "Discord Rich Presence", AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold) });
        flow.Controls.Add(discordEnabled);
        flow.Controls.Add(new Label { Text = "Use your VZ Discord Application ID below. Discord desktop must be running and allow activity sharing. Turn this switch off to immediately clear VZ from your Discord profile.", AutoSize = true, MaximumSize = new Size(740,0) });
        flow.Controls.Add(discordAppId);
        flow.Controls.Add(Btn("Apply Discord settings", () => ApplyDiscord()));
        flow.Controls.Add(discordState);
        flow.Controls.Add(Btn("Save settings", () => { SaveSettings(); MessageBox.Show("Preferences saved."); }));
        p.Controls.Add(flow);
        return p;
    }

    void LoadSettings()
    {
        try {
            if (File.Exists(settingsPath)) {
                using var doc = JsonDocument.Parse(File.ReadAllText(settingsPath));
                var r = doc.RootElement;
                if (r.TryGetProperty("theme", out var t)) selectedTheme = t.GetString() ?? "Obsidian";
                if (r.TryGetProperty("appearance", out var a)) appearance = a.GetString() ?? "System";
                if (r.TryGetProperty("discordEnabled", out var e)) discordEnabled.Checked = e.GetBoolean();
                if (r.TryGetProperty("discordAppId", out var id)) discordAppId.Text = id.GetString() ?? "";
            }
        } catch (Exception ex) { Log("Settings load: " + ex.Message); }
        if (themeChoice.Items.Contains(selectedTheme)) themeChoice.SelectedItem = selectedTheme;
        if (appearanceChoice.Items.Contains(appearance)) appearanceChoice.SelectedItem = appearance;
        ApplyTheme();
        if (discordEnabled.Checked) ApplyDiscord();
    }

    void SaveSettings()
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, JsonSerializer.Serialize(new { theme = selectedTheme, appearance, discordEnabled = discordEnabled.Checked, discordAppId = discordAppId.Text.Trim() }, new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception ex) { Log("Settings save: " + ex.Message); }
    }

    void ApplyTheme()
    {
        bool dark = appearance == "Dark" || (appearance == "System" && IsSystemDark());
        Color accent = selectedTheme switch {
            "Chrome" => Color.FromArgb(150,155,165),
            "Midnight" => Color.FromArgb(55,65,90),
            "Neon" => Color.FromArgb(0,170,155),
            "Carbon" => Color.FromArgb(75,82,92),
            "Frost" => Color.FromArgb(105,165,205),
            _ => Color.FromArgb(150,155,165)
        };
        BackColor = dark ? Color.FromArgb(19,21,27) : Color.FromArgb(238,240,244);
        ForeColor = dark ? Color.Gainsboro : Color.FromArgb(30,34,42);
        foreach (Control control in Controls) ThemeControl(control, dark, accent);
        foreach (TabPage tab in Controls.OfType<TabControl>().SelectMany(x => x.TabPages.Cast<TabPage>())) { tab.BackColor = BackColor; tab.ForeColor = ForeColor; }
    }

    void ThemeControl(Control control, bool dark, Color accent)
    {
        if (control is Button b) { b.BackColor = accent; b.ForeColor = Color.White; }
        else if (control is TextBox || control is ComboBox || control is ListBox) { control.BackColor = dark ? Color.FromArgb(32,36,45) : Color.White; control.ForeColor = dark ? Color.White : Color.FromArgb(25,28,35); }
        else { control.BackColor = BackColor; control.ForeColor = ForeColor; }
        foreach (Control child in control.Controls) ThemeControl(child, dark, accent);
    }

    static bool IsSystemDark()
    {
        try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is int v && v == 0; }
        catch { return true; }
    }

    void ApplyDiscord()
    {
        StopDiscord();
        if (!discordEnabled.Checked) { discordState.Text = "Off — VZ will not appear in Discord activity."; SaveSettings(); return; }
        string id = discordAppId.Text.Trim();
        if (id.Length < 15 || !id.All(char.IsDigit)) { discordState.Text = "Enter a valid Discord Application ID."; return; }
        try {
            discordClient = new DiscordRpcClient(id);
            discordClient.Initialize();
            discordClient.SetPresence(new RichPresence { Details = "VZ NepTunnel is open", State = "Ready to host or join", Assets = new Assets { LargeImageText = "VZ NepTunnel" }, Timestamps = Timestamps.Now });
            discordState.Text = "Enabled — VZ activity is visible while the app is open.";
            Log("Discord Rich Presence initialized.");
        } catch (Exception ex) { StopDiscord(); discordState.Text = "Discord connection failed: " + ex.Message; Log("Discord RPC: " + ex.Message); }
        SaveSettings();
    }

    void StopDiscord()
    {
        try { discordClient?.ClearPresence(); discordClient?.Dispose(); } catch { }
        discordClient = null;
    }

    void InstallPlugin()
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Plugins");
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, "VZNepTunnelHost.lua");
            using var resource = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("VZNepTunnel.VZNepTunnelImporter.lua");
            if (resource == null) throw new FileNotFoundException("The Studio importer plugin is missing from this app build.");
            using var output = File.Create(target);
            resource.CopyTo(output);
            Log("Installed Roblox Studio plugin: " + target);
            MessageBox.Show("VZ Studio host plugin installed. If Roblox Studio is already open, close and reopen it. Then open Plugins > VZ NepTunnel > Start local client session.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Plugin installation"); }
    }

    void ChooseMap()
    {
        using var d = new OpenFileDialog { Filter = "Roblox place files (*.rbxl;*.rbxlx)|*.rbxl;*.rbxlx|All files (*.*)|*.*" };
        if (d.ShowDialog() == DialogResult.OK) { mapPath = d.FileName; Log("Selected map: " + mapPath); }
    }

    void StartHost()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(mapPath) || !File.Exists(mapPath))
            {
                MessageBox.Show("Choose a valid .rbxl or .rbxlx place file first.");
                return;
            }

            if (Process.GetProcessesByName("RobloxStudioBeta").Length > 0)
            {
                MessageBox.Show("Please save your work and close every Roblox Studio window first. VZ needs to install its Studio plugin before opening your selected place. Then click Host Session again.", "Close Roblox Studio first");
                return;
            }

            InstallPlugin();
            if (string.IsNullOrWhiteSpace(studioPath) || !File.Exists(studioPath)) FindStudio();
            if (string.IsNullOrWhiteSpace(studioPath)) return;

            hostStatus.Text = "NOT HOSTED — LOCAL STUDIO TEST ONLY";
            hostStatus.ForeColor = Color.Khaki;
            endpoint.Text = $"Opening local place. Port {(int)hostPort.Value} is only a configured value; no network server or public tunnel has been verified.";
            Log("VZ Studio plugin installed. Opening the selected local place.");
            LaunchMapInStudio();
            MessageBox.Show(
                "Your local place is opening in Roblox Studio.\n\nIn Studio, click Plugins > VZ NepTunnel > Start local client session. This starts one local server and one playable client window.\n\nThis is Studio's local multiplayer test; it does not yet allow friends over the Internet.",
                "VZ NepTunnel — Start client session");
        }
        catch (Exception ex)
        {
            Log("Could not prepare Studio host: " + ex.Message);
            MessageBox.Show(ex.Message, "VZ NepTunnel");
        }
    }

    void StopHost()
    {
        try { hostCts?.Cancel(); host?.Close(); host?.Dispose(); } catch { }
        hostCts?.Dispose(); hostCts = null; host = null;
        if (endpoint.IsHandleCreated) endpoint.Text = "Host stopped";
    }

    async Task SendTest()
    {
        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 3000;
            var ip = IPAddress.Parse(address.Text.Trim());
            var ep = new IPEndPoint(ip, (int)joinPort.Value);
            var bytes = Encoding.UTF8.GetBytes("VZ-NEPTUNNEL-PING");
            await udp.SendAsync(bytes, ep);
            var receive = udp.ReceiveAsync();
            var done = await Task.WhenAny(receive, Task.Delay(3000));
            if (done != receive) { Log("No response. Check host IP, port, firewall and network."); MessageBox.Show("No UDP response. Check the host and Windows Firewall."); return; }
            var reply = await receive;
            Log($"UDP test successful: {Encoding.UTF8.GetString(reply.Buffer)} from {reply.RemoteEndPoint}");
            MessageBox.Show("LAN UDP test successful!");
        }
        catch (Exception ex) { Log("Join test failed: " + ex.Message); MessageBox.Show(ex.Message, "Connection test"); }
    }

    void AddAssets()
    {
        using var d = new OpenFileDialog { Filter = "Roblox models (*.rbxm;*.rbxmx)|*.rbxm;*.rbxmx|All files (*.*)|*.*", Multiselect = true };
        if (d.ShowDialog() == DialogResult.OK) foreach (var f in d.FileNames) imports.Items.Add(f);
    }
    void OpenSelectedAsset()
    {
        if (imports.SelectedItem is not string file) { MessageBox.Show("Select an RBXM file first."); return; }
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); Log("Opened asset using its Windows file association: " + file); }
        catch (Exception ex) { MessageBox.Show("Could not open asset: " + ex.Message); }
    }
    void FindStudio()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Roblox", "Versions"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Roblox", "Versions")
        };
        studioPath = candidates.Where(Directory.Exists)
            .SelectMany(x => Directory.GetDirectories(x))
            .Select(x => Path.Combine(x, "RobloxStudioBeta.exe"))
            .Where(File.Exists)
            .OrderByDescending(x => Directory.GetLastWriteTimeUtc(Path.GetDirectoryName(x)!))
            .FirstOrDefault();
        if (studioPath == null) { using var d = new OpenFileDialog { Filter = "Roblox Studio (RobloxStudioBeta.exe)|RobloxStudioBeta.exe" }; if (d.ShowDialog() == DialogResult.OK) studioPath = d.FileName; }
        Log(studioPath == null ? "Roblox Studio not located." : "Roblox Studio: " + studioPath);
        if (studioPath != null) MessageBox.Show("Roblox Studio found:\n" + studioPath);
    }
    void OpenMap()
    {
        if (string.IsNullOrEmpty(mapPath) || !File.Exists(mapPath)) { MessageBox.Show("Choose a valid .rbxl or .rbxlx map first."); return; }
        LaunchMapInStudio();
    }

    void LaunchMapInStudio()
    {
        if (string.IsNullOrWhiteSpace(mapPath) || !File.Exists(mapPath))
        {
            Log("No valid local place file selected.");
            MessageBox.Show("Choose your .rbxl or .rbxlx place file first.");
            return;
        }
        if (string.IsNullOrEmpty(studioPath) || !File.Exists(studioPath)) FindStudio();
        if (string.IsNullOrEmpty(studioPath)) return;
        try
        {
            // Documented Roblox Studio CLI syntax for opening a local place file.
            var start = new ProcessStartInfo { FileName = studioPath, UseShellExecute = false };
            start.ArgumentList.Add("--task");
            start.ArgumentList.Add("EditFile");
            start.ArgumentList.Add("--localPlaceFile");
            start.ArgumentList.Add(mapPath);
            Process.Start(start);
            Log("Opened local place in Roblox Studio using --task EditFile --localPlaceFile.");
            Log("In Studio, choose Plugins > VZ NepTunnel > Start local client session.");
        }
        catch (Exception ex)
        {
            Log("Could not open place in Studio: " + ex.Message);
            MessageBox.Show("Could not open this place file in Roblox Studio. Try Studio > File > Open from File.\n\n" + ex.Message);
        }
    }
    static string GetLanIp()
    {
        try { return Dns.GetHostEntry(Dns.GetHostName()).AddressList.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))?.ToString() ?? "127.0.0.1"; }
        catch { return "127.0.0.1"; }
    }
}