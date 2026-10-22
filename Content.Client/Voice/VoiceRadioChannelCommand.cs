using Content.Shared.Radio;
using Content.Shared.Voice.Components;
using Robust.Client.Player;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Client.Voice;

/// <summary>
/// Picks the radio channel used by the radio push-to-talk key.
/// </summary>
public sealed partial class VoiceRadioChannelCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private VoiceTransmitSystem _transmit = default!;

    public override string Command => "voiceradio";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (_player.LocalEntity is not { } local || !EntityManager.TryGetComponent(local, out VoiceSpeakerComponent? speaker))
        {
            shell.WriteError(Loc.GetString("cmd-voiceradio-no-entity"));
            return;
        }

        if (args.Length == 0)
        {
            _transmit.SelectedRadioChannel = null;
            shell.WriteLine(Loc.GetString("cmd-voiceradio-default", ("channel", speaker.DefaultRadioChannel?.Id ?? "-")));
            shell.WriteLine(Loc.GetString("cmd-voiceradio-available", ("channels", string.Join(", ", speaker.RadioChannels))));
            return;
        }

        var channel = new ProtoId<RadioChannelPrototype>(args[0]);
        if (!speaker.RadioChannels.Contains(channel))
        {
            shell.WriteError(Loc.GetString("cmd-voiceradio-unavailable", ("channel", args[0])));
            return;
        }

        _transmit.SelectedRadioChannel = channel;
        shell.WriteLine(Loc.GetString("cmd-voiceradio-set", ("channel", args[0])));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length != 1
            || _player.LocalEntity is not { } local
            || !EntityManager.TryGetComponent(local, out VoiceSpeakerComponent? speaker))
        {
            return CompletionResult.Empty;
        }

        var options = new List<string>();
        foreach (var channel in speaker.RadioChannels)
        {
            options.Add(channel.Id);
        }

        return CompletionResult.FromHintOptions(options, Loc.GetString("cmd-voiceradio-hint"));
    }
}
