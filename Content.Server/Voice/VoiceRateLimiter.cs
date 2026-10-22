namespace Content.Server.Voice;

/// <summary>
/// Token buckets limiting how much audio and how many packets a client may send.
/// </summary>
/// <remarks>
/// Audio is budgeted in milliseconds: a client can never send more audio than real time plus a small burst
/// allowance, no matter how it splits it into packets. The packet budget separately stops floods of tiny packets.
/// </remarks>
public sealed class VoiceRateLimiter
{
    // Allow slightly faster than real time so clock drift doesn't cause drops.
    private const double AudioRefillRate = 1.05;

    private double _audioMs;
    private double _packets;
    private TimeSpan _last;
    private bool _initialized;

    /// <summary>
    /// Total packets rejected by this limiter.
    /// </summary>
    public int Rejected { get; private set; }

    public bool TryConsume(TimeSpan now, int audioMs, float maxPacketsPerSecond, int burstMs)
    {
        var packetCapacity = Math.Max(5.0, maxPacketsPerSecond * 0.5);

        if (!_initialized)
        {
            _initialized = true;
            _audioMs = burstMs;
            _packets = packetCapacity;
            _last = now;
        }

        var elapsed = Math.Max(0, (now - _last).TotalSeconds);
        _last = now;

        _audioMs = Math.Min(burstMs, _audioMs + elapsed * 1000 * AudioRefillRate);
        _packets = Math.Min(packetCapacity, _packets + elapsed * maxPacketsPerSecond);

        if (_audioMs < audioMs || _packets < 1)
        {
            Rejected++;
            return false;
        }

        _audioMs -= audioMs;
        _packets -= 1;
        return true;
    }
}

/// <summary>
/// Detects duplicate or replayed sequence numbers in a 16-bit wrapping sequence space while still allowing
/// moderate reordering (which unreliable delivery produces).
/// </summary>
public sealed class VoiceSequenceWindow
{
    private const int WindowSize = 64;

    private ushort _highest;
    private ulong _seen;
    private bool _initialized;

    /// <summary>
    /// Returns false if <paramref name="sequence"/> was already seen or is too old to be useful.
    /// </summary>
    public bool Accept(ushort sequence)
    {
        if (!_initialized)
        {
            _initialized = true;
            _highest = sequence;
            _seen = 1;
            return true;
        }

        var diff = (short) (sequence - _highest);

        if (diff > 0)
        {
            // Newer. A big jump means the client restarted its sequence; treat it as a new stream.
            _seen = diff >= WindowSize ? 1UL : (_seen << diff) | 1UL;
            _highest = sequence;
            return true;
        }

        var age = -diff;
        if (age >= WindowSize)
        {
            // Very old: either a restarted client or a replay. Restart the window if it's far away.
            if (age > 1000)
            {
                _highest = sequence;
                _seen = 1;
                return true;
            }

            return false;
        }

        var bit = 1UL << age;
        if ((_seen & bit) != 0)
            return false;

        _seen |= bit;
        return true;
    }

    public void Reset()
    {
        _initialized = false;
    }
}
