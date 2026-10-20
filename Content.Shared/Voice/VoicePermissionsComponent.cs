namespace Content.Shared.Voice;

/// <summary>Server-side entity mute. Account-based administrative mutes must survive respawn separately.</summary>
[RegisterComponent]
public sealed partial class VoiceMutedComponent : Component;

/// <summary>Explicit voice hearing denial for gameplay integration.</summary>
[RegisterComponent]
public sealed partial class VoiceDeafComponent : Component;
