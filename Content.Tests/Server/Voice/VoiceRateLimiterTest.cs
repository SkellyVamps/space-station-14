using System;
using Content.Server.Voice;
using NUnit.Framework;

namespace Content.Tests.Server.Voice;

[TestFixture, Parallelizable]
[TestOf(typeof(VoiceRateLimiter))]
[TestOf(typeof(VoiceSequenceWindow))]
public sealed class VoiceRateLimiterTest
{
    [Test]
    public void RealTimeAudioPasses()
    {
        var limiter = new VoiceRateLimiter();
        var now = TimeSpan.Zero;

        // 10 seconds of 20 ms frames, delivered on time.
        for (var i = 0; i < 500; i++)
        {
            Assert.That(limiter.TryConsume(now, 20, 60, 400), $"frame {i}");
            now += TimeSpan.FromMilliseconds(20);
        }
    }

    [Test]
    public void JitteredBurstPasses()
    {
        var limiter = new VoiceRateLimiter();
        var now = TimeSpan.FromSeconds(1);

        // Network hiccup: 200 ms of frames arrive at once, then normal.
        for (var i = 0; i < 10; i++)
        {
            Assert.That(limiter.TryConsume(now, 20, 60, 400));
        }

        for (var i = 0; i < 50; i++)
        {
            now += TimeSpan.FromMilliseconds(20);
            Assert.That(limiter.TryConsume(now, 20, 60, 400));
        }
    }

    [Test]
    public void FasterThanRealTimeIsRejected()
    {
        // Scenario I: a client sending 3x real time audio.
        var limiter = new VoiceRateLimiter();
        var now = TimeSpan.Zero;
        var accepted = 0;

        for (var i = 0; i < 300; i++)
        {
            if (limiter.TryConsume(now, 60, 1000, 400))
                accepted++;

            now += TimeSpan.FromMilliseconds(20);
        }

        // 6 seconds elapsed: at most ~6.3 s of audio + 0.4 s burst may get through, of the 18 s sent.
        Assert.That(accepted * 60, Is.LessThan(7000));
        Assert.That(limiter.Rejected, Is.GreaterThan(150));
    }

    [Test]
    public void TinyPacketFloodIsRejected()
    {
        var limiter = new VoiceRateLimiter();
        var now = TimeSpan.Zero;
        var accepted = 0;

        // 1000 empty packets within one second.
        for (var i = 0; i < 1000; i++)
        {
            if (limiter.TryConsume(now, 0, 60, 400))
                accepted++;

            now += TimeSpan.FromMilliseconds(1);
        }

        Assert.That(accepted, Is.LessThan(100));
    }

    [Test]
    public void SequenceWindowRejectsDuplicates()
    {
        var window = new VoiceSequenceWindow();
        Assert.That(window.Accept(100));
        Assert.That(window.Accept(101));
        Assert.That(window.Accept(101), Is.False, "duplicate");
        Assert.That(window.Accept(103));
        Assert.That(window.Accept(102), "reordered but new");
        Assert.That(window.Accept(102), Is.False, "replayed");
        Assert.That(window.Accept(20), Is.False, "too old");
    }

    [Test]
    public void SequenceWindowHandlesWrapAndRestart()
    {
        var window = new VoiceSequenceWindow();
        Assert.That(window.Accept(65534));
        Assert.That(window.Accept(65535));
        Assert.That(window.Accept(0), "wraps");
        Assert.That(window.Accept(1));

        // Client restarted from scratch far away.
        Assert.That(window.Accept(30000));
        Assert.That(window.Accept(30001));
    }
}
