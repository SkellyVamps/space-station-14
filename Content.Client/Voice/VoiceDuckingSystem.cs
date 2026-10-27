using Content.Shared.CCVar;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;

namespace Content.Client.Voice;

/// <summary>
/// Lowers other game sounds while someone is heard talking (<see cref="CCVars.VoiceDucking"/>), so voices stay clear
/// in a loud room. Voice itself plays through its own streaming sources and is not affected.
/// </summary>
/// <remarks>
/// The engine resets every sound's volume to its own setting when it updates sounds, so this runs right after it and
/// lowers them again each frame. When ducking ends the engine's next update restores everything by itself.
/// </remarks>
public sealed partial class VoiceDuckingSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private VoicePlaybackSystem _playback = default!;

    /// <summary>
    /// How fast ducking fades in and out, in "full amount" per second.
    /// </summary>
    private const float AttackSpeed = 8f;
    private const float ReleaseSpeed = 2f;

    private float _amount;
    private float _current;
    private bool _wasDucking;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(AudioSystem));
        Subs.CVar(_cfg, CCVars.VoiceDucking, v => _amount = Math.Clamp(v, 0f, 0.9f), true);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var target = _amount > 0f && _playback.AnyVoicePlaying() ? _amount : 0f;
        _current = target > _current
            ? Math.Min(target, _current + AttackSpeed * frameTime)
            : Math.Max(target, _current - ReleaseSpeed * frameTime);

        if (_current <= 0.001f)
        {
            if (_wasDucking)
            {
                Apply(0f);
                _wasDucking = false;
            }

            return;
        }

        _wasDucking = true;
        Apply(SharedAudioSystem.GainToVolume(1f - _current));
    }

    private void Apply(float offsetDb)
    {
        var query = AllEntityQuery<AudioComponent>();
        while (query.MoveNext(out var audio))
        {
            // Out of range or on another map: the engine silenced it, leave it silent.
            if (audio.Gain <= 0f)
                continue;

            audio.Volume = audio.Params.Volume + offsetDb;
        }
    }
}
