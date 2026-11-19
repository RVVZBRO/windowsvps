using System.Diagnostics;

namespace VZNepTunnel;

internal static class PortwarpManager
{
    private static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VZ NepTunnel", "Portwarp");
    private static readonly string ExePath = Path.Combine(InstallDir, "pwrp.exe");
    private const string DownloadsPage = "https://portwarp.com/downloads";
    public static string ExecutablePath => ResolveExecutable();

    private static string ResolveExecutable()
    {
        if (File.Exists(ExePath)) return ExePath;
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = Path.Combine(folder.Trim('"'), "pwrp.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return ExePath;
    }

    public static Task<string> InstallOrUpdateAsync()
    {
        Directory.CreateDirectory(InstallDir);
        string existing = ResolveExecutable();
        if (File.Exists(existing)) return Task.FromResult(existing);
        if (!File.Exists(ExePath))
        {
            Process.Start(new ProcessStartInfo(DownloadsPage) { UseShellExecute = true });
            using var picker = new OpenFileDialog
            {
                Title = "Select pwrp.exe from the Portwarp Windows ZIP",
                Filter = "Portwarp CLI (pwrp.exe)|pwrp.exe|Executable files (*.exe)|*.exe",
                CheckFileExists = true
            };
            if (picker.ShowDialog() != DialogResult.OK)
                throw new OperationCanceledException("Portwarp setup cancelled. Download the Windows x86-64 CLI ZIP, extract it, then choose pwrp.exe.");
            File.Copy(picker.FileName, ExePath, true);
        }
        return Task.FromResult(ExePath);
    }

    public static async Task<string> RunAsync(string arguments, TimeSpan timeout, Action<string>? onLine = null, CancellationToken ct = default)
    {
        string executable = ResolveExecutable();
        if (!File.Exists(executable))
            throw new FileNotFoundException("Install Portwarp CLI first using the setup button in VZ.");
        var start = new ProcessStartInfo
        {
            FileName = executable, Arguments = arguments, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        using var process = new Process { StartInfo = start };
        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (output) output.AppendLine(e.Data); onLine?.Invoke(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (output) output.AppendLine(e.Data); onLine?.Invoke(e.Data); } };
        if (!process.Start()) throw new InvalidOperationException("Could not start Portwarp CLI.");
        process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try { await process.WaitForExitAsync(timeoutCts.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw new TimeoutException("Portwarp command timed out."); }
        string result; lock (output) result = output.ToString();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Portwarp exited with code {process.ExitCode}. {result}");
        return result;
    }
}
