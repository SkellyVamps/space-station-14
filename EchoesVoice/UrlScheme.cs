using System.Diagnostics;
using System.Runtime.Versioning;

namespace EchoesVoice;

/// <summary>
/// Makes <c>echoesvoice://</c> links (the voice page's "Open in the app" button) open this app. Registered for the
/// current user only, every time the app starts, so moving the app just works.
/// </summary>
public static class UrlScheme
{
    public static void Register()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null)
                return;

            if (OperatingSystem.IsWindows())
                RegisterWindows(exe);
            else if (OperatingSystem.IsLinux())
                RegisterLinux(exe);
            // macOS: declared in the app bundle's Info.plist instead.
        }
        catch (Exception)
        {
            // Links just won't open the app; pasting still works.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterWindows(string exe)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + VoiceLink.Scheme);
        key.SetValue(string.Empty, "URL:Echoes Voice");
        key.SetValue("URL Protocol", string.Empty);
        using var icon = key.CreateSubKey("DefaultIcon");
        icon.SetValue(string.Empty, $"\"{exe}\",0");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue(string.Empty, $"\"{exe}\" \"%1\"");
    }

    private static void RegisterLinux(string exe)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share",
            "applications");
        Directory.CreateDirectory(dir);
        var desktop = Path.Combine(dir, "echoesvoice.desktop");
        File.WriteAllText(desktop, $"""
            [Desktop Entry]
            Type=Application
            Name=Echoes Voice
            Comment=Voice chat for Space Station 14, Echoes of the Void
            Exec="{exe}" %u
            Terminal=false
            Categories=Game;AudioVideo;
            MimeType=x-scheme-handler/{VoiceLink.Scheme};
            """);

        Process.Start(new ProcessStartInfo("xdg-mime", $"default echoesvoice.desktop x-scheme-handler/{VoiceLink.Scheme}")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        })?.WaitForExit(3000);
    }
}
