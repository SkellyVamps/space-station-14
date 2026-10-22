using Lidgren.Network;

namespace Content.Shared.Voice;

internal static class VoiceWire
{
    public static byte[] ReadPayload(NetIncomingMessage buffer)
    {
        var length = buffer.ReadVariableInt32();
        if (length < 0 || length > VoiceConstants.MaxWirePayloadBytes)
            throw new InvalidOperationException($"Voice payload length {length} out of range.");

        if (length == 0)
            return Array.Empty<byte>();

        return buffer.ReadBytes(length);
    }

    public static void WritePayload(NetOutgoingMessage buffer, byte[] payload)
    {
        buffer.WriteVariableInt32(payload.Length);
        buffer.Write(payload);
    }
}
