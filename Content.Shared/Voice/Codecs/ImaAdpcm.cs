namespace Content.Shared.Voice.Codecs;

/// <summary>
/// IMA ADPCM voice frames, the format the voice webpage and the standalone voice app send microphone audio in:
/// 20 ms of 16 kHz mono (320 samples) in 163 bytes, 4 bits a sample. Simple enough to encode in a browser without any
/// libraries; the server turns it into the game's normal voice.
/// </summary>
/// <remarks>
/// <para>Frame layout: predictor (int16, little endian), step index (byte), then 160 bytes of 4-bit codes, low nibble
/// first. Each frame carries the state it starts from, so a lost frame never corrupts the next one.</para>
/// <para>Self-contained on purpose (no game types): the standalone voice app compiles this same file, and
/// <c>voice.js</c> mirrors it.</para>
/// </remarks>
public static class ImaAdpcm
{
    public const int SampleRate = 16000;
    public const int FrameSamples = 320;
    public const int HeaderBytes = 3;
    public const int FrameBytes = HeaderBytes + FrameSamples / 2;

    private static readonly int[] IndexTable =
    {
        -1, -1, -1, -1, 2, 4, 6, 8,
        -1, -1, -1, -1, 2, 4, 6, 8,
    };

    private static readonly int[] StepTable =
    {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97,
        107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
        876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871,
        5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623,
        27086, 29794, 32767,
    };

    /// <summary>
    /// Encoder state carried from one frame to the next.
    /// </summary>
    public struct State
    {
        public int Predictor;
        public int Index;
    }

    /// <summary>
    /// Encodes <see cref="FrameSamples"/> samples into <see cref="FrameBytes"/> bytes.
    /// </summary>
    public static void Encode(ReadOnlySpan<short> samples, ref State state, Span<byte> output)
    {
        if (samples.Length < FrameSamples || output.Length < FrameBytes)
            throw new ArgumentException("Wrong frame size.");

        output[0] = (byte) (state.Predictor & 0xFF);
        output[1] = (byte) ((state.Predictor >> 8) & 0xFF);
        output[2] = (byte) state.Index;

        var predictor = state.Predictor;
        var index = state.Index;

        for (var i = 0; i < FrameSamples; i++)
        {
            var step = StepTable[index];
            var diff = samples[i] - predictor;
            var code = 0;
            if (diff < 0)
            {
                code = 8;
                diff = -diff;
            }

            // Quantise the difference to 3 bits, tracking what the decoder will reconstruct.
            var delta = step >> 3;
            if (diff >= step)
            {
                code |= 4;
                diff -= step;
                delta += step;
            }

            if (diff >= step >> 1)
            {
                code |= 2;
                diff -= step >> 1;
                delta += step >> 1;
            }

            if (diff >= step >> 2)
            {
                code |= 1;
                delta += step >> 2;
            }

            predictor = Math.Clamp((code & 8) != 0 ? predictor - delta : predictor + delta, short.MinValue, short.MaxValue);
            index = Math.Clamp(index + IndexTable[code], 0, StepTable.Length - 1);

            var at = HeaderBytes + i / 2;
            if ((i & 1) == 0)
                output[at] = (byte) code;
            else
                output[at] |= (byte) (code << 4);
        }

        state.Predictor = predictor;
        state.Index = index;
    }

    /// <summary>
    /// Whether <paramref name="frame"/> is a well-formed frame.
    /// </summary>
    public static bool IsValid(ReadOnlySpan<byte> frame)
    {
        return frame.Length == FrameBytes && frame[2] < StepTable.Length;
    }

    /// <summary>
    /// Decodes one frame into <see cref="FrameSamples"/> samples.
    /// </summary>
    /// <returns>False if the frame is malformed.</returns>
    public static bool Decode(ReadOnlySpan<byte> frame, Span<short> output)
    {
        if (!IsValid(frame) || output.Length < FrameSamples)
            return false;

        int predictor = (short) (frame[0] | frame[1] << 8);
        int index = frame[2];

        for (var i = 0; i < FrameSamples; i++)
        {
            var packed = frame[HeaderBytes + i / 2];
            var code = (i & 1) == 0 ? packed & 0x0F : packed >> 4;

            var step = StepTable[index];
            var delta = step >> 3;
            if ((code & 4) != 0)
                delta += step;
            if ((code & 2) != 0)
                delta += step >> 1;
            if ((code & 1) != 0)
                delta += step >> 2;

            predictor = Math.Clamp((code & 8) != 0 ? predictor - delta : predictor + delta, short.MinValue, short.MaxValue);
            index = Math.Clamp(index + IndexTable[code], 0, StepTable.Length - 1);
            output[i] = (short) predictor;
        }

        return true;
    }
}
