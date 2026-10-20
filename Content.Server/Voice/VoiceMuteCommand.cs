using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.Network;

namespace Content.Server.Voice;

[AdminCommand(AdminFlags.Ban)]
public sealed partial class VoiceMuteCommand : IConsoleCommand
{
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IAdminLogManager _logs = default!;
    public string Command => "voice_mute";
    public string Description => "Temporarily mute an account's voice; use 0 seconds to unmute.";
    public string Help => "voice_mute <account UUID> <seconds 0..604800>";
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2 || !Guid.TryParse(args[0], out var id) ||
            !int.TryParse(args[1], out var seconds) || seconds is < 0 or > 604800)
        {
            shell.WriteError(Help);
            return;
        }
        _systems.GetEntitySystem<VoiceModerationSystem>().SetMute(new NetUserId(id), TimeSpan.FromSeconds(seconds));
        _logs.Add(LogType.AdminCommands, LogImpact.Medium,
            $"Voice moderation by {shell.Player?.UserId.ToString() ?? "server console"}: {id}, duration {seconds} seconds");
        shell.WriteLine($"Voice mute for {id}: {seconds} seconds (0 removes mute).");
    }
}
