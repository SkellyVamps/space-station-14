using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Skelly.VoiceLauncher;

var account = Guid.NewGuid();
var phase = 0;
using var http = new HttpClient(new Fake(async request =>
{
    if (request.RequestUri!.Host != "auth.spacestation14.com") throw new Exception("Credentials left auth host");
    using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    if (phase++ == 0)
    {
        if (json.RootElement.GetProperty("password").GetString() != "fake-password") throw new Exception("Wrong request");
        return Reply(new { code = "TfaRequired" }, HttpStatusCode.Unauthorized);
    }
    if (phase == 2)
    {
        if (json.RootElement.GetProperty("tfaCode").GetString() != "123456") throw new Exception("MFA missing");
        return Reply(new { token = "FAKE-TOKEN", username = "Test", userId = account, expireTime = DateTimeOffset.UtcNow.AddHours(1) });
    }
    if (request.RequestUri.AbsolutePath.EndsWith("refresh"))
        return Reply(new { newToken = "RENEWED-FAKE", expireTime = DateTimeOffset.UtcNow.AddHours(2) });
    if (!request.RequestUri.AbsolutePath.EndsWith("logout")) throw new Exception("Wrong logout endpoint");
    return Reply(new { });
}));
var auth = new Ss14Authentication(http);
try { await auth.Login("Test", "fake-password", null, default); throw new Exception("MFA bypass"); }
catch (Ss14Authentication.LoginDenied e) when (e.Code == "TfaRequired") { }
var session = await auth.Login("Test", "fake-password", "123456", default);
if (session.UserId != account) throw new Exception("Changed account UUID");
var start = ServerConnection.CreateLaunch(new("fake-key", new Uri("udp://localhost:1212")), session);
if (start.UseShellExecute || start.ArgumentList.Any(x => x.Contains("FAKE-TOKEN")) || start.Environment["ROBUST_AUTH_TOKEN"] != session.Token)
    throw new Exception("Unsafe token handoff");
var refreshed = await auth.Refresh(session, default);
if (refreshed.UserId != account || refreshed.Token != "RENEWED-FAKE") throw new Exception("Invalid renewal");
await auth.Logout(refreshed, default);
foreach (var bad in new[] { "https://example.com", "ss14://user:password@localhost", "ss14://localhost/path", "ss14://localhost?x=1" })
{
    try { ServerConnection.ParseAddress(bad); throw new Exception("Unsafe address accepted"); } catch (ArgumentException) { }
}
using (var server = new HttpClient(new Fake(_ => Task.FromResult(Reply(new { auth = new { mode = "Disabled", public_key = "fake" } })))))
{
    try { await ServerConnection.GetTarget(server, ServerConnection.ParseAddress("localhost"), default); throw new Exception("Disabled auth accepted"); }
    catch (InvalidDataException) { }
}
using (var server = new HttpClient(new Fake(_ => Task.FromResult(Reply(new
{
    auth = new { mode = "Required", public_key = Convert.ToBase64String(new byte[256]) }, connect_address = "udp://game.example.com:2000"
})))))
{
    var target = await ServerConnection.GetTarget(server, ServerConnection.ParseAddress("ss14s://web.example.com:443"), default);
    if (target.Endpoint.Host != "game.example.com" || target.Endpoint.Port != 2000) throw new Exception("Advertised game address ignored");
}
foreach (var (address, expectedPort) in new[] { ("ss14://example.com", 1212), ("ss14s://example.com", 443) })
{
    using var server = new HttpClient(new Fake(request =>
    {
        if (request.RequestUri!.Port != expectedPort) throw new Exception("Incorrect API default port");
        return Task.FromResult(Reply(new { auth = new { mode = "Required", public_key = Convert.ToBase64String(new byte[32]) } }));
    }));
    var target = await ServerConnection.GetTarget(server, ServerConnection.ParseAddress(address), default);
    if (target.Endpoint.Port != expectedPort) throw new Exception("Incorrect default game port");
}
using (var rsa = RSA.Create(2048))
{
    var manifest = new ReleaseManifest(12, "linux-x64", "https://example.com/client.zip", new string('A', 64), 123);
    var payload = JsonSerializer.SerializeToUtf8Bytes(manifest);
    var signed = new SignedRelease(Convert.ToBase64String(payload), Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)));
    var envelope = JsonSerializer.Serialize(signed);
    var key = rsa.ExportSubjectPublicKeyInfoPem();
    if (ReleaseUpdates.Verify(envelope, key, "linux-x64", 11).Sequence != 12) throw new Exception("Verification failed");
    foreach (var check in new Action[]
    {
        () => ReleaseUpdates.Verify(envelope, key, "win-x64", 11),
        () => ReleaseUpdates.Verify(envelope, key, "linux-x64", 12),
        () => ReleaseUpdates.Verify(JsonSerializer.Serialize(signed with { Payload = Convert.ToBase64String("tampered"u8) }), key, "linux-x64", 11)
    })
    {
        var rejected = false;
        try { check(); } catch (Exception e) when (e is InvalidDataException or CryptographicException) { rejected = true; }
        if (!rejected) throw new Exception("Unsafe release accepted");
    }
}
var installRoot = Path.Combine(Path.GetTempPath(), "skelly-update-test-" + Guid.NewGuid());
try
{
    foreach (var unsafeArchive in new[] { false, true })
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            foreach (var path in unsafeArchive ? new[] { "SkellySS14/../../escape" } : new[]
            {
                "SkellySS14/" + (OperatingSystem.IsWindows() ? "SkellyLauncher.exe" : "SkellyLauncher"),
                "SkellySS14/Client/" + (OperatingSystem.IsWindows() ? "SkellySS14.exe" : "SkellySS14")
            })
            { using var file = zip.CreateEntry(path).Open(); file.Write("test-only"u8); }
        }
        var archiveBytes = bytes.ToArray();
        using var download = new HttpClient(new Fake(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archiveBytes) })));
        var release = new ReleaseManifest(unsafeArchive ? 2 : 1, "test", "https://example.com/test.zip", Convert.ToHexString(SHA256.HashData(archiveBytes)), archiveBytes.Length);
        try
        {
            var installed = await ReleaseUpdates.Install(download, release, installRoot, default);
            if (unsafeArchive || !File.Exists(installed)) throw new Exception("Archive validation failed");
        }
        catch (InvalidDataException) when (unsafeArchive) { }
    }
}
finally { if (Directory.Exists(installRoot)) Directory.Delete(installRoot, true); }
Console.WriteLine("PASS: MFA, account identity, renewal, logout, token handoff, server addresses, signature verification, tampering, rollback, installation and path traversal refusal.");
static HttpResponseMessage Reply(object value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(JsonSerializer.Serialize(value)) };
sealed class Fake(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
}
