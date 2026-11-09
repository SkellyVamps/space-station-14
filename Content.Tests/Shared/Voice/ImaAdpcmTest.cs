using System;
using Content.Shared.Voice.Codecs;
using NUnit.Framework;

namespace Content.Tests.Shared.Voice;

[TestFixture]
[TestOf(typeof(ImaAdpcm))]
public sealed class ImaAdpcmTest
{
    private static short[] TestFrame()
    {
        var frame = new short[ImaAdpcm.FrameSamples];
        for (var i = 0; i < frame.Length; i++)
        {
            frame[i] = (short) Math.Round(Math.Sin(i * 0.07) * 9000 + Math.Sin(i * 0.31) * 3000);
        }

        return frame;
    }

    [Test]
    public void MatchesTheWebpageEncoder()
    {
        // The first 32 bytes of two consecutive frames as voice-worklet.js encodes this signal (run under node).
        // If this fails, the webpage and the server no longer agree on the format.
        const string webFirst = "00000070777777179888801122332401b8ebbccbaa89082101a8ecccbccb9a99";
        const string webSecond = "36e83677011008809888081132433201b8dcdbbaab99002111b8ddcdcbbbaa8a";

        var state = new ImaAdpcm.State();
        var a = new byte[ImaAdpcm.FrameBytes];
        var b = new byte[ImaAdpcm.FrameBytes];
        ImaAdpcm.Encode(TestFrame(), ref state, a);
        ImaAdpcm.Encode(TestFrame(), ref state, b);

        Assert.That(Convert.ToHexString(a, 0, 32).ToLowerInvariant(), Is.EqualTo(webFirst));
        Assert.That(Convert.ToHexString(b, 0, 32).ToLowerInvariant(), Is.EqualTo(webSecond));
    }

    [Test]
    public void RoundTripKeepsTheSignal()
    {
        var input = TestFrame();
        var state = new ImaAdpcm.State();
        var packed = new byte[ImaAdpcm.FrameBytes];
        var output = new short[ImaAdpcm.FrameSamples];

        // Second frame: the first one is still settling from silence.
        ImaAdpcm.Encode(input, ref state, packed);
        ImaAdpcm.Encode(input, ref state, packed);
        Assert.That(ImaAdpcm.Decode(packed, output));

        double signal = 0, noise = 0;
        for (var i = 0; i < input.Length; i++)
        {
            signal += (double) input[i] * input[i];
            noise += (double) (input[i] - output[i]) * (input[i] - output[i]);
        }

        var snr = 10 * Math.Log10(signal / noise);
        Assert.That(snr, Is.GreaterThan(20), "speech-quality reconstruction");
    }

    [Test]
    public void RejectsMalformedFrames()
    {
        var output = new short[ImaAdpcm.FrameSamples];
        Assert.That(ImaAdpcm.Decode(new byte[10], output), Is.False);

        var badIndex = new byte[ImaAdpcm.FrameBytes];
        badIndex[2] = 200;
        Assert.That(ImaAdpcm.Decode(badIndex, output), Is.False);
    }
}
