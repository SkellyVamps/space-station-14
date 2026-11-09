using System.Threading;
using System.Threading.Tasks;

namespace Content.Server.Voice.Transcription;

/// <summary>
/// Turns a piece of speech into text.
/// </summary>
public interface IVoiceTranscriber : IDisposable
{
    /// <summary>
    /// Sample rate <see cref="TranscribeAsync"/> expects.
    /// </summary>
    public const int SampleRate = 16000;

    /// <summary>
    /// Gets ready (downloading and loading the model), so the first sentence isn't held up by it.
    /// </summary>
    Task LoadAsync(CancellationToken cancel);

    /// <summary>
    /// Transcribes mono audio at <see cref="SampleRate"/>, -1 to 1. Called from a background thread, one at a time.
    /// </summary>
    /// <returns>What was said, or an empty string if nothing intelligible.</returns>
    Task<string> TranscribeAsync(float[] samples, CancellationToken cancel);
}
