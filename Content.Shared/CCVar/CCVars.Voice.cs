using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<bool> VoiceEnabled =
        CVarDef.Create("voice.enabled", false, CVar.SERVER | CVar.REPLICATED);
    public static readonly CVarDef<float> VoiceRange =
        CVarDef.Create("voice.range", 10f, CVar.SERVER | CVar.REPLICATED);
    public static readonly CVarDef<float> VoiceMinimumPressure =
        CVarDef.Create("voice.minimum_pressure", 5f, CVar.SERVER);
    public static readonly CVarDef<bool> VoiceClientEnabled =
        CVarDef.Create("voice.client_enabled", false, CVar.CLIENTONLY | CVar.ARCHIVE);
    public static readonly CVarDef<string> VoiceInputDevice =
        CVarDef.Create("voice.input_device", "", CVar.CLIENTONLY | CVar.ARCHIVE);
    public static readonly CVarDef<float> VoiceInputGain =
        CVarDef.Create("voice.input_gain", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
    public static readonly CVarDef<string> VoicePlayerVolumes =
        CVarDef.Create("voice.player_volumes", "", CVar.CLIENTONLY | CVar.ARCHIVE);
    public static readonly CVarDef<string> VoiceRadioChannel =
        CVarDef.Create("voice.radio_channel", "", CVar.CLIENTONLY | CVar.ARCHIVE);
    public static readonly CVarDef<float> VoiceOutputGain =
        CVarDef.Create("voice.output_gain", 1f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
