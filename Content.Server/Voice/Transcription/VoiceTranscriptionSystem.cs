using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Voice;
using Content.Shared.Voice.Dsp;
using Content.Shared.Voice.Codecs;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Whisper.net.Ggml;

namespace Content.Server.Voice.Transcription;

/// <summary>
/// Voice-to-text for the admin logs: every piece of speech a player sends is converted to text in the background and
/// logged as <see cref="LogType.VoiceTranscript"/>, marked as coming from voice chat, with how it was said (talking,
/// whispering, radio channel, intercom). The audio is only held in memory until it's transcribed.
/// </summary>
/// <remarks>
/// Speech is collected per player into "spurts" (from pressing talk to letting go, at most
/// <see cref="MaxSeconds"/>), and handed to a single background worker, so speech recognition
/// never slows the game down. If it can't keep up, new spurts are skipped rather than falling ever further behind.
/// </remarks>
public sealed partial class VoiceTranscriptionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IResourceManager _res = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    /// Longest piece of speech transcribed at once; longer speeches are cut into pieces this long.
    /// </summary>
    public const float MaxSeconds = 30f;

    /// <summary>
    /// Shorter than this is a click or a breath, not words.
    /// </summary>
    private const float MinSeconds = 0.35f;

    /// <summary>
    /// Quieter than this (dBFS) is background noise.
    /// </summary>
    private const float MinLevelDb = -50f;

    /// <summary>
    /// A spurt whose end never arrived (lost packet, disconnect) is transcribed after this long without audio.
    /// </summary>
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromSeconds(1);

    private const int MaxSamples = (int) (MaxSeconds * IVoiceTranscriber.SampleRate);

    private readonly Dictionary<NetUserId, Spurt> _spurts = new();
    private readonly List<NetUserId> _stale = new();
    private readonly ConcurrentQueue<(Job Job, string Text)> _results = new();
    private readonly short[] _decoded = new short[ImaAdpcm.FrameSamples];

    private Channel<Job> _jobs = Channel.CreateUnbounded<Job>();
    private CancellationTokenSource _cancel = new();
    private Task? _worker;
    private IVoiceTranscriber? _transcriber;
    private int _queued;
    private bool _broken;

    private bool _enabled;
    private int _maxQueue;

    /// <summary>
    /// Replaces speech recognition, for tests.
    /// </summary>
    public IVoiceTranscriber? TranscriberOverride;

    /// <summary>
    /// A transcript was logged: (player, how it was said, text). For tests and tools.
    /// </summary>
    public event Action<NetUserId, string, string>? Transcribed;

    public int SpurtsTranscribed { get; private set; }
    public int SpurtsSkipped { get; private set; }
    public int SpurtsFailed { get; private set; }
    public int Queued => _queued;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.VoiceTranscribe, v =>
        {
            _enabled = v;

            // Get the model ready when the server starts rather than on the first sentence.
            if (v)
                _worker ??= Task.Run(() => WorkAsync(_cancel.Token));
        }, true);
        Subs.CVar(_cfg, CCVars.VoiceTranscribeMaxQueue, v => _maxQueue = Math.Max(1, v), true);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _jobs.Writer.TryComplete();
        _cancel.Cancel();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Cancelled.
        }

        _transcriber?.Dispose();
        _transcriber = null;
    }

    #region Collecting speech

    /// <summary>
    /// A voice frame <paramref name="session"/> sent as <paramref name="speaker"/>, already validated.
    /// </summary>
    /// <param name="route">How it was said, for the log: "talking", "radio, Security"...</param>
    public void OnFrame(ICommonSession session, EntityUid speaker, string route, byte[] payload, bool endOfSpurt)
    {
        if (!_enabled || _broken)
            return;

        if (!_spurts.TryGetValue(session.UserId, out var spurt))
        {
            spurt = new Spurt();
            _spurts[session.UserId] = spurt;
        }

        // Switched from talking to the radio mid-sentence, or changed body: log the two separately.
        if (spurt.Count > 0 && (spurt.Route != route || spurt.Speaker != speaker))
            Flush(session.UserId, spurt);

        spurt.Route = route;
        spurt.Speaker = speaker;
        spurt.LastFrame = _timing.RealTime;

        if (payload.Length > 0)
            Append(session.UserId, spurt, payload);

        if (endOfSpurt)
            Flush(session.UserId, spurt);
    }

    private void Append(NetUserId user, Spurt spurt, byte[] payload)
    {
        // Voice is already 16 kHz ADPCM, exactly what speech recognition wants.
        if (!ImaAdpcm.Decode(payload, _decoded))
            return;

        for (var i = 0; i < ImaAdpcm.FrameSamples; i++)
        {
            if (spurt.Count == spurt.Samples.Length)
                Array.Resize(ref spurt.Samples, Math.Min(spurt.Samples.Length * 2, MaxSamples));

            spurt.Samples[spurt.Count++] = _decoded[i] / 32768f;

            if (spurt.Count == MaxSamples)
                Flush(user, spurt);
        }
    }

    private void Flush(NetUserId user, Spurt spurt)
    {
        var count = spurt.Count;
        spurt.Count = 0;

        if (count < MinSeconds * IVoiceTranscriber.SampleRate || spurt.Speaker is not { } speaker)
            return;

        var samples = spurt.Samples.AsSpan(0, count);
        if (VoiceDsp.RmsDb(samples) < MinLevelDb)
            return;

        if (Interlocked.Increment(ref _queued) > _maxQueue)
        {
            Interlocked.Decrement(ref _queued);
            SpurtsSkipped++;
            return;
        }

        _jobs.Writer.TryWrite(new Job(user, speaker, spurt.Route, samples.ToArray()));
    }

    #endregion

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Speech whose end never arrived.
        var now = _timing.RealTime;
        foreach (var (user, spurt) in _spurts)
        {
            if (spurt.Count > 0 && now - spurt.LastFrame > SilenceTimeout)
                Flush(user, spurt);
            else if (spurt.Count == 0 && now - spurt.LastFrame > TimeSpan.FromMinutes(5))
                _stale.Add(user);
        }

        foreach (var user in _stale)
        {
            _spurts.Remove(user);
        }

        _stale.Clear();

        while (_results.TryDequeue(out var result))
        {
            LogTranscript(result.Job, result.Text);
        }
    }

    private void LogTranscript(Job job, string text)
    {
        SpurtsTranscribed++;
        if (string.IsNullOrWhiteSpace(text))
            return;

        _adminLog.Add(LogType.VoiceTranscript, LogImpact.Low,
            $"[Voice-to-text] {ToPrettyString(job.Speaker):player} ({job.Route}): \"{text}\"");

        Transcribed?.Invoke(job.User, job.Route, text);
    }

    #region Background worker

    private async Task WorkAsync(CancellationToken cancel)
    {
        if (TranscriberOverride == null)
        {
            try
            {
                _transcriber ??= CreateTranscriber();
                await _transcriber.LoadAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                // No model: stop trying until the server restarts rather than failing on every sentence.
                _broken = true;
                Log.Error($"Voice-to-text is unavailable, could not load speech recognition: {e}");
                return;
            }
        }

        await foreach (var job in _jobs.Reader.ReadAllAsync(cancel))
        {
            try
            {
                var transcriber = TranscriberOverride ?? (_transcriber ??= CreateTranscriber());
                var text = await transcriber.TranscribeAsync(job.Samples, cancel);
                _results.Enqueue((job, text));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                SpurtsFailed++;
                Log.Error($"Voice-to-text failed: {e}");
            }
            finally
            {
                Interlocked.Decrement(ref _queued);
            }
        }
    }

    private IVoiceTranscriber CreateTranscriber()
    {
        var modelName = _cfg.GetCVar(CCVars.VoiceTranscribeModel);
        if (!Enum.TryParse<GgmlType>(modelName, true, out var model))
        {
            Log.Warning($"Unknown voice.transcribe_model \"{modelName}\", using BaseEn.");
            model = GgmlType.BaseEn;
        }

        var path = _cfg.GetCVar(CCVars.VoiceTranscribeModelPath);
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(_res.UserData.RootDir ?? "data", "whisper", $"ggml-{model}.bin");

        return new WhisperVoiceTranscriber(path, model,
            _cfg.GetCVar(CCVars.VoiceTranscribeAutoDownload),
            _cfg.GetCVar(CCVars.VoiceTranscribeLanguage).Trim().ToLowerInvariant(),
            Math.Clamp(_cfg.GetCVar(CCVars.VoiceTranscribeThreads), 1, 32),
            Log);
    }

    #endregion

    private sealed class Spurt
    {
        // Grows as needed: most spurts are a few seconds.
        public float[] Samples = new float[IVoiceTranscriber.SampleRate * 4];
        public int Count;
        public string Route = string.Empty;
        public EntityUid? Speaker;
        public TimeSpan LastFrame;
    }

    private readonly record struct Job(NetUserId User, EntityUid Speaker, string Route, float[] Samples);
}
