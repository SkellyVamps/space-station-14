using Robust.Shared.GameStates;

namespace Content.Shared.Voice.Components;

/// <summary>
/// Marks a creature whose speech nobody can understand, even if it has no accent that makes that obvious in text.
/// Its microphone audio is never sent to anyone; its speech sound plays instead.
/// </summary>
/// <remarks>
/// Creatures whose text speech is fully replaced (a <c>ReplacementAccent</c> with <c>fullReplacements</c>, like
/// mice and dogs, or the monkey accent) are already treated this way and don't need it.
/// </remarks>
[RegisterComponent, NetworkedComponent]
public sealed partial class VoiceUnintelligibleComponent : Component;
