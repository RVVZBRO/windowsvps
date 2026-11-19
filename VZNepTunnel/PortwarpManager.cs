using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;

namespace VZNepTunnel;

internal static class PortwarpManager
{
    private const string DownloadUrl = "https://portwarp.com/downloads/pwrp-windows-amd64.exe";
    private static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VZ NepTunnel", "Portwarp");
    private static readonly string ExePath = Path.Combine(InstallDir, "pwrp.exe");

    public static async Task<string> InstallOrUpdateAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(InstallDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length < 1024 || bytes.Length > 100_000_000)
            throw new InvalidDataException("The downloaded Portwarp CLI has an unexpected file size.");
        var temp = ExePath + ".download";
        await File.WriteAllBytesAsync(temp, bytes, ct);
        try
        {
            if (File.Exists(ExePath)) File.Delete(ExePath);
            File.Move(temp, ExePath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return ExePath;
    }

    public static async Task<string> RunAsync(string arguments, TimeSpan timeout, Action<string>? onLine = null, CancellationToken ct = default)
    {
        if (!File.Exists(ExePath))
            throw new FileNotFoundException("Install Portwarp CLI first using the button in VZ.");
        var start = new ProcessStartInfo
        {
            FileName = ExePath,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = InstallDir
        };
        using var process = new Process { StartInfo = start };
        var output = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (output) output.AppendLine(e.Data); onLine?.Invoke(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (output) output.AppendLine(e.Data); onLine?.Invoke(e.Data); } };
        if (!process.Start()) throw new InvalidOperationException("Could not start Portwarp CLI.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try { await process.WaitForExitAsync(timeoutCts.Token); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch { } throw new TimeoutException("Portwarp command timed out."); }
        string result; lock (output) result = output.ToString();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Portwarp exited with code {process.ExitCode}. {result}");
        return result;
    }
}
