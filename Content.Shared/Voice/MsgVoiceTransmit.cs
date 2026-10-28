using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Voice;

[Flags]
public enum VoiceTransmitFlags : byte
{
    None = 0,

    /// <summary>
    /// Transmit over radio as well as locally.
    /// </summary>
    Radio = 1 << 0,

    /// <summary>
    /// Last packet of a talk spurt (push to talk released / voice activation ended).
    /// </summary>
    EndOfSpurt = 1 << 1,

    /// <summary>
    /// Talking into a device the speaker is operating (holding an intercom's transmit button), see
    /// <see cref="MsgVoiceTransmit.Device"/>. Not combined with <see cref="Radio"/>.
    /// </summary>
    Device = 1 << 2,
}

/// <summary>
/// Client to server: one encoded voice frame from the local player.
/// </summary>
/// <remarks>
/// Deliberately does not contain a speaker entity: the server derives the speaker from the sending session.
/// </remarks>
public sealed class MsgVoiceTransmit : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.Unreliable;

    public VoiceTransmitFlags Flags;

    /// <summary>
    /// Per-client sequence number, incremented each frame. Wraps around.
    /// </summary>
    public ushort Sequence;

    /// <summary>
    /// Requested radio channel when <see cref="VoiceTransmitFlags.Radio"/> is set. Null for the default channel.
    /// </summary>
    public string? RadioChannel;

    /// <summary>
    /// The device talked into when <see cref="VoiceTransmitFlags.Device"/> is set. The server checks the speaker is
    /// actually operating it.
    /// </summary>
    public NetEntity Device;

    /// <summary>
    /// Opus payload. May be empty on an <see cref="VoiceTransmitFlags.EndOfSpurt"/> packet.
    /// </summary>
    public byte[] Payload = Array.Empty<byte>();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Flags = (VoiceTransmitFlags) buffer.ReadByte();
        Sequence = buffer.ReadUInt16();

        if ((Flags & VoiceTransmitFlags.Radio) != 0 && buffer.ReadBoolean())
        {
            RadioChannel = buffer.ReadString();
            if (RadioChannel.Length > VoiceConstants.MaxWireStringLength)
                throw new InvalidOperationException("Radio channel ID too long.");
        }

        if ((Flags & VoiceTransmitFlags.Device) != 0)
            Device = new NetEntity(buffer.ReadInt32());

        Payload = VoiceWire.ReadPayload(buffer);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write((byte) Flags);
        buffer.Write(Sequence);

        if ((Flags & VoiceTransmitFlags.Radio) != 0)
        {
            buffer.Write(RadioChannel != null);
            if (RadioChannel != null)
                buffer.Write(RadioChannel);
        }

        if ((Flags & VoiceTransmitFlags.Device) != 0)
            buffer.Write(Device.Id);

        VoiceWire.WritePayload(buffer, Payload);
    }
}
