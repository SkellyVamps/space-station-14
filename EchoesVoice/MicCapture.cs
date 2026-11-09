using Content.Shared.Voice.Codecs;
using Content.Shared.Voice.Dsp;
using OpenTK.Audio.OpenAL;

namespace EchoesVoice;

/// <summary>
/// Records the microphone (through OpenAL Soft, the same library the game uses) and turns it into 20 ms frames ready to
/// send: optional noise removal, gain, then <see cref="ImaAdpcm"/>.
/// </summary>
public sealed class MicCapture : IDisposable
{
    private const int Rate = ImaAdpcm.SampleRate;

    private readonly object _lock = new();
    private ALCaptureDevice _device = ALCaptureDevice.Null;
    private Thread? _thread;
    private volatile bool _running;

    private readonly short[] _read = new short[Rate];
    private readonly short[] _frame = new short[ImaAdpcm.FrameSamples];
    private readonly float[] _float = new float[ImaAdpcm.FrameSamples];
    private int _filled;
    private ImaAdpcm.State _state;
    private NoiseSuppressor? _suppressor;

    /// <summary>
    /// A frame is ready: (encoded frame, level 0-1). Raised on the capture thread.
    /// </summary>
    public event Action<byte[], float>? Frame;

    public float Gain { get; set; } = 1f;
    public bool Muted { get; set; }

    /// <summary>
    /// 0 = off, 1 = strongest.
    /// </summary>
    public float NoiseSuppression { get; set; } = 0.5f;

    /// <summary>
    /// For testing without a microphone: a tone instead of the microphone.
    /// </summary>
    public bool TestTone { get; set; }

    public string? DeviceName { get; private set; }

    public static List<string> ListDevices()
    {
        try
        {
            return ALC.GetStringList(GetEnumerationStringList.CaptureDeviceSpecifier).ToList();
        }
        catch (Exception)
        {
            // No OpenAL, or no capture support.
            return new List<string>();
        }
    }

    public static string? DefaultDevice()
    {
        try
        {
            return ALC.GetString(ALDevice.Null, AlcGetString.CaptureDefaultDeviceSpecifier);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens <paramref name="deviceName"/> (null: the default microphone) and starts recording.
    /// </summary>
    /// <returns>An error to show, or null.</returns>
    public string? Start(string? deviceName)
    {
        Stop();

        if (!TestTone)
        {
            ALCaptureDevice device;
            try
            {
                // A second of buffer: plenty, and we read every 10 ms.
                device = ALC.CaptureOpenDevice(deviceName, Rate, ALFormat.Mono16, Rate);
            }
            catch (Exception e)
            {
                return $"Couldn't open the microphone: {e.Message}";
            }

            if (device == ALCaptureDevice.Null)
                return "Couldn't open the microphone. Is one plugged in, and is this app allowed to use it?";

            _device = device;
            ALC.CaptureStart(_device);
        }

        DeviceName = deviceName ?? DefaultDevice();
        _running = true;
        _filled = 0;
        _state = default;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Microphone" };
        _thread.Start();
        return null;
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(1000);
        _thread = null;

        lock (_lock)
        {
            if (_device != ALCaptureDevice.Null)
            {
                ALC.CaptureStop(_device);
                ALC.CaptureCloseDevice(_device);
                _device = ALCaptureDevice.Null;
            }
        }
    }

    private void Loop()
    {
        var phase = 0.0;
        var nextTone = DateTime.UtcNow;

        while (_running)
        {
            Thread.Sleep(10);

            int count;
            if (TestTone)
            {
                // Real time worth of a 440 Hz tone, pulsing like syllables.
                var now = DateTime.UtcNow;
                count = (int) Math.Min(_read.Length, (now - nextTone).TotalSeconds * Rate);
                nextTone = nextTone.AddSeconds(count / (double) Rate);
                for (var i = 0; i < count; i++)
                {
                    phase += 1.0 / Rate;
                    var envelope = Math.Max(0, Math.Sin(phase * Math.PI * 3));
                    _read[i] = (short) (Math.Sin(phase * 2 * Math.PI * 440) * 8000 * envelope);
                }
            }
            else
            {
                lock (_lock)
                {
                    count = ReadDevice();
                }
            }

            for (var i = 0; i < count; i++)
            {
                _frame[_filled++] = _read[i];
                if (_filled == ImaAdpcm.FrameSamples)
                {
                    _filled = 0;
                    EmitFrame();
                }
            }
        }
    }

    private unsafe int ReadDevice()
    {
        if (_device == ALCaptureDevice.Null)
            return 0;

        var available = Math.Min(_read.Length, ALC.GetInteger(_device, AlcGetInteger.CaptureSamples));
        if (available <= 0)
            return 0;

        fixed (short* ptr = _read)
        {
            ALC.CaptureSamples(_device, ptr, available);
        }

        return available;
    }

    private void EmitFrame()
    {
        for (var i = 0; i < _frame.Length; i++)
        {
            _float[i] = Muted ? 0f : _frame[i] / 32768f * Gain;
        }

        // Fans, hum and hiss: the same noise removal the game uses.
        if (NoiseSuppression > 0.01f)
        {
            _suppressor ??= new NoiseSuppressor(NoiseSuppression);
            _suppressor.Strength = NoiseSuppression;
            _suppressor.Process(_float);
        }

        double sum = 0;
        for (var i = 0; i < _float.Length; i++)
        {
            var s = Math.Clamp(_float[i], -1f, 1f);
            sum += s * s;
            _frame[i] = (short) (s * 32767);
        }

        var packed = new byte[ImaAdpcm.FrameBytes];
        ImaAdpcm.Encode(_frame, ref _state, packed);
        Frame?.Invoke(packed, (float) Math.Sqrt(sum / _float.Length));
    }

    public void Dispose()
    {
        Stop();
    }
}
