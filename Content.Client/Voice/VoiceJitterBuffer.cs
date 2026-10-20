using System;
using System.Collections.Generic;

namespace Content.Client.Voice;

/// <summary>
/// Bounded reorder buffer. The playback clock calls TryRead once per 20 ms;
/// a null payload after startup requests one frame of decoder PLC. No catch-up bursts.
/// </summary>
public sealed class VoiceJitterBuffer
{
    private const int Capacity = 12;
    private const int InitialFrames = 3;
    private readonly Dictionary<uint, byte[]> _frames = new(Capacity);
    private uint _next;
    private bool _initialized;
    private bool _playing;
    private int _missing;

    public bool Enqueue(uint sequence, byte[] payload)
    {
        if (payload.Length is <= 0 or > Shared.Voice.VoiceProtocol.MaxPayloadBytes)
            return false;
        if (!_initialized)
        {
            _next = sequence;
            _initialized = true;
        }
        var delta = unchecked((int) (sequence - _next));
        if (delta < 0 || delta >= Capacity || _frames.Count >= Capacity)
            return false;
        return _frames.TryAdd(sequence, payload);
    }

    public bool TryRead(out byte[]? payload)
    {
        payload = null;
        if (!_initialized || (!_playing && _frames.Count < InitialFrames))
            return false;
        _playing = true;
        _frames.Remove(_next++, out payload);
        _missing = payload == null ? _missing + 1 : 0;
        if (_missing >= Capacity)
            Clear();
        return true;
    }

    public void Clear()
    {
        _frames.Clear();
        _initialized = false;
        _playing = false;
        _missing = 0;
    }
}
