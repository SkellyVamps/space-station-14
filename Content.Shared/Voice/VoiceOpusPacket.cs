using System;

namespace Content.Shared.Voice;

/// <summary>Bounded RFC 6716 section 3 framing validation. No audio decoding or allocations.</summary>
public static class VoiceOpusPacket
{
    public static bool IsValid(ReadOnlySpan<byte> packet)
    {
        if (packet.Length is < 2 or > VoiceProtocol.MaxPayloadBytes || (packet[0] & 4) != 0) return false;
        var config = packet[0] >> 3;
        var samples = config >= 16 ? 120 << (config & 3) : config >= 12 ? 480 << (config & 1) : (config & 3) == 3 ? 2880 : 480 << (config & 3);
        var code = packet[0] & 3;
        var count = code == 0 ? 1 : code == 3 ? packet[1] & 63 : 2;
        if (count == 0 || samples * count != VoiceProtocol.SamplesPerFrame) return false;
        var offset = code == 3 ? 2 : 1;
        var end = packet.Length;
        if (code == 3 && (packet[1] & 64) != 0)
        {
            int padding;
            do
            {
                if (offset >= end) return false;
                padding = packet[offset++];
                end -= padding == 255 ? 254 : padding;
                if (end < offset) return false;
            } while (padding == 255);
        }
        if (code == 0) return end - offset <= 1275;
        if (code == 1 || code == 3 && (packet[1] & 128) == 0) return (end - offset) % count == 0 && (end - offset) / count <= 1275;
        var total = 0;
        for (var frame = 0; frame < count - 1; frame++)
        {
            if (offset >= end) return false;
            var length = (int) packet[offset++];
            if (length >= 252) { if (offset >= end) return false; length += 4 * packet[offset++]; }
            if (length > 1275) return false;
            total += length;
        }
        return end - offset - total is >= 0 and <= 1275;
    }
}
