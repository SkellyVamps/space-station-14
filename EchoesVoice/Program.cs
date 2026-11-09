using System.IO.Pipes;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace EchoesVoice;

public static class Program
{
    private static string PipeName => "EchoesVoice-" + Environment.UserName;

    /// <summary>
    /// A link handed to us by the system (an echoesvoice:// link) or by a second copy of the app.
    /// </summary>
    public static event Action<string>? LinkReceived;

    public static string? StartLink { get; private set; }
    public static bool TestTone { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        // macOS: use the OpenAL Soft that ships with the app, like the game does, not Apple's old one.
        if (OperatingSystem.IsMacOS())
            OpenTK.Audio.OpenAL.OpenALLibraryNameContainer.OverridePath = "libopenal.1.dylib";

        // Troubleshooting: which microphones OpenAL sees.
        if (args.Contains("--list-devices"))
        {
            try
            {
                Console.WriteLine("Default: " + OpenTK.Audio.OpenAL.ALC.GetString(OpenTK.Audio.OpenAL.ALDevice.Null,
                    OpenTK.Audio.OpenAL.AlcGetString.CaptureDefaultDeviceSpecifier));
                foreach (var device in OpenTK.Audio.OpenAL.ALC.GetStringList(
                             OpenTK.Audio.OpenAL.GetEnumerationStringList.CaptureDeviceSpecifier))
                {
                    Console.WriteLine("Device: " + device);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("OpenAL error: " + e);
            }

            return;
        }

        TestTone = args.Contains("--test-tone");
        StartLink = args.FirstOrDefault(a => !a.StartsWith("--"));

        // Already running: give it the link and get out of the way.
        if (SendToRunningApp(StartLink))
            return;

        Task.Run(ListenForLinks);
        UrlScheme.Register();

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .StartWithClassicDesktopLifetime(args);
    }

    private static bool SendToRunningApp(string? link)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(300);
            using var writer = new StreamWriter(pipe);
            writer.WriteLine(link ?? string.Empty);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static async Task ListenForLinks()
    {
        while (true)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe);
                var line = (await reader.ReadLineAsync())?.Trim() ?? string.Empty;
                LinkReceived?.Invoke(line);
            }
            catch (Exception)
            {
                await Task.Delay(1000);
            }
        }
    }
}

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }
}
