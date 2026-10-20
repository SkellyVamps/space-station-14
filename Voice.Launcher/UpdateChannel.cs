using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Skelly.VoiceLauncher;

public static class UpdateChannel
{
    public sealed record Trust(string PublicKey, string ManifestUrl);
    public static Trust Configuration()
    {
        using var stream = typeof(UpdateChannel).Assembly.GetManifestResourceStream("UpdateTrust");
        return stream == null ? new("", "") : JsonSerializer.Deserialize<Trust>(stream) ?? new("", "");
    }
    public static bool Enabled => !string.IsNullOrWhiteSpace(Configuration().PublicKey) && !string.IsNullOrWhiteSpace(Configuration().ManifestUrl);
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Skelly SS14 Voice", "releases");
    private static string Runtime => RuntimeInformation.RuntimeIdentifier;
    private static string State => Path.Combine(Root, Runtime + ".json");
    private static long Current()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "build-info.json");
        if (!File.Exists(path)) return 0;
        using var info = JsonDocument.Parse(File.ReadAllText(path));
        return info.RootElement.TryGetProperty("release_sequence", out var sequence) ? sequence.GetInt64() : 0;
    }
    public static bool TryStartInstalled()
    {
        try
        {
            if (!Enabled || !File.Exists(State)) return false;
            var release = ReleaseUpdates.Verify(File.ReadAllText(State), Configuration().PublicKey, Runtime, Current());
            var executable = Path.Combine(Root, release.Sequence + "-" + Runtime, "SkellySS14", OperatingSystem.IsWindows() ? "SkellyLauncher.exe" : "SkellyLauncher");
            if (!File.Exists(executable) || Path.GetFullPath(executable) == Path.GetFullPath(Environment.ProcessPath!)) return false;
            return Start(executable);
        }
        catch (Exception e) when (IsExpected(e)) { return false; }
    }
    public static async Task InstallLatest(CancellationToken cancel)
    {
        var trust = Configuration();
        if (!Enabled || !Uri.TryCreate(trust.ManifestUrl.Replace("{runtime}", Runtime), UriKind.Absolute, out var url) || url.Scheme != "https")
            throw new InvalidDataException("Update channel unavailable");
        using var download = new HttpClient { Timeout = TimeSpan.FromMinutes(10), MaxResponseContentBufferSize = 65536 };
        var envelope = await download.GetStringAsync(url, cancel);
        var current = Current();
        if (File.Exists(State)) current = Math.Max(current, ReleaseUpdates.Verify(File.ReadAllText(State), trust.PublicKey, Runtime, -1).Sequence);
        var release = ReleaseUpdates.Verify(envelope, trust.PublicKey, Runtime, current);
        var executable = await ReleaseUpdates.Install(download, release, Root, cancel);
        await File.WriteAllTextAsync(State + ".pending", envelope, cancel);
        File.Move(State + ".pending", State, true);
        if (!Start(executable)) throw new IOException("Could not start updated launcher");
    }
    private static bool Start(string path) => Process.Start(new ProcessStartInfo(path)
        { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! }) != null;
    public static bool IsExpected(Exception e) => e is HttpRequestException or IOException or System.Security.Cryptography.CryptographicException or
        System.ComponentModel.Win32Exception or JsonException or ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException or OperationCanceledException;
}
