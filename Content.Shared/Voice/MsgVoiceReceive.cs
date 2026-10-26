using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Voice;

public enum VoiceReceiveMode : byte
{
    /// <summary>
    /// Heard in person; positional at <see cref="MsgVoiceReceive.Speaker"/>.
    /// </summary>
    Local = 0,

    /// <summary>
    /// Heard through a radio receiver; not positional, radio effects applied.
    /// </summary>
    Radio = 1,

    /// <summary>
    /// A radio transmission played out loud by a device with a speaker (intercom, handheld radio): positional at
    /// <see cref="MsgVoiceReceive.Speaker"/>, which is the device, with radio effects and acoustics applied.
    /// </summary>
    Loudspeaker = 2,
}

[Flags]
public enum VoiceReceiveFlags : byte
{
    None = 0,
    EndOfSpurt = 1 << 0,

    /// <summary>
    /// The message carries <see cref="MsgVoiceReceive.SpeakerName"/>.
    /// </summary>
    HasName = 1 << 1,

    /// <summary>
    /// The speaker is a ghost (only relevant to clients that are allowed to hear ghosts).
    /// </summary>
    Ghost = 1 << 2,
}

/// <summary>
/// Server to client: one voice frame the receiving player can hear, with the server-computed audibility.
/// </summary>
public sealed class MsgVoiceReceive : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.Unreliable;

    public VoiceReceiveMode Mode;

    public VoiceReceiveFlags Flags;

    /// <summary>
    /// Opaque stream identifier, unique per speaker and mode (and channel, for radio). Radio streams deliberately use
    /// different IDs from local streams so a client cannot link a radio voice to a body it can see.
    /// </summary>
    public ushort StreamId;

    public ushort Sequence;

    /// <summary>
    /// Speaking entity for <see cref="VoiceReceiveMode.Local"/>; the playing device for
    /// <see cref="VoiceReceiveMode.Loudspeaker"/>. Not sent for <see cref="VoiceReceiveMode.Radio"/>.
    /// </summary>
    public NetEntity Speaker;

    /// <summary>
    /// Volume the receiver should play this frame at, quantized 0-255 (0-1).
    /// </summary>
    public byte Gain;

    /// <summary>
    /// How muffled the voice is (walls, doors, containers, masks on the listener...), quantized 0-255 (0-1).
    /// </summary>
    public byte Muffle;

    /// <summary>
    /// Radio channel prototype ID for <see cref="VoiceReceiveMode.Radio"/> and <see cref="VoiceReceiveMode.Loudspeaker"/>.
    /// </summary>
    public string? Channel;

    /// <summary>
    /// Display name of the speaker (as radio would show it), sent occasionally on radio streams.
    /// </summary>
    public string? SpeakerName;

    public byte[] Payload = Array.Empty<byte>();

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Mode = (VoiceReceiveMode) buffer.ReadByte();
        Flags = (VoiceReceiveFlags) buffer.ReadByte();
        StreamId = buffer.ReadUInt16();
        Sequence = buffer.ReadUInt16();
        Gain = buffer.ReadByte();
        Muffle = buffer.ReadByte();

        if (Mode != VoiceReceiveMode.Radio)
            Speaker = new NetEntity(buffer.ReadInt32());

        if (Mode != VoiceReceiveMode.Local)
            Channel = buffer.ReadString();

        if ((Flags & VoiceReceiveFlags.HasName) != 0)
            SpeakerName = buffer.ReadString();

        Payload = VoiceWire.ReadPayload(buffer);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        var flags = Flags;
        if (SpeakerName != null)
            flags |= VoiceReceiveFlags.HasName;
        else
            flags &= ~VoiceReceiveFlags.HasName;

        buffer.Write((byte) Mode);
        buffer.Write((byte) flags);
        buffer.Write(StreamId);
        buffer.Write(Sequence);
        buffer.Write(Gain);
        buffer.Write(Muffle);

        if (Mode != VoiceReceiveMode.Radio)
            buffer.Write(Speaker.Id);

        if (Mode != VoiceReceiveMode.Local)
            buffer.Write(Channel ?? string.Empty);

        if (SpeakerName != null)
            buffer.Write(SpeakerName);

        VoiceWire.WritePayload(buffer, Payload);
    }
}
