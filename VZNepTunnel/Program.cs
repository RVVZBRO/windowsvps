using System.Diagnostics;
using System.Drawing;
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
    private readonly TextBox selectedFile = new() { ReadOnly = true, Dock = DockStyle.Fill, PlaceholderText = "Choose a Roblox place file (.rbxl or .rbxlx)" };
    private readonly TextBox studioExe = new() { ReadOnly = true, Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true, ForeColor = Color.LightGreen, Text = "Ready — choose a local Roblox place file." };
    private readonly TextBox activity = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 18, 27), ForeColor = Color.Gainsboro, BorderStyle = BorderStyle.FixedSingle };
    private string? placePath;
    private string? studioPath;

    public MainForm()
    {
        Text = "VZ — Roblox Studio Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        Size = new Size(900, 650);
        BackColor = Color.FromArgb(20, 24, 34);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var heading = new Label { Text = "VZ", AutoSize = true, Font = new Font("Segoe UI", 32, FontStyle.Bold), ForeColor = Color.White, Margin = new Padding(0, 0, 0, 0) };
        root.Controls.Add(heading);
        root.Controls.Add(new Label { Text = "LOCAL PLACE LAUNCHER", AutoSize = true, ForeColor = Color.FromArgb(115, 190, 255), Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(2, 0, 0, 18) });

        var setup = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        setup.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        setup.Controls.Add(new Label { Text = "PLACE FILE", AutoSize = true, ForeColor = Color.Silver, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 0, 0, 5) }, 0, 0);
        setup.SetColumnSpan(setup.GetControlFromPosition(0, 0)!, 2);
        setup.Controls.Add(selectedFile, 0, 1);
        setup.Controls.Add(Button("Browse…", ChoosePlace), 1, 1);
        setup.Controls.Add(new Label { Text = "ROBLOX STUDIO", AutoSize = true, ForeColor = Color.Silver, Font = new Font("Segoe UI", 9, FontStyle.Bold), Margin = new Padding(0, 16, 0, 5) }, 0, 2);
        setup.SetColumnSpan(setup.GetControlFromPosition(0, 2)!, 2);
        setup.Controls.Add(studioExe, 0, 3);
        setup.Controls.Add(Button("Locate…", LocateStudio), 1, 3);
        root.Controls.Add(setup);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 0, 0, 14) };
        actions.Controls.Add(Button("OPEN IN ROBLOX STUDIO", OpenPlace, true));
        actions.Controls.Add(Button("HOST LOCAL TEST", HostLocalTest));
        actions.Controls.Add(Button("OPEN STUDIO TEST GUIDE", OpenGuide));
        root.Controls.Add(actions);

        var info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(810, 0),
            ForeColor = Color.Khaki,
            Text = "LOCAL TESTING ONLY: Studio can run a local server with multiple simulated clients. This does not create an internet session or an invite code. Remote friends cannot join an unpublished local Studio test. VZ does not publish or upload your place.",
            Margin = new Padding(0, 0, 0, 14)
        };
        root.Controls.Add(info);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        bottom.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.Controls.Add(status, 0, 0);
        bottom.Controls.Add(activity, 0, 1);
        root.Controls.Add(bottom);

        studioPath = FindStudio();
        if (studioPath != null) studioExe.Text = studioPath;
        Log("VZ started. No plugin, tunnel, upload, or publishing is used.");
        if (studioPath == null) SetStatus("Roblox Studio not found — use Locate… to select RobloxStudioBeta.exe.", Color.Khaki);
    }

    private Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            Padding = new Padding(14, 10, 14, 10),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(28, 112, 190) : Color.FromArgb(48, 56, 72),
            ForeColor = Color.White,
            Margin = new Padding(0, 0, 10, 0),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) => action();
        return b;
    }

    private void ChoosePlace()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select a Roblox place",
            Filter = "Roblox place files (*.rbxl;*.rbxlx)|*.rbxl;*.rbxlx",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        placePath = dialog.FileName;
        selectedFile.Text = placePath;
        Log("Selected place: " + placePath);
        SetStatus("Place selected. Click OPEN IN ROBLOX STUDIO or HOST LOCAL TEST.");
    }

    private void LocateStudio()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Locate RobloxStudioBeta.exe",
            Filter = "Roblox Studio (RobloxStudioBeta.exe)|RobloxStudioBeta.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        studioPath = dialog.FileName;
        studioExe.Text = studioPath;
        Log("Roblox Studio selected: " + studioPath);
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

    private bool ValidateSelection()
    {
        if (string.IsNullOrWhiteSpace(placePath) || !File.Exists(placePath))
        {
            MessageBox.Show(this, "Choose an existing .rbxl or .rbxlx file first.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        if (string.IsNullOrWhiteSpace(studioPath) || !File.Exists(studioPath))
        {
            studioPath = FindStudio();
            if (studioPath == null)
            {
                MessageBox.Show(this, "Roblox Studio was not found. Click Locate… and select RobloxStudioBeta.exe.", "VZ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            studioExe.Text = studioPath;
        }
        return true;
    }

    private void OpenPlace()
    {
        if (!ValidateSelection()) return;
        try
        {
            var start = new ProcessStartInfo { FileName = studioPath!, UseShellExecute = false };
            start.ArgumentList.Add("--task");
            start.ArgumentList.Add("EditFile");
            start.ArgumentList.Add("--localPlaceFile");
            start.ArgumentList.Add(placePath!);
            Process.Start(start);
            Log("Launched Roblox Studio with the selected local place using the documented EditFile command.");
            SetStatus("Roblox Studio is opening your place.", Color.LightGreen);
        }
        catch (Exception ex)
        {
            Log("Launch failed: " + ex);
            SetStatus("Could not launch Roblox Studio.", Color.OrangeRed);
            MessageBox.Show(this, ex.Message, "VZ launch error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void HostLocalTest()
    {
        if (!ValidateSelection()) return;
        OpenPlace();
        if (status.ForeColor == Color.OrangeRed) return;
        MessageBox.Show(this,
            "Your place is opening in Roblox Studio.\n\nTo run a local multiplayer test:\n1. Open the Test tab in Studio.\n2. Set the number of clients.\n3. Click Start (or Start Server, then Start Player).\n\nThis creates local simulated clients on this PC. It does not create an internet invite code, and friends on other PCs cannot join this unpublished test.",
            "VZ — Local Studio test", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Log("Host Local Test requested. Studio local server/client controls must be started from the Test tab.");
    }

    private void OpenGuide()
    {
        Process.Start(new ProcessStartInfo("https://create.roblox.com/docs/studio/testing-modes") { UseShellExecute = true });
    }

    private void SetStatus(string message, Color? color = null)
    {
        status.Text = message;
        if (color.HasValue) status.ForeColor = color.Value;
    }

    private void Log(string message)
    {
        activity.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}
