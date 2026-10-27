using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /*
     * Server: general
     */

    /// <summary>
    ///     Master switch for proximity voice chat. When false the server drops all voice packets
    ///     and clients hide voice controls.
    /// </summary>
    public static readonly CVarDef<bool> VoiceEnabled =
        CVarDef.Create("voice.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Target Opus bitrate (bits per second) clients should encode voice at.
    /// </summary>
    public static readonly CVarDef<int> VoiceBitrate =
        CVarDef.Create("voice.bitrate", 24000, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Duration of a single voice frame in milliseconds. Must be 10, 20, 40 or 60.
    /// </summary>
    public static readonly CVarDef<int> VoiceFrameMs =
        CVarDef.Create("voice.frame_ms", 20, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Packet loss percentage clients tell their encoder to expect. Higher values add more forward error correction.
    /// </summary>
    public static readonly CVarDef<int> VoiceExpectedLoss =
        CVarDef.Create("voice.expected_loss", 10, CVar.SERVER | CVar.REPLICATED);

    /*
     * Server: anti-abuse
     */

    /// <summary>
    ///     Largest Opus payload, in bytes, the server accepts from a client.
    /// </summary>
    public static readonly CVarDef<int> VoiceMaxPacketBytes =
        CVarDef.Create("voice.max_packet_bytes", 400, CVar.SERVER);

    /// <summary>
    ///     Sustained voice packets per second allowed per client.
    /// </summary>
    public static readonly CVarDef<float> VoiceMaxPacketsPerSecond =
        CVarDef.Create("voice.max_packets_per_second", 60f, CVar.SERVER);

    /// <summary>
    ///     How many milliseconds of audio a client may send ahead of real time (burst allowance for network jitter).
    /// </summary>
    public static readonly CVarDef<int> VoiceMaxBurstMs =
        CVarDef.Create("voice.max_burst_ms", 400, CVar.SERVER);

    /// <summary>
    ///     If a client exceeds the rate limits this many times within a minute, admins are alerted (logged).
    /// </summary>
    public static readonly CVarDef<int> VoiceAbuseAlertThreshold =
        CVarDef.Create("voice.abuse_alert_threshold", 200, CVar.SERVER);

    /// <summary>
    ///     Maximum number of different speakers a single listener receives at the same time. Quietest are dropped.
    /// </summary>
    public static readonly CVarDef<int> VoiceMaxStreamsPerListener =
        CVarDef.Create("voice.max_streams_per_listener", 12, CVar.SERVER);

    /*
     * Server: propagation
     */

    /// <summary>
    ///     Maximum acoustic distance (in tiles) local voice can travel. Packets are not sent beyond this.
    /// </summary>
    public static readonly CVarDef<float> VoiceRange =
        CVarDef.Create("voice.range", 10f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Distance (in tiles) within which local voice is at full volume.
    /// </summary>
    public static readonly CVarDef<float> VoiceFullVolumeRange =
        CVarDef.Create("voice.full_volume_range", 2f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Shape of the falloff between the full-volume range and the maximum range.
    ///     1 is linear, higher values drop off faster near the speaker.
    /// </summary>
    public static readonly CVarDef<float> VoiceFalloffExponent =
        CVarDef.Create("voice.falloff_exponent", 1.6f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Listeners receiving less than this gain (0-1) are not sent the packet at all.
    /// </summary>
    public static readonly CVarDef<float> VoiceMinAudibleGain =
        CVarDef.Create("voice.min_audible_gain", 0.02f, CVar.SERVER);

    /// <summary>
    ///     How often (seconds) the audibility between active speakers and listeners is recalculated.
    /// </summary>
    public static readonly CVarDef<float> VoiceAudibilityInterval =
        CVarDef.Create("voice.audibility_interval", 0.2f, CVar.SERVER);

    /// <summary>
    ///     Maximum age (seconds) of a cached acoustic propagation field before it is rebuilt even if nothing changed.
    ///     Bounds how stale atmosphere-dependent results can be.
    /// </summary>
    public static readonly CVarDef<float> VoiceFieldMaxAge =
        CVarDef.Create("voice.field_max_age", 1f, CVar.SERVER);

    /// <summary>
    ///     If true, local voice propagates along acoustic paths (walls, doors, windows, vacuum).
    ///     If false, only straight-line distance is used.
    /// </summary>
    public static readonly CVarDef<bool> VoiceAcoustics =
        CVarDef.Create("voice.acoustics", true, CVar.SERVER);

    /// <summary>
    ///     Effective extra distance (tiles) for sound passing an air-blocking structure that has no explicit
    ///     voice barrier configuration. Negative means such structures block voice completely.
    /// </summary>
    public static readonly CVarDef<float> VoiceDefaultBarrierPenalty =
        CVarDef.Create("voice.default_barrier_penalty", -1f, CVar.SERVER);

    /// <summary>
    ///     Muffling (0-1) applied when passing an air-blocking structure without explicit voice barrier configuration.
    /// </summary>
    public static readonly CVarDef<float> VoiceDefaultBarrierMuffle =
        CVarDef.Create("voice.default_barrier_muffle", 0.8f, CVar.SERVER);

    /// <summary>
    ///     Below this pressure (kPa) sound does not propagate through a tile at all.
    /// </summary>
    public static readonly CVarDef<float> VoiceMinPressure =
        CVarDef.Create("voice.min_pressure", 10f, CVar.SERVER);

    /// <summary>
    ///     At or above this pressure (kPa) speech is at full volume. Between min and full it fades out.
    /// </summary>
    public static readonly CVarDef<float> VoiceFullPressure =
        CVarDef.Create("voice.full_pressure", 70f, CVar.SERVER);

    /// <summary>
    ///     Maximum distance (tiles) voice can cross between different grids, or between a grid and open map space.
    ///     Mainly so people can talk across a docking connection. 0 disables cross-grid local voice.
    /// </summary>
    public static readonly CVarDef<float> VoiceCrossGridRange =
        CVarDef.Create("voice.cross_grid_range", 2.5f, CVar.SERVER);

    /// <summary>
    ///     Extra distance (tiles) for voice escaping a container that has no explicit voice configuration.
    /// </summary>
    public static readonly CVarDef<float> VoiceDefaultContainerPenalty =
        CVarDef.Create("voice.default_container_penalty", 1f, CVar.SERVER);

    /// <summary>
    ///     Muffling (0-1) for voice escaping a container that has no explicit voice configuration.
    /// </summary>
    public static readonly CVarDef<float> VoiceDefaultContainerMuffle =
        CVarDef.Create("voice.default_container_muffle", 0.3f, CVar.SERVER);

    /*
     * Server: game rules
     */

    /// <summary>
    ///     Whether ghosts (with ghost hearing enabled) hear living players' local voice.
    /// </summary>
    public static readonly CVarDef<bool> VoiceGhostsHearLiving =
        CVarDef.Create("voice.ghosts_hear_living", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Whether ghosts can talk to each other with voice.
    /// </summary>
    public static readonly CVarDef<bool> VoiceGhostVoice =
        CVarDef.Create("voice.ghost_voice", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Whether living players can hear ghosts. Off by default; a spooky option for special events.
    /// </summary>
    public static readonly CVarDef<bool> VoiceLivingHearGhosts =
        CVarDef.Create("voice.living_hear_ghosts", false, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Muffling (0-1) applied to everything a sleeping listener hears. 1 means sleeping players hear nothing.
    /// </summary>
    public static readonly CVarDef<float> VoiceSleepingMuffle =
        CVarDef.Create("voice.sleeping_muffle", 0.7f, CVar.SERVER);

    /// <summary>
    ///     Whether critically injured listeners can hear (heavily muffled) local voice.
    /// </summary>
    public static readonly CVarDef<bool> VoiceCritCanHear =
        CVarDef.Create("voice.crit_can_hear", true, CVar.SERVER);

    /// <summary>
    ///     Whether voice can be transmitted over radio channels.
    /// </summary>
    public static readonly CVarDef<bool> VoiceRadioEnabled =
        CVarDef.Create("voice.radio_enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Gain multiplier for nearby people hearing someone speak into a radio (radio speech is quieter, like
    ///     whispering into a headset). 0 makes radio transmissions inaudible locally.
    /// </summary>
    public static readonly CVarDef<float> VoiceRadioLocalGain =
        CVarDef.Create("voice.radio_local_gain", 0.6f, CVar.SERVER);

    /// <summary>
    ///     Volume of radio voice played out loud by intercoms and handheld radios with their speaker on, relative to
    ///     someone talking at the same spot. 0 turns it off.
    /// </summary>
    public static readonly CVarDef<float> VoiceLoudspeakerGain =
        CVarDef.Create("voice.loudspeaker_gain", 0.8f, CVar.SERVER);

    /// <summary>
    ///     Whether intercoms and handheld radios with their microphone on broadcast voice spoken near them.
    /// </summary>
    public static readonly CVarDef<bool> VoiceRadioMicrophones =
        CVarDef.Create("voice.radio_microphones", true, CVar.SERVER);

    /// <summary>
    ///     Seconds of talking before a creature that can't speak (hamster, dog...) makes its first noise. Their voice
    ///     itself is never transmitted.
    /// </summary>
    public static readonly CVarDef<float> VoiceCreatureNoiseDelay =
        CVarDef.Create("voice.creature_noise_delay", 0.4f, CVar.SERVER);

    /// <summary>
    ///     Average seconds between a creature's noises while it keeps talking.
    /// </summary>
    public static readonly CVarDef<float> VoiceCreatureNoiseInterval =
        CVarDef.Create("voice.creature_noise_interval", 1.5f, CVar.SERVER);

    /// <summary>
    ///     Maximum pitch offset (semitones) a character voice profile may use.
    /// </summary>
    public static readonly CVarDef<float> VoiceProfileMaxPitch =
        CVarDef.Create("voice.profile_max_pitch", 6f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum formant offset (semitones) a character voice profile may use.
    /// </summary>
    public static readonly CVarDef<float> VoiceProfileMaxFormant =
        CVarDef.Create("voice.profile_max_formant", 4f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Voice filter prototype applied on the receiving side to radio voice.
    /// </summary>
    public static readonly CVarDef<string> VoiceRadioFilter =
        CVarDef.Create("voice.radio_filter", "RadioDefault", CVar.SERVER | CVar.REPLICATED);

    /*
     * Client
     */

    /// <summary>
    ///     Client-side toggle for voice chat (both speaking and hearing).
    /// </summary>
    public static readonly CVarDef<bool> VoiceClientEnabled =
        CVarDef.Create("voice.client_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Name of the microphone to use. Empty for the system default.
    /// </summary>
    public static readonly CVarDef<string> VoiceInputDevice =
        CVarDef.Create("voice.input_device", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Linear gain applied to the microphone.
    /// </summary>
    public static readonly CVarDef<float> VoiceInputGain =
        CVarDef.Create("voice.input_gain", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Volume of local (proximity) voice.
    /// </summary>
    public static readonly CVarDef<float> VoiceOutputVolume =
        CVarDef.Create("voice.output_volume", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Volume of radio voice.
    /// </summary>
    public static readonly CVarDef<float> VoiceRadioVolume =
        CVarDef.Create("voice.radio_volume", 0.9f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     0 = push to talk, 1 = voice activation.
    /// </summary>
    public static readonly CVarDef<int> VoiceInputMode =
        CVarDef.Create("voice.input_mode", 0, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Voice activation threshold in dBFS.
    /// </summary>
    public static readonly CVarDef<float> VoiceActivationThreshold =
        CVarDef.Create("voice.activation_threshold", -38f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     How long (ms) voice activation keeps transmitting after the level drops below the threshold.
    /// </summary>
    public static readonly CVarDef<int> VoiceActivationRelease =
        CVarDef.Create("voice.activation_release_ms", 400, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Noise gate threshold in dBFS applied to the microphone while transmitting. Very low values disable it.
    /// </summary>
    public static readonly CVarDef<float> VoiceNoiseGate =
        CVarDef.Create("voice.noise_gate_db", -60f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     How strongly steady background noise (fans, hum, hiss) is removed from the microphone. 0 = off, 1 = strongest.
    /// </summary>
    public static readonly CVarDef<float> VoiceNoiseSuppression =
        CVarDef.Create("voice.noise_suppression", 0f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     How much quieter other game sounds get while someone is heard talking. 0 = off, 0.9 = almost silent.
    /// </summary>
    public static readonly CVarDef<float> VoiceDucking =
        CVarDef.Create("voice.ducking", 0f, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Volume per radio channel, as "Channel=volume" pairs separated by commas (e.g. "Common=0.5,Security=1.5").
    ///     Channels not listed play at 1.
    /// </summary>
    public static readonly CVarDef<string> VoiceRadioChannelVolumes =
        CVarDef.Create("voice.radio_channel_volumes", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Radio channel the second radio push-to-talk key transmits on. Empty = not set.
    /// </summary>
    public static readonly CVarDef<string> VoiceRadioKey2Channel =
        CVarDef.Create("voice.radio_key2_channel", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Radio channel the third radio push-to-talk key transmits on. Empty = not set.
    /// </summary>
    public static readonly CVarDef<string> VoiceRadioKey3Channel =
        CVarDef.Create("voice.radio_key3_channel", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Radio channel the fourth radio push-to-talk key transmits on. Empty = not set.
    /// </summary>
    public static readonly CVarDef<string> VoiceRadioKey4Channel =
        CVarDef.Create("voice.radio_key4_channel", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Whether to show speaking indicators above characters.
    /// </summary>
    public static readonly CVarDef<bool> VoiceShowIndicators =
        CVarDef.Create("voice.show_indicators", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Whether to show the radio panel (who is talking on the radio, which channels to listen and talk on).
    /// </summary>
    public static readonly CVarDef<bool> VoiceShowRadioPanel =
        CVarDef.Create("voice.show_radio_panel", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Where the player dragged the radio panel to, as "x,y" in UI units from the top left. Empty for the default
    ///     position.
    /// </summary>
    public static readonly CVarDef<string> VoiceRadioPanelPosition =
        CVarDef.Create("voice.radio_panel_position", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Whether the radio panel's channel settings are expanded.
    /// </summary>
    public static readonly CVarDef<bool> VoiceRadioPanelExpanded =
        CVarDef.Create("voice.radio_panel_expanded", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Comma separated radio channel IDs the player doesn't want to hear voice on.
    /// </summary>
    public static readonly CVarDef<string> VoiceRadioMutedChannels =
        CVarDef.Create("voice.radio_muted_channels", string.Empty, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    ///     Target jitter buffer depth in milliseconds. Larger is smoother on bad connections but adds latency.
    /// </summary>
    public static readonly CVarDef<int> VoiceJitterBufferMs =
        CVarDef.Create("voice.jitter_buffer_ms", 60, CVar.CLIENTONLY | CVar.ARCHIVE);
}
