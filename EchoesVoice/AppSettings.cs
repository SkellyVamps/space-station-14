using System.Text.Json;

namespace EchoesVoice;

/// <summary>
/// What the app remembers between runs, in the user's app data folder.
/// </summary>
public sealed class AppSettings
{
    public string? Device { get; set; }
    public float Gain { get; set; } = 1f;
    public float NoiseSuppression { get; set; } = 0.5f;

    /// <summary>
    /// The last link used, to reconnect with on start (codes last until the player leaves that server).
    /// </summary>
    public string? LastLink { get; set; }

    /// <summary>
    /// Servers (host:port) the player agreed to send their microphone to. Links to any other server ask first.
    /// </summary>
    public List<string> TrustedServers { get; set; } = new();

    private static string Path => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EchoesVoice", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Path)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // Unreadable: start fresh.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Not worth bothering the player about.
        }
    }
}
