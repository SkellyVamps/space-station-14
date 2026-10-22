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

        VoiceWire.WritePayload(buffer, Payload);
    }
}
