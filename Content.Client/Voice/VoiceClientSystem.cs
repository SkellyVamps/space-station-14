using Content.Shared.CCVar;
using System.Linq;
using Content.Shared.Input;
using Content.Shared.Voice;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Audio.Sources;
using Robust.Shared.Configuration;
using Robust.Shared.Input.Binding;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

/// <summary>Opt-in PTT capture, Opus, bounded receive streams, and positional streaming playback.</summary>
public sealed partial class VoiceClientSystem : EntitySystem
{
    [Dependency] private IAudioManager _audio = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly Dictionary<NetUserId, Playback> _streams = new();
    private readonly Dictionary<NetUserId, float> _volumes = new();
    private readonly short[] _pcm = new short[VoiceProtocol.SamplesPerFrame];
    private readonly byte[] _encoded = new byte[VoiceProtocol.MaxPayloadBytes];
    private readonly int[] _processedHandles = new int[4];
    private readonly int[] _queuedHandles = new int[1];
    private IAudioCapture? _capture;
    private VoiceEncoder? _encoder;
    private uint _sequence;
    private VoiceDspPipeline _dsp = new();
    private bool _held;
    private bool _radioHeld;
    public bool MicrophoneTest { get; private set; }
    private EntityUid? _captureEntity;
    private string _captureDevice = "";
    private TimeSpan _retryAfter;
    private TimeSpan _playbackRetryAfter;
    public float MicrophoneLevel { get; private set; }
    public bool Transmitting => _capture != null;
    public string? CaptureError { get; private set; }

    private sealed class Playback : IDisposable
    {
        public readonly VoiceJitterBuffer Jitter = new();
        public readonly VoiceDecoder Decoder = new();
        public readonly float[] Pcm = new float[960];
        // Existing engine buffered-source API takes stereo-sized spans, even in mono mode.
        public readonly ushort[] Upload = new ushort[1920];
        public readonly Queue<int> Free = new(new[] { 0, 1, 2, 3 });
        public readonly IBufferedAudioSource Source;
        public TimeSpan LastReceived;
        public TimeSpan NextFrame;
        public NetEntity Entity;
        public float Gain;
        public bool Radio;
        public bool Preview;
        public readonly RadioVoiceFilter RadioFilter = new();
        public Playback(IBufferedAudioSource source) { Source = source; }
        public void Dispose() => Source.Dispose();
    }

    public override void Initialize()
    {
        base.Initialize();

        _net.RegisterNetMessage<MsgVoiceFrame>(accept: NetMessageAccept.Server);
        _net.RegisterNetMessage<MsgVoicePlayback>(Receive, accept: NetMessageAccept.Client);
        CommandBinds.Builder.Bind(ContentKeyFunctions.VoicePushToTalk,
            InputCmdHandler.FromDelegate(_ => _held = true, _ => _held = false))
            .Bind(ContentKeyFunctions.VoiceRadioPushToTalk,
                InputCmdHandler.FromDelegate(_ => _radioHeld = true, _ => _radioHeld = false))
            .Register<VoiceClientSystem>();
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<VoiceClientSystem>();
        StopCapture();
        ClearPlayback();
        base.Shutdown();
    }

    /// <summary>Zero mutes an opaque stream. Entity/profile changes intentionally produce a new stream.</summary>
    public void SetPlayerVolume(NetUserId user, float volume)
    {
        _volumes[user] = float.IsFinite(volume) ? Math.Clamp(volume, 0, 2) : 0;
        if (_streams.Remove(user, out var stream))
            DisposeStream(stream);
    }

    public float GetPlayerVolume(NetUserId user) => _volumes.GetValueOrDefault(user, 1);

    public void SetMicrophoneTest(bool enabled)
    {
        MicrophoneTest = enabled;
        _held = _radioHeld = false;
        StopCapture();
        ClearPlayback();
    }

    public NetUserId[] HeardStreams => _streams.Where(pair => !pair.Value.Preview).Select(pair => pair.Key)
        .Concat(_volumes.Keys).Distinct().ToArray();

    public bool IsSpeaking(NetUserId user) => _streams.TryGetValue(user, out var stream) &&
        _timing.RealTime - stream.LastReceived < TimeSpan.FromMilliseconds(150);

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var enabled = _config.GetCVar(CCVars.VoiceClientEnabled) &&
                      (MicrophoneTest || (_net.IsConnected && _config.GetCVar(CCVars.VoiceEnabled)));
        if (!enabled)
        {
            MicrophoneTest = false;
            _held = _radioHeld = false;
            StopCapture();
            ClearPlayback();
            return;
        }
        if (!_clyde.IsFocused)
            MicrophoneTest = false;
        if (!_clyde.IsFocused || _ui.KeyboardFocused != null || _ui.ModalRoot.ChildCount > 0 ||
            _players.LocalEntity == null || _players.LocalEntity != _captureEntity && _capture != null)
            _held = _radioHeld = false;
        if (!_held && !_radioHeld && !MicrophoneTest)
            StopCapture();
        else
            Capture();

        Span<int> processedHandles = _processedHandles;
        Span<int> queued = _queuedHandles;
        foreach (var user in new List<NetUserId>(_streams.Keys))
        {
            var stream = _streams[user];
            try
            {
                if (_timing.RealTime - stream.LastReceived > TimeSpan.FromMilliseconds(500) ||
                    (!stream.Radio && !stream.Preview && (!TryGetEntity(stream.Entity, out var target) || Deleted(target.Value))))
                {
                    _streams.Remove(user);
                    DisposeStream(stream);
                    continue;
                }
                stream.Source.Global = stream.Radio || stream.Preview;
                stream.Source.Occlusion = stream.Radio || stream.Preview ? 0 : Math.Clamp((1 - stream.Gain) * 0.6f, 0, 0.6f);
                if (!stream.Radio && !stream.Preview && TryGetEntity(stream.Entity, out var entity))
                    stream.Source.Position = _transform.GetWorldPosition(Transform(entity.Value));
                stream.Source.Gain = stream.Gain * Math.Clamp(_config.GetCVar(CCVars.VoiceOutputGain), 0, 2) *
                                     _volumes.GetValueOrDefault(user, 1);
                var processed = stream.Source.GetNumberOfBuffersProcessed();
                if (processed > 0)
                {
                    processed = Math.Min(processed, processedHandles.Length);
                    stream.Source.GetBuffersProcessed(processedHandles[..processed]);
                    for (var i = 0; i < processed; i++)
                        stream.Free.Enqueue(processedHandles[i]);
                }
                if (_timing.RealTime - stream.NextFrame > TimeSpan.FromMilliseconds(60))
                    stream.NextFrame = _timing.RealTime;
                for (var budget = 0; budget < 3 && _timing.RealTime >= stream.NextFrame && stream.Free.Count > 0; budget++)
                {
                    stream.NextFrame += TimeSpan.FromMilliseconds(20);
                    if (!stream.Jitter.TryRead(out var payload))
                        break;
                    var samples = stream.Decoder.Decode(payload ?? Array.Empty<byte>(), stream.Pcm);
                    if (samples != 960)
                        Array.Clear(stream.Pcm);
                    if (stream.Radio)
                        stream.RadioFilter.Process(stream.Pcm);
                    // Engine consumes half the span length for a mono buffered source.
                    for (var i = 0; i < stream.Pcm.Length; i++)
                    {
                        var value = float.IsFinite(stream.Pcm[i]) ? stream.Pcm[i] : 0;
                        stream.Upload[i] = unchecked((ushort) (short) Math.Clamp(value * 32768, short.MinValue, short.MaxValue));
                    }
                    var handle = stream.Free.Dequeue();
                    stream.Source.WriteBuffer(handle, stream.Upload);
                    queued[0] = handle;
                    stream.Source.QueueBuffers(queued);
                    stream.Source.StartPlaying();
                }
            }
            catch (Exception)
            {
                _streams.Remove(user);
                DisposeStream(stream);
                _playbackRetryAfter = _timing.RealTime + TimeSpan.FromSeconds(2);
            }
        }
    }

    private void Capture()
    {
        ActiveVoiceProfileComponent? profile = null;
        if (_players.LocalEntity is { } entity)
            TryComp(entity, out profile);
        if (!MicrophoneTest && (profile == null || profile.Revision == 0))
            return;
        profile ??= new ActiveVoiceProfileComponent();
        try
        {
            var device = _config.GetCVar(CCVars.VoiceInputDevice);
            if (_capture != null && device != _captureDevice)
                StopCapture();
            if (_capture == null)
            {
                if (_timing.RealTime < _retryAfter)
                    return;
                _retryAfter = _timing.RealTime + TimeSpan.FromSeconds(2);
                _capture = _audio.OpenCapture(string.IsNullOrEmpty(device) ? null : device);
                CaptureError = _capture == null ? "Microphone unavailable or permission denied" : null;
                if (_capture == null)
                    return;
                _retryAfter = TimeSpan.Zero;
                _captureEntity = _players.LocalEntity;
                _captureDevice = device;
                _encoder = new VoiceEncoder();
            }
            if (_capture.AvailableSamples > 3840)
            {
                StopCapture(); // Discard stale speech after a stalled frame.
                return;
            }
            for (var budget = 0; budget < 3 && _capture.AvailableSamples >= 960; budget++)
            {
                if (_capture.Read(_pcm) != 960)
                    break;
                var gain = Math.Clamp(_config.GetCVar(CCVars.VoiceInputGain), 0, 4);
                var level = 0f;
                for (var i = 0; i < _pcm.Length; i++)
                {
                    var value = Math.Clamp(_pcm[i] * gain, short.MinValue, short.MaxValue);
                    _pcm[i] = (short) value;
                    level = Math.Max(level, Math.Abs(value) / 32768f);
                }
                MicrophoneLevel = level;
                _dsp.Process(_pcm, profile);
                var count = _encoder!.Encode(_pcm, _encoded);
                if (count is <= 0 or > VoiceProtocol.MaxPayloadBytes)
                    continue;
                if (MicrophoneTest)
                {
                    EnqueuePlayback(new MsgVoicePlayback
                    {
                        Speaker = new NetUserId(Guid.Empty), Entity = NetEntity.Invalid,
                        Sequence = _sequence++, Gain = 1, Payload = _encoded.AsSpan(0, count).ToArray(),
                    }, true);
                    continue;
                }
                _net.ClientSendMessage(new MsgVoiceFrame
                {
                    Sequence = _sequence++,
                    ProfileRevision = profile.Revision,
                    Transmission = _radioHeld ? VoiceTransmission.LocalAndRadio : VoiceTransmission.Local,
                    RadioChannel = _config.GetCVar(CCVars.VoiceRadioChannel),
                    Payload = _encoded.AsSpan(0, count).ToArray(),
                });
            }
        }
        catch (Exception e)
        {
            CaptureError = e.Message;
            StopCapture();
            _retryAfter = _timing.RealTime + TimeSpan.FromSeconds(2);
        }
    }

    private void Receive(MsgVoicePlayback message)
    {
        try
        {
            if (VoiceOpusPacket.IsValid(message.Payload)) EnqueuePlayback(message, false);
        }
        catch (Exception)
        {
            _playbackRetryAfter = _timing.RealTime + TimeSpan.FromSeconds(2);
            if (_streams.Remove(message.Speaker, out var failed)) DisposeStream(failed);
        }
    }

    private void EnqueuePlayback(MsgVoicePlayback message, bool preview)
    {
        if (!preview && (!_config.GetCVar(CCVars.VoiceClientEnabled) || !_config.GetCVar(CCVars.VoiceEnabled) ||
            _volumes.GetValueOrDefault(message.Speaker, 1) <= 0 ||
            (!message.Radio && !TryGetEntity(message.Entity, out _))))
            return;
        if (_timing.RealTime < _playbackRetryAfter) return;
        if (!_streams.TryGetValue(message.Speaker, out var stream))
        {
            if (_streams.Count >= 32)
                return;
            var source = _audio.CreateBufferedAudioSource(4, false);
            if (source == null)
                return;
            source.SampleRate = VoiceProtocol.SampleRate;
            source.Global = false;
            source.RolloffFactor = 0; // Server supplies acoustic distance gain.
            stream = new Playback(source);
            _streams.Add(message.Speaker, stream);
        }
        if (stream.Entity != message.Entity)
        {
            stream.Jitter.Clear();
            stream.Entity = message.Entity;
        }
        stream.LastReceived = _timing.RealTime;
        stream.Gain = message.Gain;
        stream.Radio = message.Radio;
        stream.Preview = preview;
        stream.Jitter.Enqueue(message.Sequence, message.Payload);
    }

    private void StopCapture()
    {
        if (_capture == null)
            return;
        var capture = _capture;
        _capture = null;
        try { capture.Dispose(); }
        catch (Exception) { CaptureError = "Microphone disconnected or unavailable"; }
        _encoder = null;
        _dsp = new VoiceDspPipeline();
        _captureEntity = null;
        MicrophoneLevel = 0;
    }

    private static void DisposeStream(Playback stream)
    {
        try { stream.Dispose(); }
        catch (Exception) { /* A lost audio device must not stop the game. */ }
    }

    private void ClearPlayback()
    {
        foreach (var stream in _streams.Values)
            DisposeStream(stream);
        _streams.Clear();
        _volumes.Clear();
    }
}
