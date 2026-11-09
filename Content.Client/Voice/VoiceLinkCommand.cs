using Robust.Shared.Console;

namespace Content.Client.Voice;

/// <summary>
/// Uses the voice webpage or the standalone voice app as the microphone: opens the page, copies the link, prints it,
/// or goes back to the game's microphone.
/// </summary>
public sealed partial class VoiceLinkCommand : LocalizedEntityCommands
{
    [Dependency] private VoiceExternalSystem _external = default!;

    public override string Command => "voicelink";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        switch (args.Length > 0 ? args[0] : "open")
        {
            case "open":
                _external.OpenWebpage();
                break;
            case "copy":
                _external.CopyLink();
                shell.WriteLine(Loc.GetString("cmd-voicelink-copied"));
                break;
            case "print":
                void Print(string link)
                {
                    _external.LinkReceived -= Print;
                    shell.WriteLine(link);
                }

                _external.LinkReceived += Print;
                _external.RequestLink();
                break;
            case "off":
                _external.Disconnect();
                break;
            default:
                shell.WriteError(Loc.GetString("cmd-voicelink-help"));
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromOptions(new[] { "open", "copy", "print", "off" })
            : CompletionResult.Empty;
    }
}
