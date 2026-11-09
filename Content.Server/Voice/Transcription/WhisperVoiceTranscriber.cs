using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace Content.Server.Voice.Transcription;

/// <summary>
/// Speech recognition with OpenAI's Whisper model through whisper.cpp (Whisper.net), running locally on the server's
/// CPU. Nothing is sent anywhere. The model file is downloaded once if missing and allowed.
/// </summary>
public sealed partial class WhisperVoiceTranscriber : IVoiceTranscriber
{
    private readonly string _modelPath;
    private readonly GgmlType _model;
    private readonly bool _download;
    private readonly string _language;
    private readonly int _threads;
    private readonly ISawmill _log;

    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;

    /// <summary>
    /// What Whisper writes for non-speech: "[BLANK_AUDIO]", "(music)", "*cough*"...
    /// </summary>
    [GeneratedRegex(@"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*")]
    private static partial Regex NonSpeech();

    public WhisperVoiceTranscriber(string modelPath, GgmlType model, bool download, string language, int threads,
        ISawmill log)
    {
        _modelPath = modelPath;
        _model = model;
        _download = download;
        _language = language;
        _threads = threads;
        _log = log;
    }

    public async Task LoadAsync(CancellationToken cancel)
    {
        _processor ??= await CreateProcessor(cancel);
    }

    public async Task<string> TranscribeAsync(float[] samples, CancellationToken cancel)
    {
        var processor = _processor ??= await CreateProcessor(cancel);

        var text = new StringBuilder();
        await foreach (var segment in processor.ProcessAsync(samples, cancel))
        {
            var part = NonSpeech().Replace(segment.Text, string.Empty).Trim();
            if (part.Length == 0)
                continue;

            if (text.Length > 0)
                text.Append(' ');

            text.Append(part);
        }

        return text.ToString();
    }

    private async Task<WhisperProcessor> CreateProcessor(CancellationToken cancel)
    {
        if (!File.Exists(_modelPath))
        {
            if (!_download)
                throw new FileNotFoundException($"Speech recognition model not found at {_modelPath}");

            _log.Info($"Downloading speech recognition model {_model} to {_modelPath}...");
            Directory.CreateDirectory(Path.GetDirectoryName(_modelPath)!);

            // Download next to it and rename at the end, so an interrupted download is never mistaken for a model.
            var partial = _modelPath + ".download";
            await using (var source = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(_model,
                             QuantizationType.NoQuantization, cancel))
            await using (var file = File.Create(partial))
            {
                await source.CopyToAsync(file, cancel);
            }

            File.Move(partial, _modelPath, true);
            _log.Info("Speech recognition model downloaded.");
        }

        _factory = WhisperFactory.FromPath(_modelPath);

        var builder = _factory.CreateBuilder()
            .WithThreads(Math.Max(1, _threads))
            // Each piece of speech is transcribed on its own: don't let one bleed into the next.
            .WithNoContext()
            .WithNoSpeechThreshold(0.6f);

        builder = _language == "auto" ? builder.WithLanguageDetection() : builder.WithLanguage(_language);

        _log.Info($"Speech recognition ready ({_model}, {_threads} threads).");
        return builder.Build();
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
