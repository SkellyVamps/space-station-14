namespace Content.Shared.Voice;

/// <summary>
/// Fixed parameters of the voice pipeline shared by client and server.
/// </summary>
public static class VoiceConstants
{
    /// <summary>
    /// Sample rate of all voice audio (capture, codec and playback).
    /// </summary>
    public const int SampleRate = 48000;

    /// <summary>
    /// Hard cap for a single Opus payload on the wire. The server applies a stricter configurable limit.
    /// </summary>
    public const int MaxWirePayloadBytes = 1275;

    /// <summary>
    /// Hard cap on the length of a radio channel ID or speaker name in a voice message.
    /// </summary>
    public const int MaxWireStringLength = 64;

    /// <summary>
    /// Frame durations the codec setup accepts, in milliseconds.
    /// </summary>
    public static bool IsValidFrameMs(int ms) => ms is 10 or 20 or 40 or 60;

    public static int SamplesForMs(int ms) => SampleRate / 1000 * ms;
}
