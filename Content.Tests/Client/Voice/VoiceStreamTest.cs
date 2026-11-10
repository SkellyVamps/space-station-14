using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Voice;
using Content.Shared.Voice;
using Content.Shared.Voice.Codecs;
using NUnit.Framework;

namespace Content.Tests.Client.Voice;

/// <summary>
/// Voice plays as a chain of short overlapping clips (the engine has no live streams). This rebuilds what the listener
/// hears from those clips, with uneven frame timing like a real game, and checks it's seamless.
/// </summary>
[TestFixture]
[TestOf(typeof(VoiceStream))]
public sealed class VoiceStreamTest
{
    private const int Rate = VoiceConstants.SampleRate;

    /// <summary>
    /// Plays clips against a pretend audio clock.
    /// </summary>
    private sealed class FakePlayer : IVoiceClipPlayer
    {
        public double Now;
        public readonly List<FakeClip> Clips = new();

        public IVoiceClip Play(ReadOnlySpan<short> samples, int sampleRate, float startAt, bool global, Vector2 position)
        {
            var clip = new FakeClip(this, samples.ToArray(), Now, startAt);
            Clips.Add(clip);
            return clip;
        }
    }

    private sealed class FakeClip(FakePlayer player, short[] samples, double started, float startAt) : IVoiceClip
    {
        public readonly short[] Samples = samples;
        public readonly double Started = started;
        public readonly float StartAt = startAt;

        public float PlaybackPosition => (float) Math.Min(Samples.Length / (double) Rate, StartAt + player.Now - Started);
        public bool Playing => StartAt + player.Now - Started < Samples.Length / (double) Rate;

        public bool Global
        {
            set { }
        }

        public Vector2 Position
        {
            set { }
        }

        public void Dispose()
        {
        }
    }

    [Test]
    public void ClipsJoinUpWithoutGapsOrBumps([Values(16, 33, 45)] int maxFrameMs, [Values(0, 25)] int networkJitterMs)
    {
        var player = new FakePlayer();
        var stream = new VoiceStream(VoiceReceiveMode.Local, player, new VoiceJitterBuffer(3), null)
        {
            TargetGain = 1f,
            Volume = 1f,
        };

        // Three seconds of a steady tone, in 20 ms frames.
        const int frames = 150;
        var tone = new short[ImaAdpcm.FrameSamples];
        var state = new ImaAdpcm.State();
        var payloads = new List<byte[]>();
        for (var f = 0; f < frames; f++)
        {
            for (var i = 0; i < tone.Length; i++)
            {
                var t = (f * tone.Length + i) / (double) ImaAdpcm.SampleRate;
                tone[i] = (short) (Math.Sin(2 * Math.PI * 300 * t) * 8000);
            }

            var payload = new byte[ImaAdpcm.FrameBytes];
            ImaAdpcm.Encode(tone, ref state, payload);
            payloads.Add(payload);
        }

        // Uneven frames, like a busy game, and packets that don't all arrive on time.
        var random = new Random(maxFrameMs * 100 + networkJitterMs);
        var arrivals = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            arrivals[f] = f * 0.02 + random.Next(0, networkJitterMs + 1) / 1000.0;
        }

        var nextPacket = 0;
        while (player.Now < 4.0)
        {
            var now = TimeSpan.FromSeconds(player.Now);
            // In order, as the network delivers them (a late one holds up the ones behind it).
            while (nextPacket < frames && arrivals[nextPacket] <= player.Now)
            {
                stream.Jitter.Push((ushort) nextPacket, payloads[nextPacket], nextPacket == frames - 1, now);
                nextPacket++;
            }

            var frameTime = (float) (random.Next(8, maxFrameMs + 1) / 1000.0);
            stream.Update(now, frameTime);
            player.Now += frameTime;
        }

        // What the listener heard: every clip, from where it started, laid at the moment it played.
        var heard = new double[(int) (5.0 * Rate)];
        foreach (var clip in player.Clips)
        {
            var first = (int) Math.Round(clip.StartAt * Rate);
            for (var n = first; n < clip.Samples.Length; n++)
            {
                var at = (int) Math.Round((clip.Started + (n / (double) Rate - clip.StartAt)) * Rate);
                if (at >= 0 && at < heard.Length)
                    heard[at] += clip.Samples[n];
            }
        }

        // Find where the voice starts, then check the middle stays at one level: a gap or a doubled overlap would
        // show up as a dip or a bump.
        var start = Array.FindIndex(heard, s => Math.Abs(s) > 100);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "something was played");

        // Whole periods of the 300 Hz tone, so a steady tone gives a steady number.
        const int window = Rate * 10 / 1000;
        var from = start + Rate / 5;
        var to = start + (int) (2.6 * Rate);
        double min = double.MaxValue, max = 0;
        for (var w = from; w + window < to; w += window)
        {
            double sum = 0;
            for (var i = w; i < w + window; i++)
            {
                sum += heard[i] * heard[i];
            }

            var rms = Math.Sqrt(sum / window);
            if (rms < 4000)
            {
                TestContext.Out.WriteLine($"dip at {w / (double) Rate:0.000}s rms {rms:0}");
                foreach (var c in player.Clips)
                {
                    var t0 = c.Started - c.StartAt;
                    if (Math.Abs(t0 - w / (double) Rate) < 0.15)
                        TestContext.Out.WriteLine($"  clip t0 {t0:0.000} started {c.Started:0.000} at {c.StartAt * 1000:0}ms len {c.Samples.Length * 1000 / Rate}ms");
                }
            }

            min = Math.Min(min, rms);
            max = Math.Max(max, rms);
        }

        var spreadDb = 20 * Math.Log10(max / min);
        Assert.That(spreadDb, Is.LessThan(1.5), $"level steady across clip joins (min {min:0}, max {max:0})");
        Assert.That(player.Clips.Count, Is.GreaterThan(30), "played as a chain of clips");
    }
}
