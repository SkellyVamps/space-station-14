using System;
using System.IO;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Voice;

/// <summary>Server-authenticated playback metadata. Never accepted from clients.</summary>
public sealed class MsgVoicePlayback : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.Unreliable;
    public NetUserId Speaker;
    public NetEntity Entity;
    public uint Sequence;
    public float Gain;
    public bool Radio;
    public byte[] Payload = Array.Empty<byte>();
    public override int EstimateBufferSize() => Payload.Length + 35;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Speaker = new NetUserId(new Guid(buffer.ReadBytes(16)));
        Entity = buffer.ReadNetEntity();
        Sequence = buffer.ReadUInt32();
        Gain = buffer.ReadFloat();
        Radio = buffer.ReadBoolean();
        buffer.ReadPadBits();
        var length = buffer.ReadUInt16();
        if (!float.IsFinite(Gain) || Gain is < 0 or > 1 || length is 0 or > VoiceProtocol.MaxPayloadBytes ||
            buffer.LengthBits - buffer.Position != length * 8)
            throw new InvalidDataException("Invalid voice playback frame");
        Payload = buffer.ReadBytes(length);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Speaker.UserId.ToByteArray());
        buffer.Write(Entity);
        buffer.Write(Sequence);
        buffer.Write(Gain);
        buffer.Write(Radio);
        buffer.WritePadBits();
        buffer.Write((ushort) Payload.Length);
        buffer.Write(Payload);
    }
}
