namespace Content.Shared.Voice.Dsp;

/// <summary>
/// A real-time mono audio processing stage. Samples are floats in roughly [-1, 1].
/// </summary>
/// <remarks>
/// Implementations must be allocation free in <see cref="Process"/> and tolerate any block size.
/// </remarks>
public interface IVoiceProcessor
{
    /// <summary>
    /// Processes <paramref name="buffer"/> in place.
    /// </summary>
    void Process(Span<float> buffer);

    /// <summary>
    /// Clears internal state (filter memory, delay lines...), e.g. at the start of a new talk spurt.
    /// </summary>
    void Reset();
}
