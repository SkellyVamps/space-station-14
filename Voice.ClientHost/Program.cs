using Robust.Client;
using Robust.Shared;

namespace Skelly.VoiceClient;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Resource paths must work when launched from a desktop shortcut or another directory.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        var resources = new[] { "engine-resources.zip", "game-content.zip" };
        if (!args.Contains("--help"))
        {
            foreach (var path in resources)
            {
                if (File.Exists(path))
                    continue;
                Console.Error.WriteLine($"Missing {path}. Extract the complete Skelly SS14 client bundle before launching.");
                return 1;
            }
        }
        ContentStart.StartEngineLibrary(args, new GameControllerOptions
        {
            Sandboxing = true,
            ResourceMountDisabled = true,
            LoadContentResources = false,
            UserDataDirectoryName = "Skelly SS14 Voice",
            ConfigFileName = "client_config.toml",
            DefaultWindowTitle = "Skelly SS14 Voice — Development",
            MountOptions = new MountOptions
            {
                ZipMounts = new List<string>(resources),
            },
        });
        return 0;
    }
}
