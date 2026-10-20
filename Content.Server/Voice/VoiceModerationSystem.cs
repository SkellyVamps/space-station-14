using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server.Voice;

/// <summary>Account mutes survive respawn/reconnect; intentionally no audio is stored.</summary>
public sealed partial class VoiceModerationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    private readonly Dictionary<NetUserId, TimeSpan> _mutes = new();
    public bool IsMuted(NetUserId user)
    {
        if (!_mutes.TryGetValue(user, out var until))
            return false;
        if (until > _timing.RealTime)
            return true;
        _mutes.Remove(user);
        return false;
    }
    public void SetMute(NetUserId user, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            _mutes.Remove(user);
        else
            _mutes[user] = _timing.RealTime + duration;
    }
}
