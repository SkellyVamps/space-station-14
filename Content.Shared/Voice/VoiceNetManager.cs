using Robust.Shared.Network;

namespace Content.Shared.Voice;

/// <summary>
/// Registers the voice net messages exactly once per process and forwards them to whichever voice system is alive.
/// </summary>
/// <remarks>
/// Net messages can't be registered from entity systems: client systems are torn down and re-initialized on every
/// reconnect and would register twice.
/// </remarks>
public sealed class VoiceNetManager
{
    [Dependency] private INetManager _net = default!;

    /// <summary>
    /// Server: a client sent a voice frame.
    /// </summary>
    public event Action<MsgVoiceTransmit>? TransmitReceived;

    /// <summary>
    /// Client: the server forwarded a voice frame.
    /// </summary>
    public event Action<MsgVoiceReceive>? VoiceReceived;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgVoiceTransmit>(msg => TransmitReceived?.Invoke(msg));
        _net.RegisterNetMessage<MsgVoiceReceive>(msg => VoiceReceived?.Invoke(msg));
    }
}
