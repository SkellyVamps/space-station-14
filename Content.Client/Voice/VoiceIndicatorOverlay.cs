using System.Numerics;
using Content.Client.Resources;
using Content.Shared.Radio;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

/// <summary>
/// Speaking indicators: a pulsing ring over anyone heard talking (including yourself while transmitting) and a list of
/// radio transmissions currently being heard.
/// </summary>
public sealed class VoiceIndicatorOverlay : Overlay
{
    private const string FontPath = "/Fonts/NotoSans/NotoSans-Regular.ttf";

    private readonly IEntityManager _ent;
    private readonly IPlayerManager _player;
    private readonly IPrototypeManager _proto;
    private readonly IGameTiming _timing;
    private readonly VoicePlaybackSystem _playback;
    private readonly VoiceTransmitSystem _transmit;
    private readonly SharedTransformSystem _xform;
    private readonly IEyeManager _eye;
    private readonly Font _font;

    private readonly HashSet<EntityUid> _speakers = new();
    private readonly List<(string Channel, string Name)> _radio = new();

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public VoiceIndicatorOverlay(IEntityManager ent, IPlayerManager player, IPrototypeManager proto, IGameTiming timing,
        IEyeManager eye, IResourceCache cache)
    {
        _ent = ent;
        _player = player;
        _proto = proto;
        _timing = timing;
        _eye = eye;
        _playback = ent.System<VoicePlaybackSystem>();
        _transmit = ent.System<VoiceTransmitSystem>();
        _xform = ent.System<SharedTransformSystem>();
        _font = cache.GetFont(FontPath, 12);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.ViewportControl is not { } viewport)
            return;

        var handle = args.ScreenHandle;
        var pulse = 0.75f + 0.25f * MathF.Sin((float) _timing.RealTime.TotalSeconds * 10f);
        var mapId = _eye.CurrentEye.Position.MapId;

        _speakers.Clear();
        _playback.GetLocalSpeakers(_speakers);

        if (_transmit.Transmitting && _player.LocalEntity is { } local)
            _speakers.Add(local);

        foreach (var speaker in _speakers)
        {
            if (!_ent.TryGetComponent(speaker, out TransformComponent? xform) || xform.MapID != mapId)
                continue;

            var world = _xform.GetWorldPosition(xform);
            var screen = viewport.WorldToScreen(world) - new Vector2(0, 48f);
            var radio = speaker == _player.LocalEntity && _transmit.TransmittingRadio;
            var color = (radio ? Color.Gold : Color.LimeGreen).WithAlpha(pulse);

            handle.DrawCircle(screen, 6f, color);
            handle.DrawCircle(screen, 10f * pulse, color.WithAlpha(0.35f * pulse), false);
            handle.DrawCircle(screen, 14f * pulse, color.WithAlpha(0.2f * pulse), false);
        }

        _radio.Clear();
        _playback.GetRadioSpeakers(_radio);
        if (_radio.Count == 0)
            return;

        var position = new Vector2(args.ViewportBounds.Right - 280f, args.ViewportBounds.Top + 70f);
        foreach (var (channelId, name) in _radio)
        {
            var color = Color.LightGray;
            var channelName = channelId;
            if (_proto.TryIndex<RadioChannelPrototype>(channelId, out var channel))
            {
                color = channel.Color;
                channelName = channel.LocalizedName;
            }

            handle.DrawCircle(position + new Vector2(-10f, 8f), 4f, color.WithAlpha(pulse));
            handle.DrawString(_font, position, $"[{channelName}] {name}", color);
            position.Y += 18f;
        }
    }
}
