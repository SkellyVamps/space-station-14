using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

/// <summary>
/// Adds or removes the speaking indicator overlay according to the player's settings.
/// </summary>
public sealed partial class VoiceIndicatorSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IResourceCache _cache = default!;

    private VoiceIndicatorOverlay? _overlay;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, CCVars.VoiceShowIndicators, SetEnabled, true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        SetEnabled(false);
    }

    private void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            _overlay ??= new VoiceIndicatorOverlay(EntityManager, _player, _proto, _timing, _eye, _cache);
            if (!_overlays.HasOverlay<VoiceIndicatorOverlay>())
                _overlays.AddOverlay(_overlay);
        }
        else if (_overlay != null)
        {
            _overlays.RemoveOverlay(_overlay);
        }
    }
}
