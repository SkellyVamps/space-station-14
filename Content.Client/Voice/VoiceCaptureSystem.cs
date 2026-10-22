using Content.Shared.CCVar;
using Content.Shared.Voice;
using Robust.Client.Audio.Realtime;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

public enum VoiceCaptureStatus : byte
{
    /// <summary>
    /// Nothing needs the microphone; it is closed.
    /// </summary>
    Closed,

    Capturing,

    /// <summary>
    /// This client can't capture at all (headless, or the audio backend lacks capture support).
    /// </summary>
    Unsupported,

    /// <summary>
    /// No microphone found or it could not be opened (in use, OS permission denied...). Retrying periodically.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The microphone was unplugged. Retrying periodically.
    /// </summary>
    Disconnected,
}

/// <summary>
/// CAPTURE layer: owns the microphone. Other systems acquire it while they need audio and read 16-bit mono samples.
/// </summary>
/// <remarks>
/// The device is only open while something needs it (plus a short grace period), so in push-to-talk mode the
/// microphone is not open all the time. Device loss is detected and the device is reopened automatically; changing
/// the input device setting reopens it without restarting anything.
/// </remarks>
public sealed partial class VoiceCaptureSystem : EntitySystem
{
    [Dependency] private IRealtimeAudioManager _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    /// Backend ring buffer size: half a second, so a slow frame never loses audio.
    /// </summary>
    private const int BufferSamples = VoiceConstants.SampleRate / 2;

    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(3);

    private readonly HashSet<object> _users = new();
    private readonly short[] _flushScratch = new short[1024];
    private IAudioCaptureDevice? _device;
    private string _deviceSetting = string.Empty;
    private TimeSpan _lastUsed;
    private TimeSpan _nextRetry;

    public VoiceCaptureStatus Status { get; private set; } = VoiceCaptureStatus.Closed;

    /// <summary>
    /// Name of the open device, if any.
    /// </summary>
    public string? DeviceName => _device?.DeviceName;

    public event Action<VoiceCaptureStatus>? StatusChanged;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_cfg, CCVars.VoiceInputDevice, OnDeviceSettingChanged, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        CloseDevice(VoiceCaptureStatus.Closed);
    }

    public IReadOnlyList<string> GetDevices()
    {
        return _audio.CaptureSupported ? _audio.GetCaptureDevices() : Array.Empty<string>();
    }

    private void OnDeviceSettingChanged(string device)
    {
        if (device == _deviceSetting)
            return;

        _deviceSetting = device;

        // Reopen on the next acquire/update with the new device.
        if (_device != null)
        {
            CloseDevice(VoiceCaptureStatus.Closed);
            _nextRetry = TimeSpan.Zero;
        }
    }

    /// <summary>
    /// Declares that <paramref name="user"/> needs microphone audio. Call every frame while needed or once; release
    /// with <see cref="Release"/>.
    /// </summary>
    public void Acquire(object user)
    {
        _users.Add(user);
        _lastUsed = _timing.RealTime;
    }

    public void Release(object user)
    {
        _users.Remove(user);
    }

    public bool IsAcquired(object user) => _users.Contains(user);

    /// <summary>
    /// Reads captured samples. Returns 0 if there is nothing or the device isn't capturing.
    /// </summary>
    public int Read(Span<short> buffer)
    {
        if (_device == null || !_device.Capturing)
            return 0;

        try
        {
            return _device.ReadSamples(buffer);
        }
        catch (Exception e)
        {
            Log.Error($"Error reading microphone: {e}");
            CloseDevice(VoiceCaptureStatus.Disconnected);
            return 0;
        }
    }

    /// <summary>
    /// Throws away anything buffered, e.g. audio captured before push to talk was pressed.
    /// </summary>
    public void Flush()
    {
        // No stackalloc: content code must pass the sandbox's IL verification.
        while (Read(_flushScratch) == _flushScratch.Length)
        {
        }
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var now = _timing.RealTime;

        if (_users.Count > 0)
            _lastUsed = now;

        if (_device != null)
        {
            if (!_device.Connected)
            {
                Log.Warning($"Microphone '{_device.DeviceName}' disconnected.");
                CloseDevice(VoiceCaptureStatus.Disconnected);
                _nextRetry = now + RetryInterval;
                return;
            }

            if (_users.Count == 0 && now - _lastUsed > IdleClose)
                CloseDevice(VoiceCaptureStatus.Closed);

            return;
        }

        if (_users.Count == 0 || now < _nextRetry)
            return;

        TryOpen(now);
    }

    private void TryOpen(TimeSpan now)
    {
        if (!_audio.CaptureSupported)
        {
            SetStatus(VoiceCaptureStatus.Unsupported);
            _nextRetry = TimeSpan.MaxValue;
            return;
        }

        try
        {
            var name = string.IsNullOrEmpty(_deviceSetting) ? null : _deviceSetting;
            _device = _audio.OpenCaptureDevice(name, VoiceConstants.SampleRate, BufferSamples);

            // The configured device is gone: fall back to the default one rather than going silent.
            if (_device == null && name != null)
            {
                Log.Warning($"Microphone '{name}' unavailable, falling back to the default device.");
                _device = _audio.OpenCaptureDevice(null, VoiceConstants.SampleRate, BufferSamples);
            }
        }
        catch (Exception e)
        {
            Log.Error($"Failed to open microphone: {e}");
            _device = null;
        }

        if (_device == null)
        {
            SetStatus(VoiceCaptureStatus.Unavailable);
            _nextRetry = now + RetryInterval;
            return;
        }

        _device.Start();
        SetStatus(VoiceCaptureStatus.Capturing);
    }

    private void CloseDevice(VoiceCaptureStatus newStatus)
    {
        if (_device != null)
        {
            try
            {
                _device.Dispose();
            }
            catch (Exception e)
            {
                Log.Error($"Error closing microphone: {e}");
            }

            _device = null;
        }

        SetStatus(newStatus);
    }

    private void SetStatus(VoiceCaptureStatus status)
    {
        if (Status == status)
            return;

        Status = status;
        StatusChanged?.Invoke(status);
    }
}
