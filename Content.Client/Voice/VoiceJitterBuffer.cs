namespace Content.Client.Voice;

/// <summary>
/// Reorders incoming voice packets and decides, each time playback needs audio, whether to play a packet, conceal a
/// lost one (optionally with forward error correction from the next packet), or wait.
/// </summary>
/// <remarks>
/// Pure logic with no audio dependencies so it can be tested in isolation. The depth adapts: it grows after
/// underruns (bad network) and slowly shrinks again while playback is smooth.
/// </remarks>
public sealed class VoiceJitterBuffer
{
    public const int Capacity = 64;

    /// <summary>
    /// After this many consecutive concealed frames with nothing to play, playback stops instead of droning on.
    /// </summary>
    public const int MaxConcealedFrames = 4;

    private readonly Entry[] _entries = new Entry[Capacity];
    private int _count;
    private ushort _next;
    private bool _started;
    private bool _endOfSpurt;
    private int _concealed;
    private TimeSpan _firstArrival;
    private TimeSpan _lastUnderrun;

    /// <summary>
    /// Target buffered depth, in frames, before playback starts.
    /// </summary>
    public int TargetDepth { get; private set; }

    public int MinDepth { get; set; } = 1;
    public int MaxDepth { get; set; } = 10;

    /// <summary>
    /// How long to wait for <see cref="TargetDepth"/> before starting anyway (short utterances).
    /// </summary>
    public TimeSpan MaxStartDelay { get; set; } = TimeSpan.FromMilliseconds(120);

    public bool Playing => _started;
    public int Buffered => _count;

    public int Underruns { get; private set; }
    public int LatePackets { get; private set; }
    public int ConcealedFrames { get; private set; }

    public VoiceJitterBuffer(int targetDepth)
    {
        TargetDepth = Math.Clamp(targetDepth, MinDepth, MaxDepth);
    }

    public void SetTargetDepth(int depth)
    {
        TargetDepth = Math.Clamp(depth, MinDepth, MaxDepth);
    }

    public void Push(ushort sequence, byte[] payload, bool endOfSpurt, TimeSpan now)
    {
        if (payload.Length == 0)
        {
            if (endOfSpurt)
                _endOfSpurt = true;

            return;
        }

        if (_started)
        {
            var ahead = (short) (sequence - _next);
            if (ahead < 0)
            {
                // Already played or concealed. A huge negative jump means the sender restarted.
                if (ahead > -100)
                {
                    LatePackets++;
                    return;
                }

                Reset();
            }
            else if (ahead > Capacity)
            {
                // Way ahead: sender restarted or we fell far behind. Resync.
                Reset();
            }
        }

        if (_count == 0)
        {
            _firstArrival = now;

            // First packet of a new spurt: forget a stale end marker from the previous one.
            if (!_started)
                _endOfSpurt = false;
        }

        if (endOfSpurt)
            _endOfSpurt = true;

        var slot = sequence % Capacity;
        if (_entries[slot].Valid && _entries[slot].Sequence == sequence)
            return; // duplicate

        if (!_entries[slot].Valid)
            _count++;

        _entries[slot] = new Entry(sequence, payload);
    }

    /// <summary>
    /// Gets what to play next.
    /// </summary>
    public JitterResult Pop(TimeSpan now, out byte[]? payload)
    {
        payload = null;

        if (!_started)
        {
            if (_count == 0)
                return JitterResult.Idle;

            var depthReached = _count >= TargetDepth;
            if (!depthReached && !_endOfSpurt && now - _firstArrival < MaxStartDelay)
                return JitterResult.Idle;

            _started = true;
            _concealed = 0;
            _next = Oldest();
        }

        var slot = _next % Capacity;
        if (_entries[slot].Valid && _entries[slot].Sequence == _next)
        {
            payload = _entries[slot].Payload;
            _entries[slot] = default;
            _count--;
            _next++;
            _concealed = 0;
            MaybeShrink(now);
            return JitterResult.Packet;
        }

        if (_count == 0)
        {
            if (_endOfSpurt)
            {
                // Normal end of a talk spurt.
                Stop();
                return JitterResult.Idle;
            }

            // Underrun: nothing at all to play.
            if (++_concealed > MaxConcealedFrames)
            {
                Underruns++;
                _lastUnderrun = now;
                SetTargetDepth(TargetDepth + 1);
                Stop();
                return JitterResult.Idle;
            }

            _next++;
            ConcealedFrames++;
            return JitterResult.Lost;
        }

        // A gap: this packet is missing but later ones exist. Try FEC from the next packet.
        var nextSlot = (ushort) (_next + 1) % Capacity;
        _next++;
        ConcealedFrames++;
        _concealed++;

        if (_entries[nextSlot].Valid && _entries[nextSlot].Sequence == _next)
        {
            payload = _entries[nextSlot].Payload;
            return JitterResult.LostWithFec;
        }

        return JitterResult.Lost;
    }

    private void MaybeShrink(TimeSpan now)
    {
        if (TargetDepth > MinDepth && now - _lastUnderrun > TimeSpan.FromSeconds(15))
        {
            SetTargetDepth(TargetDepth - 1);
            _lastUnderrun = now;
        }
    }

    private ushort Oldest()
    {
        ushort best = 0;
        var found = false;
        foreach (var entry in _entries)
        {
            if (!entry.Valid)
                continue;

            if (!found || (short) (entry.Sequence - best) < 0)
            {
                best = entry.Sequence;
                found = true;
            }
        }

        return best;
    }

    private void Stop()
    {
        _started = false;
        _endOfSpurt = false;
        _concealed = 0;
    }

    public void Reset()
    {
        Array.Clear(_entries);
        _count = 0;
        Stop();
    }

    private readonly record struct Entry(ushort Sequence, byte[] Payload)
    {
        public bool Valid => Payload != null;
    }
}

public enum JitterResult : byte
{
    /// <summary>
    /// Nothing to play (not started, or the talk spurt ended).
    /// </summary>
    Idle,

    /// <summary>
    /// Play the returned packet.
    /// </summary>
    Packet,

    /// <summary>
    /// The packet is missing: generate concealment.
    /// </summary>
    Lost,

    /// <summary>
    /// The packet is missing but the returned next packet carries FEC data for it.
    /// </summary>
    LostWithFec,
}
