using System;
using System.IO;
using System.Text;
using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Voice;

/// <summary>Fixed codec contract. Changes require a new protocol version, not a client CVar.</summary>
public static class VoiceProtocol
{
    public const byte Version = 1;
    public const int SampleRate = 48000;
    public const int FrameMilliseconds = 20;
    public const int SamplesPerFrame = 960;
    // Well below the transport MTU, including forwarding metadata and encryption.
    public const int MaxPayloadBytes = 512;
    public const int FramesPerSecond = 50;
}

/// <summary>No speaker identity is accepted from the transmitting client.</summary>
public enum VoiceTransmission : byte { Local, Radio, LocalAndRadio }

public sealed class MsgVoiceFrame : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.Unreliable;
    public uint Sequence;
    public uint ProfileRevision;
    public VoiceTransmission Transmission;
    public string RadioChannel = "";
    public byte[] Payload = Array.Empty<byte>();

    public override int EstimateBufferSize() => 13 + RadioChannel.Length + Payload.Length;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        if (buffer.ReadByte() != VoiceProtocol.Version)
            throw new InvalidDataException("Unsupported voice protocol");
        Sequence = buffer.ReadUInt32();
        ProfileRevision = buffer.ReadUInt32();
        Transmission = (VoiceTransmission) buffer.ReadByte();
        var channelLength = buffer.ReadByte();
        if (Transmission > VoiceTransmission.LocalAndRadio || channelLength > 32)
            throw new InvalidDataException("Invalid voice transmission");
        RadioChannel = Encoding.ASCII.GetString(buffer.ReadBytes(channelLength));
        var length = buffer.ReadUInt16();
        // Validate before allocating, and reject truncated/trailing data.
        if (length is 0 or > VoiceProtocol.MaxPayloadBytes || buffer.LengthBits - buffer.Position != length * 8)
            throw new InvalidDataException("Invalid voice frame length");
        Payload = buffer.ReadBytes(length);
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        if (Payload.Length is 0 or > VoiceProtocol.MaxPayloadBytes)
            throw new InvalidDataException("Invalid voice frame length");
        buffer.Write(VoiceProtocol.Version);
        buffer.Write(Sequence);
        buffer.Write(ProfileRevision);
        if (RadioChannel.Length > 32 || Transmission > VoiceTransmission.LocalAndRadio)
            throw new InvalidDataException("Invalid voice transmission");
        buffer.Write((byte) Transmission);
        buffer.Write((byte) RadioChannel.Length);
        buffer.Write(Encoding.ASCII.GetBytes(RadioChannel));
        buffer.Write((ushort) Payload.Length);
        buffer.Write(Payload);
    }
}
