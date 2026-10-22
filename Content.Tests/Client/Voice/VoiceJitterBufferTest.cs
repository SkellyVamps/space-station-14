using System;
using Content.Client.Voice;
using NUnit.Framework;

namespace Content.Tests.Client.Voice;

[TestFixture, Parallelizable]
[TestOf(typeof(VoiceJitterBuffer))]
public sealed class VoiceJitterBufferTest
{
    private static readonly byte[] Data = { 1, 2, 3 };
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(20);

    private static byte[] P(ushort seq) => new[] { (byte) seq, (byte) (seq >> 8) };

    private static ushort Seq(byte[] payload) => (ushort) (payload[0] | payload[1] << 8);

    [Test]
    public void WaitsForDepthThenPlaysInOrder()
    {
        var buffer = new VoiceJitterBuffer(3);
        var now = TimeSpan.Zero;

        buffer.Push(10, P(10), false, now);
        Assert.That(buffer.Pop(now, out _), Is.EqualTo(JitterResult.Idle));

        // Arrive out of order.
        buffer.Push(12, P(12), false, now);
        buffer.Push(11, P(11), false, now);

        for (ushort expected = 10; expected <= 12; expected++)
        {
            Assert.That(buffer.Pop(now, out var payload), Is.EqualTo(JitterResult.Packet));
            Assert.That(Seq(payload!), Is.EqualTo(expected));
        }
    }

    [Test]
    public void StartsAnywayAfterDelay()
    {
        // A very short utterance shorter than the target depth still plays.
        var buffer = new VoiceJitterBuffer(5);
        buffer.Push(1, P(1), false, TimeSpan.Zero);
        Assert.That(buffer.Pop(TimeSpan.FromMilliseconds(10), out _), Is.EqualTo(JitterResult.Idle));
        Assert.That(buffer.Pop(TimeSpan.FromMilliseconds(200), out _), Is.EqualTo(JitterResult.Packet));
    }

    [Test]
    public void LostPacketUsesFecFromNext()
    {
        var buffer = new VoiceJitterBuffer(1);
        var now = TimeSpan.Zero;
        buffer.Push(1, P(1), false, now);
        buffer.Push(3, P(3), false, now);

        Assert.That(buffer.Pop(now, out _), Is.EqualTo(JitterResult.Packet));
        Assert.That(buffer.Pop(now, out var fec), Is.EqualTo(JitterResult.LostWithFec));
        Assert.That(Seq(fec!), Is.EqualTo(3), "FEC data for 2 comes from packet 3");
        Assert.That(buffer.Pop(now, out var three), Is.EqualTo(JitterResult.Packet));
        Assert.That(Seq(three!), Is.EqualTo(3));
    }

    [Test]
    public void EndOfSpurtStopsCleanly()
    {
        var buffer = new VoiceJitterBuffer(1);
        var now = TimeSpan.Zero;
        buffer.Push(1, P(1), false, now);
        buffer.Push(2, Array.Empty<byte>(), true, now);

        Assert.That(buffer.Pop(now, out _), Is.EqualTo(JitterResult.Packet));
        Assert.That(buffer.Pop(now, out _), Is.EqualTo(JitterResult.Idle));
        Assert.That(buffer.Playing, Is.False);
        Assert.That(buffer.Underruns, Is.Zero);
    }

    [Test]
    public void UnderrunConcealsThenStopsAndGrows()
    {
        var buffer = new VoiceJitterBuffer(1);
        var now = TimeSpan.Zero;
        buffer.Push(1, P(1), false, now);
        Assert.That(buffer.Pop(now, out _), Is.EqualTo(JitterResult.Packet));

        for (var i = 0; i < VoiceJitterBuffer.MaxConcealedFrames; i++)
        {
            Assert.That(buffer.Pop(now, out _), Is.EqualTo(JitterResult.Lost));
        }

        Assert.That(buffer.Pop(now, out _), Is.EqualTo(JitterResult.Idle));
        Assert.That(buffer.Underruns, Is.EqualTo(1));
        Assert.That(buffer.TargetDepth, Is.EqualTo(2));
    }

    [Test]
    public void LatePacketsAreDropped()
    {
        var buffer = new VoiceJitterBuffer(1);
        var now = TimeSpan.Zero;
        buffer.Push(5, P(5), false, now);
        buffer.Push(6, P(6), false, now);
        buffer.Pop(now, out _);
        buffer.Pop(now, out _);

        buffer.Push(4, P(4), false, now);
        Assert.That(buffer.LatePackets, Is.EqualTo(1));
        Assert.That(buffer.Buffered, Is.Zero);
    }

    [Test]
    public void SequenceWrapAround()
    {
        var buffer = new VoiceJitterBuffer(2);
        var now = TimeSpan.Zero;
        buffer.Push(65535, P(65535), false, now);
        buffer.Push(0, P(0), false, now);

        Assert.That(buffer.Pop(now, out var a), Is.EqualTo(JitterResult.Packet));
        Assert.That(Seq(a!), Is.EqualTo(65535));
        Assert.That(buffer.Pop(now, out var b), Is.EqualTo(JitterResult.Packet));
        Assert.That(Seq(b!), Is.EqualTo(0));
    }

    [Test]
    public void SimulatedLossyJitteryNetwork()
    {
        // 10% loss, up to 60 ms jitter: playback should almost never fully stall.
        var rng = new Random(42);
        var buffer = new VoiceJitterBuffer(3);
        var arrivals = new System.Collections.Generic.List<(TimeSpan Time, ushort Seq)>();

        for (ushort seq = 0; seq < 500; seq++)
        {
            if (rng.NextDouble() < 0.1)
                continue;

            arrivals.Add((Frame * seq + TimeSpan.FromMilliseconds(rng.Next(0, 60)), seq));
        }

        arrivals.Sort((x, y) => x.Time.CompareTo(y.Time));

        var played = 0;
        var concealed = 0;
        var arrivalIndex = 0;
        for (var t = TimeSpan.Zero; t < Frame * 520; t += Frame)
        {
            while (arrivalIndex < arrivals.Count && arrivals[arrivalIndex].Time <= t)
            {
                var seq = arrivals[arrivalIndex++].Seq;
                buffer.Push(seq, P(seq), false, t);
            }

            switch (buffer.Pop(t, out _))
            {
                case JitterResult.Packet:
                    played++;
                    break;
                case JitterResult.Lost:
                case JitterResult.LostWithFec:
                    concealed++;
                    break;
            }
        }

        Assert.That(played, Is.GreaterThan(400));
        Assert.That(buffer.Underruns, Is.LessThan(5));
        Assert.That(concealed, Is.LessThan(100));
    }
}
