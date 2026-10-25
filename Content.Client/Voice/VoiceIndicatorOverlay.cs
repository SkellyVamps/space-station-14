using Content.Shared.Examine;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Containers;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

/// <summary>
/// Speaking indicators: a pulsing ring over anyone heard talking (including yourself while transmitting).
/// Radio transmissions are listed in the <see cref="VoiceRadioPanel"/> instead.
/// </summary>
/// <remarks>
/// You can hear someone through a wall, but the ring only shows when you could see them: it follows the same line of
/// sight as the field of view, and hides speakers inside containers or using stealth, so it never reveals more than
/// the game shows.
/// </remarks>
public sealed class VoiceIndicatorOverlay : Overlay
{
    private readonly IEntityManager _ent;
    private readonly IPlayerManager _player;
    private readonly IGameTiming _timing;
    private readonly VoicePlaybackSystem _playback;
    private readonly VoiceTransmitSystem _transmit;
    private readonly SharedTransformSystem _xform;
    private readonly ExamineSystemShared _examine;
    private readonly SharedContainerSystem _container;
    private readonly IEyeManager _eye;

    private readonly HashSet<EntityUid> _speakers = new();

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public VoiceIndicatorOverlay(IEntityManager ent, IPlayerManager player, IGameTiming timing, IEyeManager eye)
    {
        _ent = ent;
        _player = player;
        _timing = timing;
        _eye = eye;
        _playback = ent.System<VoicePlaybackSystem>();
        _transmit = ent.System<VoiceTransmitSystem>();
        _xform = ent.System<SharedTransformSystem>();
        _examine = ent.System<ExamineSystemShared>();
        _container = ent.System<SharedContainerSystem>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.ViewportControl is not { } viewport)
            return;

        var handle = args.ScreenHandle;
        var pulse = 0.75f + 0.25f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 10f);
        var eye = _eye.CurrentEye;
        var eyePosition = eye.Position;

        _speakers.Clear();
        _playback.GetLocalSpeakers(_speakers);

        var local = _player.LocalEntity;
        if (_transmit.Transmitting && local != null)
            _speakers.Add(local.Value);

        foreach (var speaker in _speakers)
        {
            if (!_ent.TryGetComponent(speaker, out TransformComponent? xform) || xform.MapID != eyePosition.MapId)
                continue;

            var position = _xform.GetMapCoordinates(speaker, xform);

            if (speaker != local && !IsVisible(speaker, eye.DrawFov, eyePosition, position))
                continue;

            var screen = viewport.WorldToScreen(position.Position) - new System.Numerics.Vector2(0, 48f);
            var radio = speaker == local && _transmit.TransmittingRadio;
            var color = (radio ? Color.Gold : Color.LimeGreen).WithAlpha(pulse);

            handle.DrawCircle(screen, 6f, color);
            handle.DrawCircle(screen, 10f * pulse, color.WithAlpha(0.35f * pulse), false);
            handle.DrawCircle(screen, 14f * pulse, color.WithAlpha(0.2f * pulse), false);
        }
    }

    /// <summary>
    /// Whether the speaker can be seen from the eye, the same way the game decides whether to show them.
    /// </summary>
    private bool IsVisible(EntityUid speaker, bool checkOcclusion, Robust.Shared.Map.MapCoordinates eye,
        Robust.Shared.Map.MapCoordinates speakerPosition)
    {
        // Hidden in a locker, a bag, a mech...: you hear them, but you don't know who it is.
        if (_container.IsEntityOrParentInContainer(speaker))
            return false;

        if (_ent.TryGetComponent(speaker, out SpriteComponent? sprite) && !sprite.Visible)
            return false;

        if (_ent.TryGetComponent(speaker, out StealthComponent? stealth) && stealth.Enabled)
            return false;

        // Ghosts and admins with field of view off see everything anyway.
        if (!checkOcclusion)
            return true;

        return _examine.InRangeUnOccluded(eye, speakerPosition, 0f, null);
    }
}
