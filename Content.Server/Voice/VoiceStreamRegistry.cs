using Robust.Shared.Network;

namespace Content.Server.Voice;

/// <summary>Listener-scoped opaque playback IDs; real account identities remain server-side.</summary>
public sealed class VoiceStreamRegistry
{
    private readonly Dictionary<(NetUserId Listener, NetUserId Speaker), Stream> _streams = new();
    private sealed record Stream(EntityUid Entity, uint Revision, NetUserId Id);
    public NetUserId Get(NetUserId listener, NetUserId speaker, EntityUid entity, uint revision)
    {
        var key = (listener, speaker);
        if (!_streams.TryGetValue(key, out var stream) || stream.Entity != entity || stream.Revision != revision)
            _streams[key] = stream = new Stream(entity, revision, new NetUserId(Guid.NewGuid()));
        return stream.Id;
    }
    public void Retain(HashSet<NetUserId> connected)
    {
        foreach (var key in new List<(NetUserId Listener, NetUserId Speaker)>(_streams.Keys))
            if (!connected.Contains(key.Listener) || !connected.Contains(key.Speaker)) _streams.Remove(key);
    }
}
