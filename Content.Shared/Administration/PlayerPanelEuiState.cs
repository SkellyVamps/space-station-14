using Content.Shared.Eui;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Administration;

[Serializable, NetSerializable]
public sealed class PlayerPanelEuiState(
    NetUserId guid,
    string username,
    TimeSpan playtime,
    int? totalNotes,
    int? totalBans,
    int? totalRoleBans,
    int sharedConnections,
    bool? whitelisted,
    bool canFreeze,
    bool frozen,
    bool canAhelp,
    float trustScore,
    DateTime? accountCreationDate,
    bool canVoiceMute = false,
    bool voiceMuted = false,
    TimeSpan? voiceMuteRemaining = null)
    : EuiStateBase
{
    public readonly NetUserId Guid = guid;
    public readonly string Username = username;
    public readonly TimeSpan Playtime = playtime;
    public readonly int? TotalNotes = totalNotes;
    public readonly int? TotalBans = totalBans;
    public readonly int? TotalRoleBans = totalRoleBans;
    public readonly int SharedConnections = sharedConnections;
    public readonly bool? Whitelisted = whitelisted;
    public readonly bool CanFreeze = canFreeze;
    public readonly bool Frozen = frozen;
    public readonly bool CanAhelp = canAhelp;
    public readonly float TrustScore = trustScore;
    public readonly DateTime? AccountCreationDate = accountCreationDate;

    /// <summary>
    /// The viewing admin may voice mute (Moderator flag).
    /// </summary>
    public readonly bool CanVoiceMute = canVoiceMute;

    public readonly bool VoiceMuted = voiceMuted;

    /// <summary>
    /// How long the voice mute still lasts; null while muted means until lifted.
    /// </summary>
    public readonly TimeSpan? VoiceMuteRemaining = voiceMuteRemaining;
}

/// <summary>
/// Voice mutes the player for <see cref="Minutes"/> (null: until lifted), or lifts their voice mute.
/// </summary>
[Serializable, NetSerializable]
public sealed class PlayerPanelVoiceMuteMessage(bool mute, int? minutes) : EuiMessageBase
{
    public readonly bool Mute = mute;
    public readonly int? Minutes = minutes;
}


[Serializable, NetSerializable]
public sealed class PlayerPanelFreezeMessage : EuiMessageBase
{
    public readonly bool Mute;

    public PlayerPanelFreezeMessage(bool mute = false)
    {
        Mute = mute;
    }
}

[Serializable, NetSerializable]
public sealed class PlayerPanelLogsMessage : EuiMessageBase;

[Serializable, NetSerializable]
public sealed class PlayerPanelDeleteMessage : EuiMessageBase;

[Serializable, NetSerializable]
public sealed class PlayerPanelRejuvenationMessage: EuiMessageBase;

[Serializable, NetSerializable]
public sealed class PlayerPanelFollowMessage: EuiMessageBase;
