using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
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
    readonly TextBox relayIp = new() { Width = 300, PlaceholderText = "Relay server public IPv4" };
    readonly TextBox roomCode = new() { Width = 300, PlaceholderText = "Shared room code (8+ characters)" };
    readonly TextBox hetznerToken = new() { Width = 420, UseSystemPasswordChar = true, PlaceholderText = "Hetzner API token (Read & Write)" };
    readonly ComboBox hetznerLocation = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    readonly ComboBox hetznerType = new() { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
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
        tabs.TabPages.Add(CloudPage());
        tabs.TabPages.Add(SettingsPage());
        Controls.Add(tabs);
        Log("VZ NepTunnel started. Public VPS relay is the intended connection path; LAN-IP joining is disabled.");
        LoadSettings();
        discordEnabled.CheckedChanged += (_, _) => ApplyDiscord();
        FormClosing += (_, _) => { StopHost(); StopDiscord(); SaveSettings(); };
    }

    TabPage Page(string title) { var p = new TabPage(title) { BackColor = BackColor, ForeColor = ForeColor, Padding = new Padding(18) }; return p; }
    Button Btn(string text, Action action) { var b = new Button { Text = text, AutoSize = true, Padding = new Padding(12,7,12,7), BackColor = Color.FromArgb(49,93,160), ForeColor = Color.White, FlatStyle = FlatStyle.Flat }; b.Click += (_,_) => action(); return b; }
    Label Heading(string s) => new() { Text = s, AutoSize = true, Font = new Font("Segoe UI", 19, FontStyle.Bold), ForeColor = Color.White, Margin = new Padding(0,0,0,16) };
    void Log(string s) { if (log.InvokeRequired) { log.BeginInvoke(new Action(() => Log(s))); return; } log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}"); }

    TabPage HomePage()
    {
        var p = Page("Host");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        flow.Controls.Add(new Label { Text = "VZ", AutoSize = true, Font = new Font("Segoe UI", 34, FontStyle.Bold), ForeColor = Color.Silver, Margin = new Padding(0,0,0,0) });
        flow.Controls.Add(Heading("NEPTUNNEL  //  HOST A SESSION"));
        flow.Controls.Add(new Label { Text = "Choose your Roblox place file (.rbxl / .rbxlx). It stays on this PC.", AutoSize = true });
        var map = Btn("Choose map…", () => ChooseMap());
        flow.Controls.Add(map);
        flow.Controls.Add(new Label { Text = "Session status:", AutoSize = true });
        flow.Controls.Add(hostStatus);
        flow.Controls.Add(new Label { Text = "Configured port (not proof of a running game server):", AutoSize = true });
        flow.Controls.Add(hostPort);
        flow.Controls.Add(configuredPort);
        flow.Controls.Add(Btn("Host selected map in Studio", () => StartHost()));
        flow.Controls.Add(endpoint);
        flow.Controls.Add(Btn("Find Roblox Studio", () => FindStudio()));
        flow.Controls.Add(Btn("Open selected map in Studio", () => OpenMap()));
        flow.Controls.Add(new Label { Text = "Host installs the VZ Studio plugin and opens your selected place. In Studio, click Plugins > VZ NepTunnel > Start local client session to launch one server and one playable client window. This is a local Studio test, not an Internet join tunnel.", AutoSize = true, MaximumSize = new Size(780,0), ForeColor = Color.Khaki, Margin = new Padding(0,22,0,0) });
        configuredPort.Text = $"Configured port: {(int)hostPort.Value}";
        hostPort.ValueChanged += (_, _) => configuredPort.Text = $"Configured port: {(int)hostPort.Value}";
        p.Controls.Add(flow); return p;
    }

    TabPage JoinPage()
    {
        var p = Page("Join");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        flow.Controls.Add(Heading("Join over the Internet"));
        flow.Controls.Add(new Label { Text = "Enter the public VZ relay address and the room code shared by the host. You do not need the host's home IP or to be on the same Wi-Fi.", AutoSize = true, MaximumSize = new Size(760,0) });
        flow.Controls.Add(new Label { Text = "Public relay IPv4:", AutoSize = true });
        flow.Controls.Add(relayIp);
        flow.Controls.Add(new Label { Text = "Host's room code:", AutoSize = true });
        flow.Controls.Add(roomCode);
        relayRole.Items.Clear(); relayRole.Items.AddRange(new object[] { "host", "join" }); relayRole.SelectedItem = "join";
        flow.Controls.Add(Btn("Connect to public relay", async () => { relayRole.SelectedItem = "join"; await TestRelay(); }));
        flow.Controls.Add(new Label { Text = "This currently tests public relay registration only. Full Roblox Studio gameplay forwarding is not yet verified, so VZ will not claim that you have joined a playable session.", AutoSize = true, MaximumSize = new Size(760,0), ForeColor = Color.Khaki, Margin = new Padding(0,16,0,0) });
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


    TabPage CloudPage()
    {
        var p = Page("VPS Relay");
        var flow = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        flow.Controls.Add(Heading("Create your VZ relay VPS"));
        flow.Controls.Add(new Label { Text = "Paste a Hetzner Cloud API token with Read & Write permission. The token is used only for this request and is not saved.", AutoSize = true, MaximumSize = new Size(760,0) });
        flow.Controls.Add(hetznerToken);
        hetznerLocation.Items.AddRange(new object[] { "fsn1", "nbg1", "hel1", "ash", "hil", "sin" }); hetznerLocation.SelectedIndex = 0;
        hetznerType.Items.AddRange(new object[] { "cx22", "cpx22", "cpx32" }); hetznerType.SelectedIndex = 0;
        flow.Controls.Add(new Label { Text = "Location:", AutoSize = true }); flow.Controls.Add(hetznerLocation);
        flow.Controls.Add(new Label { Text = "Server type (Hetzner billing applies):", AutoSize = true }); flow.Controls.Add(hetznerType);
        flow.Controls.Add(Btn("Create UDP relay VPS", async () => await CreateRelayVps()));
        flow.Controls.Add(new Label { Text = "The VPS installs a UDP relay on port 45000. This creates relay infrastructure; it does not automatically route Roblox Studio's full multiplayer traffic yet.", AutoSize = true, MaximumSize = new Size(760,0), ForeColor = Color.Khaki, Margin = new Padding(0,18,0,0) });
        p.Controls.Add(flow); return p;
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

    async Task CreateRelayVps()
    {
        try
        {
            UseWaitCursor = true;
            var result = await new HetznerProvisioner().CreateRelayAsync(hetznerToken.Text, hetznerLocation.Text, hetznerType.Text);
            relayIp.Text = result.ip;
            Log($"Hetzner relay server created: ID {result.id}, IP {result.ip}. Allow several minutes for cloud-init.");
            MessageBox.Show($"Relay VPS created!\nIP: {result.ip}\nUDP port: 45000\nServer ID: {result.id}\n\nHetzner charges your account for this server.", "VZ NepTunnel");
        }
        catch (Exception ex) { Log("VPS provisioning failed: " + ex.Message); MessageBox.Show(ex.Message, "Hetzner provisioning"); }
        finally { UseWaitCursor = false; }
    }

    async Task TestRelay()
    {
        try
        {
            var ip = IPAddress.Parse(relayIp.Text.Trim());
            if (roomCode.Text.Trim().Length < 8) throw new ArgumentException("Room code must be at least 8 characters.");
            using var udp = new UdpClient();
            var ep = new IPEndPoint(ip, 45000);
            var payload = Encoding.UTF8.GetBytes("VZ1|" + roomCode.Text.Trim() + "|" + relayRole.Text);
            for (int i = 0; i < 5; i++)
            {
                await udp.SendAsync(payload, ep);
                var receive = udp.ReceiveAsync();
                if (await Task.WhenAny(receive, Task.Delay(700)) == receive)
                {
                    var response = Encoding.UTF8.GetString((await receive).Buffer);
                    Log("Relay response: " + response);
                    MessageBox.Show("Relay responded: " + response + "\nA peer must register in the same room to exchange traffic.");
                    return;
                }
            }
            throw new TimeoutException("No relay response. Check VPS cloud-init and UDP port 45000 firewall.");
        }
        catch (Exception ex) { Log("Relay test failed: " + ex.Message); MessageBox.Show(ex.Message, "Relay test"); }
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