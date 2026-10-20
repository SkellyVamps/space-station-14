# Signed portable releases

The launcher checks RSA/SHA-256 manifest signatures before trusting download metadata, verifies exact
archive size and SHA-256, rejects rollback/runtime mismatches and unsafe ZIP paths/symlinks, and installs
side-by-side. Original shortcuts forward to a previously verified installed update when available.
A failed update leaves the existing installation intact.

Updates are disabled until an owner-controlled signing key and HTTPS channel are configured. OS
Authenticode/notarization is separate and not implemented. Initial installation must come from a trusted
owner distribution channel. Keep private keys outside the repository and back them up securely.

```sh
dotnet run --project Voice.ReleaseTool -- keygen /secure/path/skelly-release.pem
```

Put the public `.pub` PEM text and a fixed HTTPS manifest URL into `Voice.Launcher/update-trust.json`,
then commit that public configuration. The URL can contain `{runtime}` for `win-x64` / `linux-x64`.
The trust key is embedded when compiling; the update server cannot supply a replacement trust key.

Build from a clean commit with a positive increasing release sequence:

```sh
python3 Tools/voice/package_custom_client.py --runtime linux-x64 --release-sequence 1
dotnet run --project Voice.ReleaseTool -- sign /secure/path/skelly-release.pem release/voice-client/SkellySS14-voice-dev-linux-x64.zip https://your-host/client-linux-x64.zip 1 linux-x64 manifest-linux-x64.json trust-template.json
```

The signer refuses dirty-source builds and sequence/runtime mismatches. Publish the archive at the
signed URL, then the envelope at the embedded manifest URL. Repeat for Windows. Never reuse a sequence
for a new release. The public trust template does not alter the key embedded in already installed clients.

Offline tests cover authentication, signatures, tampering, rollback and archive extraction. Owner-hosted
end-to-end updates, interrupted downloads, OS signing and platform installation tests remain required.
