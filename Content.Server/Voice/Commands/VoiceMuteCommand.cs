using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server.Voice.Commands;

[AdminCommand(AdminFlags.Moderator)]
public sealed partial class VoiceMuteCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private VoiceModerationSystem _moderation = default!;

    public override string Command => "voicemute";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 1)
        {
            shell.WriteError(Loc.GetString("cmd-voicemute-help"));
            return;
        }

        TimeSpan? duration = null;
        if (args.Length >= 2)
        {
            if (!float.TryParse(args[1], out var minutes) || minutes < 0)
            {
                shell.WriteError(Loc.GetString("cmd-voicemute-invalid-minutes"));
                return;
            }

            if (minutes > 0)
                duration = TimeSpan.FromMinutes(minutes);
        }

        var reason = args.Length >= 3 ? string.Join(' ', args[2..]) : "No reason given";
        var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("cmd-voicemute-no-player", ("player", args[0])));
            return;
        }

        _moderation.AdminMute(located.UserId, located.Username, duration, reason, shell.Player);
        shell.WriteLine(Loc.GetString("cmd-voicemute-success", ("player", located.Username)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), Loc.GetString("cmd-voicemute-hint-player")),
            2 => CompletionResult.FromHint(Loc.GetString("cmd-voicemute-hint-minutes")),
            3 => CompletionResult.FromHint(Loc.GetString("cmd-voicemute-hint-reason")),
            _ => CompletionResult.Empty,
        };
    }
}

[AdminCommand(AdminFlags.Moderator)]
public sealed partial class VoiceUnmuteCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerLocator _locator = default!;
    [Dependency] private VoiceModerationSystem _moderation = default!;

    public override string Command => "voiceunmute";

    public override async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("shell-need-exactly-one-argument"));
            return;
        }

        var located = await _locator.LookupIdByNameOrIdAsync(args[0]);
        if (located == null)
        {
            shell.WriteError(Loc.GetString("cmd-voicemute-no-player", ("player", args[0])));
            return;
        }

        shell.WriteLine(_moderation.AdminUnmute(located.UserId, located.Username, shell.Player)
            ? Loc.GetString("cmd-voiceunmute-success", ("player", located.Username))
            : Loc.GetString("cmd-voiceunmute-not-muted", ("player", located.Username)));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), Loc.GetString("cmd-voicemute-hint-player"))
            : CompletionResult.Empty;
    }
}

[AdminCommand(AdminFlags.Moderator)]
public sealed partial class VoiceMutesCommand : LocalizedEntityCommands
{
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private VoiceModerationSystem _moderation = default!;
    [Dependency] private Robust.Shared.Timing.IGameTiming _timing = default!;

    public override string Command => "voicemutes";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var any = false;
        foreach (var (user, mute) in _moderation.GetAdminMutes())
        {
            any = true;
            var name = _players.TryGetSessionById(user, out var session) ? session.Name : user.ToString();
            var remaining = mute.Expires is { } e ? $"{(e - _timing.RealTime).TotalMinutes:0.#} min" : "indefinite";
            shell.WriteLine($"{name}: {remaining}, by {mute.Admin}: {mute.Reason}");
        }

        if (!any)
            shell.WriteLine(Loc.GetString("cmd-voicemutes-none"));
    }
}

[AdminCommand(AdminFlags.Debug)]
public sealed partial class VoiceStatsCommand : LocalizedEntityCommands
{
    [Dependency] private VoiceSystem _voice = default!;
    [Dependency] private VoiceAudibilitySystem _audibility = default!;

    public override string Command => "voicestats";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var s = _voice.Stats;
        var avgRecipientUs = s.RecipientUpdates == 0 ? 0 : s.RecipientTime.TotalMilliseconds * 1000 / s.RecipientUpdates;
        var avgFieldUs = _audibility.FieldsComputed == 0 ? 0 : _audibility.FieldComputeTime.TotalMilliseconds * 1000 / _audibility.FieldsComputed;

        shell.WriteLine($"Active speakers: {_voice.ActiveSpeakers}");
        shell.WriteLine($"Packets: received {s.PacketsReceived}, rejected {s.PacketsRejected}, muted {s.PacketsMuted}, sent {s.PacketsSent} ({s.BytesSent / 1024} KiB)");
        shell.WriteLine($"Recipient updates: {s.RecipientUpdates}, avg {avgRecipientUs:0.0} us; streams capped {s.StreamsCapped}");
        shell.WriteLine($"Creature frames replaced by noises: {s.PacketsReplaced}");
        shell.WriteLine($"Acoustic fields: computed {_audibility.FieldsComputed} (avg {avgFieldUs:0.0} us), cache hits {_audibility.FieldCacheHits}");
    }
}
