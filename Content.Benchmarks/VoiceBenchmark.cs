using Robust.Shared.Analyzers;
using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using Content.Server.Voice.Acoustics;
using Content.Shared.Voice;
using Content.Shared.Voice.Dsp;
using Content.Shared.Voice.Effects;
using Robust.Shared.Audio.Codecs;
using Robust.Shared.Maths;

namespace Content.Benchmarks;

/// <summary>
/// Hot paths of voice chat: server-side acoustic propagation and client-side DSP / codec per 20 ms frame.
/// </summary>
[Virtual]
[MemoryDiagnoser]
public class VoiceBenchmark
{
    private const int Frame = 960;

    private readonly StationSource _station = new();
    private readonly AcousticField _field = new();
    private ProcessorChain _senderChain = default!;
    private ProcessorChain _radioChain = default!;
    private OpusVoiceEncoder _encoder = default!;
    private OpusVoiceDecoder _decoder = default!;
    private readonly float[] _floats = new float[Frame];
    private readonly short[] _pcm = new short[Frame];
    private readonly byte[] _packet = new byte[1275];
    private byte[] _encoded = default!;

    /// <summary>
    /// A station-like grid: 6x6 rooms separated by walls with a door in each wall, pressurized.
    /// </summary>
    private sealed class StationSource : IAcousticTileSource
    {
        public AcousticTile GetTile(Vector2i pos)
        {
            var x = ((pos.X % 7) + 7) % 7;
            var y = ((pos.Y % 7) + 7) % 7;

            if (x == 6 && y == 3 || y == 6 && x == 3)
                return new AcousticTile { Open = true, Fill = new AcousticBarrier(3, 0.7f, false) };

            if (x == 6 || y == 6)
                return new AcousticTile { Open = true, Fill = new AcousticBarrier(0, 1, true) };

            return new AcousticTile { Open = true, Pressure = 101 };
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        // Character with a pitch/formant shift, species colouring and a gas mask: a heavy but realistic chain.
        var processors = new List<IVoiceProcessor>();
        new VoiceProfile(3f, -1f, 0.3f, 0.2f, 0.1f).CreateProcessors(VoiceConstants.SampleRate, 1, processors);
        processors.Add(new FilterVoiceEffect { Type = BiquadType.LowPass, Frequency = 3200 }.CreateProcessor(VoiceConstants.SampleRate, 2));
        processors.Add(new CombVoiceEffect().CreateProcessor(VoiceConstants.SampleRate, 3));
        processors.Add(new NoiseVoiceEffect { FollowSignal = true }.CreateProcessor(VoiceConstants.SampleRate, 4));
        _senderChain = new ProcessorChain(processors);

        _radioChain = new ProcessorChain(new List<IVoiceProcessor>
        {
            new Biquad(BiquadType.HighPass, VoiceConstants.SampleRate, 320),
            new Biquad(BiquadType.HighPass, VoiceConstants.SampleRate, 320),
            new Biquad(BiquadType.LowPass, VoiceConstants.SampleRate, 3400),
            new Biquad(BiquadType.LowPass, VoiceConstants.SampleRate, 3400),
            new CompressorProcessor(VoiceConstants.SampleRate, -26, 5, 3, 90, 8),
            new DistortionProcessor(2.5f, 0.3f, false),
            new NoiseProcessor(VoiceConstants.SampleRate, 0.006f, false, 900, 5000, 5),
        });

        _encoder = new OpusVoiceEncoder(VoiceConstants.SampleRate) { Bitrate = 24000, Complexity = 6, UseInbandFec = true };
        _decoder = new OpusVoiceDecoder(VoiceConstants.SampleRate);

        for (var i = 0; i < Frame; i++)
        {
            _pcm[i] = (short) (System.MathF.Sin(i * 0.03f) * 6000 + System.MathF.Sin(i * 0.17f) * 2000);
        }

        var length = _encoder.Encode(_pcm, _packet);
        _encoded = _packet[..length];
    }

    [Benchmark(Description = "Acoustic field, 10 tile range, rooms + doors")]
    public int AcousticField()
    {
        _field.Compute(_station, new Vector2i(3, 3), new AcousticSettings(10f, 10f));
        return _field.NodeCount;
    }

    [Benchmark(Description = "Sender DSP chain (profile + species + mask), 20 ms")]
    public float SenderChain()
    {
        VoiceDsp.ShortToFloat(_pcm, _floats);
        _senderChain.Process(_floats);
        return _floats[0];
    }

    [Benchmark(Description = "Opus encode, 20 ms")]
    public int Encode()
    {
        return _encoder.Encode(_pcm, _packet);
    }

    [Benchmark(Description = "Opus decode, 20 ms")]
    public int Decode()
    {
        return _decoder.Decode(_encoded, _pcm);
    }

    [Benchmark(Description = "Receive radio chain, 20 ms")]
    public float RadioChain()
    {
        VoiceDsp.ShortToFloat(_pcm, _floats);
        _radioChain.Process(_floats);
        return _floats[0];
    }
}
