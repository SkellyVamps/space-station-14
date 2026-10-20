using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Skelly.VoiceLauncher;

public sealed record ReleaseManifest(long Sequence, string Runtime, string Url, string Sha256, long Size);
public sealed record SignedRelease(string Payload, string Signature);

public static class ReleaseUpdates
{
    public static ReleaseManifest Verify(string envelope, string publicKey, string runtime, long minimumSequence)
    {
        if (envelope.Length > 65536) throw new InvalidDataException("Release manifest too large");
        var signed = JsonSerializer.Deserialize<SignedRelease>(envelope) ?? throw new InvalidDataException("Missing signature");
        if (string.IsNullOrWhiteSpace(signed.Payload) || string.IsNullOrWhiteSpace(signed.Signature))
            throw new InvalidDataException("Missing signature");
        var payload = Convert.FromBase64String(signed.Payload);
        if (payload.Length > 16384) throw new InvalidDataException("Release payload too large");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (!rsa.VerifyData(payload, Convert.FromBase64String(signed.Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("Release signature rejected");
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(payload) ?? throw new InvalidDataException("Missing release metadata");
        if (manifest.Sequence <= minimumSequence || manifest.Runtime != runtime || manifest.Size is < 1 or > 536870912 ||
            string.IsNullOrWhiteSpace(manifest.Sha256) || manifest.Sha256.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit) ||
            !Uri.TryCreate(manifest.Url, UriKind.Absolute, out var url) || url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo))
            throw new InvalidDataException("Release metadata rejected");
        return manifest;
    }

    public static async Task<string> Install(HttpClient http, ReleaseManifest manifest, string installationRoot, CancellationToken cancel)
    {
        Directory.CreateDirectory(installationRoot);
        var pending = Path.Combine(installationRoot, ".pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pending);
        try
        {
            var archivePath = Path.Combine(pending, "client.zip");
            using (var response = await http.GetAsync(manifest.Url, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancel);
                await using var destination = File.Create(archivePath);
                var buffer = new byte[65536];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    total += read;
                    if (total > manifest.Size) throw new InvalidDataException("Download exceeds signed length");
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancel);
                }
                if (total != manifest.Size) throw new InvalidDataException("Incomplete release download");
            }
            await using (var archive = File.OpenRead(archivePath))
            {
                var digest = Convert.ToHexString(await SHA256.HashDataAsync(archive, cancel));
                if (!digest.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase)) throw new CryptographicException("Release digest rejected");
            }
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                if (zip.Entries.Count > 10000) throw new InvalidDataException("Too many release files");
                long expanded = 0;
                foreach (var entry in zip.Entries)
                {
                    var parts = entry.FullName.Split('/');
                    if (parts.Length < 2 || parts[0] != "SkellySS14" || parts.Any(p => p is "." or ".." || p.Contains(':') || p.Contains('\\')) ||
                        ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                        throw new InvalidDataException("Unsafe release path");
                    expanded += entry.Length;
                    if (expanded > 2147483648) throw new InvalidDataException("Expanded release exceeds size limit");
                }
                zip.ExtractToDirectory(pending);
            }
            File.Delete(archivePath);
            var launcher = Path.Combine(pending, "SkellySS14", OperatingSystem.IsWindows() ? "SkellyLauncher.exe" : "SkellyLauncher");
            var client = Path.Combine(pending, "SkellySS14", "Client", OperatingSystem.IsWindows() ? "SkellySS14.exe" : "SkellySS14");
            if (!File.Exists(launcher) || !File.Exists(client)) throw new InvalidDataException("Incomplete client release");
            if (!OperatingSystem.IsWindows())
                foreach (var executable in new[] { launcher, client })
                    File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
            var installed = Path.Combine(installationRoot, manifest.Sequence + "-" + manifest.Runtime);
            Directory.Move(pending, installed);
            return Path.Combine(installed, "SkellySS14", Path.GetFileName(launcher));
        }
        finally { if (Directory.Exists(pending)) Directory.Delete(pending, true); }
    }
}
