using System;

namespace Content.Shared.Voice;

/// <summary>
/// Per-authenticated-connection admission control, independent of client stream IDs.
/// Call on the game thread with monotonic server time. Destroy on disconnect.
/// </summary>
public sealed class VoiceIngressGuard
{
    private const double Burst = 8;
    private double _tokens = Burst;
    private TimeSpan _updated;
    private TimeSpan _lastAccepted;
    private uint _sequence;
    private bool _started;

    public bool TryAccept(uint sequence, int bytes, TimeSpan now, bool permitted)
    {
        var elapsed = Math.Max(0, (now - _updated).TotalSeconds);
        _tokens = Math.Min(Burst, _tokens + elapsed * VoiceProtocol.FramesPerSecond);
        _updated = now;
        // Charge invalid attempts too; stream changes must never refill the bucket.
        if (_tokens < 1)
            return false;
        _tokens--;
        if (!permitted || bytes is <= 0 or > VoiceProtocol.MaxPayloadBytes)
            return false;
        // Signed modular subtraction handles uint wrap. After an idle timeout allow a restart.
        if (_started && now - _lastAccepted < TimeSpan.FromSeconds(2) && unchecked((int) (sequence - _sequence)) <= 0)
            return false;
        _started = true;
        _sequence = sequence;
        _lastAccepted = now;
        return true;
    }
}
