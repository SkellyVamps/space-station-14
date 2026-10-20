using System;
using System.Diagnostics;
using Robust.Client.Audio;
using Content.Client.Voice;
using Content.Shared.Voice;
using NUnit.Framework;

namespace Content.Tests.Shared.Voice;

[TestFixture]
public sealed class VoiceFoundationTest
{
    [Test]
    public void AdmissionIsBoundedAndWrapSafe()
    {
        var guard = new VoiceIngressGuard();
        Assert.That(guard.TryAccept(uint.MaxValue, 20, TimeSpan.Zero, true));
        Assert.That(guard.TryAccept(0, 20, TimeSpan.FromMilliseconds(20), true));
        Assert.That(guard.TryAccept(0, 20, TimeSpan.FromMilliseconds(40), true), Is.False);
        Assert.That(guard.TryAccept(1, 513, TimeSpan.FromMilliseconds(60), true), Is.False);
        Assert.That(guard.TryAccept(1, 20, TimeSpan.FromMilliseconds(80), false), Is.False);
        Assert.That(guard.TryAccept(1, 20, TimeSpan.FromMilliseconds(100), true));
        var burst = new VoiceIngressGuard();
        for (uint i = 0; i < 8; i++)
            Assert.That(burst.TryAccept(i, 20, TimeSpan.Zero, true));
        Assert.That(burst.TryAccept(8, 20, TimeSpan.Zero, true), Is.False);
        Assert.That(burst.TryAccept(8, 20, TimeSpan.FromMilliseconds(20), true));
    }

    [Test]
    public void JitterReordersWrapsAndRequestsConcealment()
    {
        var jitter = new VoiceJitterBuffer();
        Assert.That(jitter.Enqueue(uint.MaxValue, new byte[] { 1 }));
        Assert.That(jitter.Enqueue(1, new byte[] { 3 }));
        Assert.That(jitter.TryRead(out _), Is.False);
        Assert.That(jitter.Enqueue(0, new byte[] { 2 }));
        Assert.That(jitter.Enqueue(0, new byte[] { 2 }), Is.False);
        Assert.That(jitter.TryRead(out var first));
        Assert.That(first, Is.EqualTo(new byte[] { 1 }));
        Assert.That(jitter.TryRead(out var second));
        Assert.That(second, Is.EqualTo(new byte[] { 2 }));
        Assert.That(jitter.TryRead(out _));
        Assert.That(jitter.TryRead(out var missing));
        Assert.That(missing, Is.Null);
        for (var i = 0; i < 12; i++)
            jitter.TryRead(out _);
        Assert.That(jitter.TryRead(out _), Is.False);
        Assert.That(jitter.Enqueue(1000, new byte[] { 1 }));
    }

    [Test]
    public void PathsRespectWallsVacuumDoorsAndRange()
    {
        var origin = new VoiceAcousticTile(0, 0);
        var listener = new VoiceAcousticTile(2, 0);
        var closed = false;
        var vacuum = false;
        var wall = false;
        VoiceAcousticMedium Sample(VoiceAcousticTile tile) =>
            new(wall && tile.X == 1, vacuum && tile.X == 1 ? 0 : 101,
                closed && tile.X == 1 ? 4 : 0);
        var open = VoiceAcousticSearch.Search(origin, 4, 5, 128, Sample);
        Assert.That(open[listener], Is.EqualTo(2));
        closed = true;
        Assert.That(VoiceAcousticSearch.Search(origin, 4, 5, 128, Sample).ContainsKey(listener), Is.False);
        closed = false;
        vacuum = true;
        Assert.That(VoiceAcousticSearch.Search(origin, 4, 5, 128, Sample).ContainsKey(listener), Is.False);
        vacuum = false;
        wall = true;
        Assert.That(VoiceAcousticSearch.Search(origin, 4, 5, 128, Sample).ContainsKey(listener), Is.False);
    }

    [Test]
    public void PathCanGoAroundCornerButNeverBeyondBudget()
    {
        var calls = 0;
        var result = VoiceAcousticSearch.Search(new(0, 0), 8, 5, 128, tile =>
        {
            calls++;
            return new VoiceAcousticMedium(tile == new VoiceAcousticTile(1, 0), 101, 0);
        });
        Assert.That(result[new(2, 0)], Is.EqualTo(4));
        Assert.That(calls, Is.LessThanOrEqualTo(1 + 128 * 4));
        Assert.That(VoiceAcousticSearch.Search(new(0, 0), float.NaN, 5, 128,
            _ => throw new Exception("Invalid configuration must not sample")), Is.Empty);
    }
    [Test]
    public void ManagedOpusRoundTripAndLossConcealment()
    {
        var encoder = new VoiceEncoder();
        var decoder = new VoiceDecoder();
        var pcm = new short[960];
        var encoded = new byte[512];
        var decoded = new float[960];
        double energy = 0;
        for (var frame = 0; frame < 12; frame++)
        {
            for (var i = 0; i < pcm.Length; i++)
                pcm[i] = (short) (Math.Sin(2 * Math.PI * 220 * (frame * 960 + i) / 48000) * 12000);
            var length = encoder.Encode(pcm, encoded);
            Assert.That(length, Is.InRange(1, 512));
            Assert.That(decoder.Decode(encoded.AsSpan(0, length), decoded), Is.EqualTo(960));
            foreach (var sample in decoded)
            {
                Assert.That(float.IsFinite(sample));
                energy += sample * sample;
            }
        }
        Assert.That(energy, Is.GreaterThan(1));
        Assert.That(decoder.Decode(ReadOnlySpan<byte>.Empty, decoded), Is.EqualTo(960));
        Assert.That(decoder.Decode(new byte[513], decoded), Is.EqualTo(0));
    }

    [Test]
    public void ProceduralEffectsStayFiniteAndBounded()
    {
        var dsp = new VoiceDspPipeline();
        var profile = new ActiveVoiceProfileComponent { Pitch = 0.8f, LowPass = 2800, Robot = 0.2f, Drive = 0.4f };
        var pcm = new short[960];
        long energy = 0;
        for (var frame = 0; frame < 20; frame++)
        {
            for (var i = 0; i < pcm.Length; i++)
                pcm[i] = (short) (Math.Sin(2 * Math.PI * 180 * (frame * 960 + i) / 48000) * 16000);
            dsp.Process(pcm, profile);
            foreach (var sample in pcm)
                energy += (long) sample * sample;
        }
        Assert.That(energy, Is.GreaterThan(100000));
    }

    [Test]
    public void AcousticSearchSyntheticLoad()
    {
        var watch = Stopwatch.StartNew();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var samples = 0;
        for (var epoch = 0; epoch < 10; epoch++)
        for (var speaker = 0; speaker < 128; speaker++)
        {
            var routes = VoiceAcousticSearch.Search(new(0, 0), 10, 5, 2048, _ =>
            {
                samples++;
                return new VoiceAcousticMedium(false, 101, 0);
            });
            Assert.That(routes.Count, Is.EqualTo(221));
        }
        watch.Stop();
        TestContext.Progress.WriteLine($"Synthetic acoustic load: 128 speakers x 10 epochs, {watch.Elapsed.TotalMilliseconds:F1} ms, " +
            $"{GC.GetAllocatedBytesForCurrentThread() - allocated} allocated bytes, {samples} tile samples. " +
            "Pure search only; excludes ECS, atmosphere, network, radio and audio.");
    }

}
