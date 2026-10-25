using Content.Shared.CCVar;
using Content.Shared.Speech;
using Content.Shared.Speech.Components;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Voice;

/// <summary>
/// Creatures that can't talk (hamsters, mice, dogs, monkeys, zombies...): nobody can understand them, so their
/// microphone audio is never sent to anyone. While they talk, their usual speech sound plays now and then instead.
/// </summary>
/// <remarks>
/// Follows text chat: anything whose text speech is fully replaced ("Squeak!") counts, so voice and text always agree.
/// They still hear everyone else normally.
/// </remarks>
public sealed partial class VoiceCreatureSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SpeechSoundSystem _speechSound = default!;

    /// <summary>
    /// A pause longer than this starts a new talk spurt even if its end packet was lost.
    /// </summary>
    private static readonly TimeSpan SpurtGap = TimeSpan.FromSeconds(0.5);

    private readonly Dictionary<EntityUid, SpurtState> _spurts = new();
    private readonly List<EntityUid> _expired = new();

    private TimeSpan _firstDelay;
    private TimeSpan _interval;
    private TimeSpan _nextPrune;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReplacementAccentComponent, VoiceIntelligibilityEvent>(OnReplacementAccent);
        SubscribeLocalEvent<MonkeyAccentComponent, VoiceIntelligibilityEvent>(OnUnintelligible);
        SubscribeLocalEvent<VoiceUnintelligibleComponent, VoiceIntelligibilityEvent>(OnUnintelligible);

        Subs.CVar(_cfg, CCVars.VoiceCreatureNoiseDelay, v => _firstDelay = TimeSpan.FromSeconds(Math.Clamp(v, 0f, 5f)), true);
        Subs.CVar(_cfg, CCVars.VoiceCreatureNoiseInterval, v => _interval = TimeSpan.FromSeconds(Math.Clamp(v, 0.3f, 30f)), true);
    }

    private void OnReplacementAccent(Entity<ReplacementAccentComponent> ent, ref VoiceIntelligibilityEvent args)
    {
        // Word replacements (pirate, dwarf...) stay understandable; full replacements ("Squeak!") don't.
        if (_proto.TryIndex(ent.Comp.Accent, out var accent) && accent.FullReplacements != null)
            args.Unintelligible = true;
    }

    private void OnUnintelligible<T>(Entity<T> ent, ref VoiceIntelligibilityEvent args) where T : IComponent
    {
        args.Unintelligible = true;
    }

    /// <summary>
    /// Whether nobody can understand <paramref name="speaker"/>'s speech.
    /// </summary>
    public bool IsUnintelligible(EntityUid speaker)
    {
        var ev = new VoiceIntelligibilityEvent();
        RaiseLocalEvent(speaker, ref ev);
        return ev.Unintelligible;
    }

    /// <summary>
    /// Called for each voice frame an unintelligible creature sends, instead of routing it.
    /// </summary>
    public void OnSpeech(EntityUid speaker, bool endOfSpurt)
    {
        var now = _timing.RealTime;

        if (endOfSpurt)
        {
            _spurts.Remove(speaker);
            return;
        }

        if (!_spurts.TryGetValue(speaker, out var state) || now - state.LastFrame > SpurtGap)
        {
            state = new SpurtState { NextNoise = now + _firstDelay };
            _spurts[speaker] = state;
        }

        state.LastFrame = now;

        if (now < state.NextNoise)
            return;

        // Slightly irregular, so it sounds like an animal and not a metronome.
        state.NextNoise = now + _interval * _random.NextFloat(0.75f, 1.25f);
        PlayNoise(speaker);
    }

    private void PlayNoise(EntityUid speaker)
    {
        if (!TryComp(speaker, out SpeechComponent? speech) || !speech.Enabled || speech.SpeechSounds == null)
            return;

        // Mostly plain noises, sometimes a questioning or excited one.
        var message = _random.Next(6) switch
        {
            0 => "?",
            1 => "!",
            _ => ".",
        };

        if (_speechSound.GetSpeechSound((speaker, speech), message) is { } sound)
            _audio.PlayPvs(sound, speaker);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.RealTime;
        if (now < _nextPrune)
            return;

        _nextPrune = now + TimeSpan.FromSeconds(10);

        foreach (var (uid, state) in _spurts)
        {
            if (now - state.LastFrame > SpurtGap || TerminatingOrDeleted(uid))
                _expired.Add(uid);
        }

        foreach (var uid in _expired)
        {
            _spurts.Remove(uid);
        }

        _expired.Clear();
    }

    private sealed class SpurtState
    {
        public TimeSpan LastFrame;
        public TimeSpan NextNoise;
    }
}
