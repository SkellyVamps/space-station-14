using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Skelly.VoiceLauncher;

if (args.Length == 2 && args[0] == "keygen")
{
    if (File.Exists(args[1]) || File.Exists(args[1] + ".pub")) throw new IOException("Refusing to overwrite an existing key");
    using var rsa = RSA.Create(4096);
    var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
    if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    using (var file = new FileStream(args[1], options))
    using (var writer = new StreamWriter(file)) writer.Write(rsa.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(args[1] + ".pub", rsa.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine("Created private signing key and public key. Keep the private key outside the repository.");
}
else if (args.Length == 8 && args[0] == "sign")
{
    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(args[1]));
    using (var bundle = ZipFile.OpenRead(args[2]))
    {
        using var metadata = bundle.GetEntry("SkellySS14/build-info.json")?.Open() ?? throw new InvalidDataException("Missing metadata");
        using var info = JsonDocument.Parse(metadata);
        if (info.RootElement.GetProperty("release_sequence").GetInt64() != long.Parse(args[4]) ||
            info.RootElement.GetProperty("runtime").GetString() != args[5] || info.RootElement.GetProperty("source_dirty").GetBoolean())
            throw new InvalidDataException("Release sequence/runtime mismatch or dirty source tree");
    }
    using var archive = File.OpenRead(args[2]);
    var release = new ReleaseManifest(long.Parse(args[4]), args[5], args[3], Convert.ToHexString(SHA256.HashData(archive)), archive.Length);
    var payload = JsonSerializer.SerializeToUtf8Bytes(release);
    var envelope = JsonSerializer.Serialize(new SignedRelease(Convert.ToBase64String(payload),
        Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))));
    ReleaseUpdates.Verify(envelope, rsa.ExportSubjectPublicKeyInfoPem(), args[5], 0);
    File.WriteAllText(args[6], envelope);
    File.WriteAllText(args[7], JsonSerializer.Serialize(new { PublicKey = rsa.ExportSubjectPublicKeyInfoPem(), ManifestUrl = "" }));
    Console.WriteLine("Signed release manifest and public trust template written.");
}
else
{
    Console.Error.WriteLine("keygen <private-key-path>\nsign <private-key> <archive> <https-url> <sequence> <runtime> <manifest.json> <trust-template.json>");
    return 1;
}
return 0;
