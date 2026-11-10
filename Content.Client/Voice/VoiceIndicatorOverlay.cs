using System.Numerics;
using Robust.Shared.Timing;
using Robust.Shared.Map;
using Robust.Shared.Configuration;
using Content.Shared.CCVar;
using Content.Shared.Radio;
using Content.Shared.Voice;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client.Voice;

/// <summary>
/// Speaking indicators: a small level meter next to the head of anyone heard talking (including yourself), three bars
/// for bass, middle and treble that move with their voice. White when talking in person, the channel's colour when
/// talking into a radio or played by an intercom, red when you're trying to talk but can't be heard.
/// </summary>
/// <remarks>
/// You can hear someone through a wall, but the meter only shows when you could see them: it follows the same line of
/// sight as the field of view, and hides speakers inside containers or using stealth, so it never reveals more than
/// the game shows.
/// </remarks>
public sealed class VoiceIndicatorOverlay : Overlay
{
    private const float Pixel = 1f / EyeManager.PixelsPerMeter;

    // Meter shape, in sprite pixels: a dark box just above the head with three bars in it.
    private static readonly Vector2 BoxOrigin = new Vector2(-6f, 17f) * Pixel;
    private static readonly Vector2 BoxSize = new Vector2(12f, 10f) * Pixel;
    private const float BarWidth = 2f;
    private const float BarGap = 1f;
    private const float BarMaxHeight = 7f;

    public static readonly Color TalkColor = Color.FromHex("#EDEDED");
    public static readonly Color WhisperColor = Color.FromHex("#3D7BE0");
    public static readonly Color ShoutColor = Color.FromHex("#F2994A");
    public static readonly Color BlockedColor = Color.FromHex("#E05050");

    private readonly IEntityManager _ent;
    private readonly IPlayerManager _player;
    private readonly IPrototypeManager _proto;
    private readonly VoicePlaybackSystem _playback;
    private readonly VoiceTransmitSystem _transmit;
    private readonly SharedTransformSystem _xform;
    private readonly VoiceIndicatorSystem _indicators;

    private readonly IConfigurationManager _cfg;
    private readonly IGameTiming _timing;

    /// <summary>
    /// After switching between whisper, talk and shout, the range circle shows this long, then fades.
    /// </summary>
    private static readonly TimeSpan RangeCircleTime = TimeSpan.FromSeconds(2.5);

    /// <summary>
    /// Draw the level meters over heads (the player can turn them off).
    /// </summary>
    public bool ShowMeters = true;

    /// <summary>
    /// Draw the circle showing how far our voice reaches.
    /// </summary>
    public bool ShowRangeCircle = true;

    private readonly List<HeardVoice> _heard = new();
    private readonly Dictionary<EntityUid, (VoiceLevels Levels, Color Color, bool Talking)> _meters = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public VoiceIndicatorOverlay(IEntityManager ent, IPlayerManager player, IPrototypeManager proto)
    {
        _ent = ent;
        _player = player;
        _proto = proto;
        _playback = ent.System<VoicePlaybackSystem>();
        _transmit = ent.System<VoiceTransmitSystem>();
        _xform = ent.System<SharedTransformSystem>();
        _indicators = ent.System<VoiceIndicatorSystem>();
        _cfg = IoCManager.Resolve<IConfigurationManager>();
        _timing = IoCManager.Resolve<IGameTiming>();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var eye = args.Viewport.Eye;
        if (eye == null)
            return;

        if (ShowRangeCircle)
            DrawRangeCircle(handle, args.MapId);

        if (!ShowMeters)
            return;

        _meters.Clear();
        _heard.Clear();
        _playback.GetHeardVoices(_heard);

        foreach (var voice in _heard)
        {
            // Radio has nobody to point at: it's in the voice panel instead.
            if (voice.Mode == VoiceReceiveMode.Radio || !_ent.TryGetEntity(voice.Speaker, out var uid))
                continue;

            var color = voice.Mode == VoiceReceiveMode.Loudspeaker ? ChannelColor(voice.Channel) : RangeColor(voice.Range);
            Merge(uid.Value, voice.Levels, color, voice.Talking);
        }

        if (_player.LocalEntity is { } local)
        {
            if (_transmit.KeyBlocked)
                Merge(local, default, BlockedColor, true);
            else if (_transmit.Transmitting || !_transmit.SelfLevels.IsSilent)
                Merge(local, _transmit.SelfLevels, SelfColor(), _transmit.Transmitting);
        }

        // Keep the meters upright on screen whichever way the camera is turned.
        var rotation = Matrix3Helpers.CreateRotation(-eye.Rotation);

        foreach (var (uid, (levels, color, talking)) in _meters)
        {
            if (!_ent.TryGetComponent(uid, out TransformComponent? xform) || xform.MapID != args.MapId)
                continue;

            var position = _xform.GetMapCoordinates(uid, xform);
            if (!args.WorldAABB.Contains(position.Position))
                continue;

            if (uid != _player.LocalEntity && !_indicators.CanSee(uid, eye))
                continue;

            // Fades out once they stop.
            var alpha = talking ? 1f : Math.Clamp(levels.Overall * 4f, 0f, 1f);
            if (alpha <= 0.01f)
                continue;

            handle.SetTransform(Matrix3x2.Multiply(rotation, Matrix3Helpers.CreateTranslation(position.Position)));
            DrawMeter(handle, levels, color, alpha);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>
    /// A circle around us showing how far our voice carries: always while whispering (stronger while actually
    /// talking), and for a moment after switching between whisper, talk and shout.
    /// </summary>
    private void DrawRangeCircle(DrawingHandleWorld handle, MapId map)
    {
        if (_player.LocalEntity is not { } local
            || !_ent.TryGetComponent(local, out TransformComponent? xform)
            || xform.MapID != map
            || !_transmit.CanTransmit(out _))
        {
            return;
        }

        var range = _transmit.Range;
        var sinceChange = _timing.RealTime - _transmit.RangeChangedAt;
        var talking = _transmit.Transmitting && _transmit.TransmitChannel == null;

        float alpha;
        if (range == VoiceRangeMode.Whisper)
            alpha = talking ? 1f : 0.65f;
        else if (_transmit.RangeChangedAt != TimeSpan.Zero && sinceChange < RangeCircleTime)
            alpha = 1f - (float) (sinceChange / RangeCircleTime);
        else
            return;

        // Same distances the server uses: the talking range, scaled for a whisper or a shout.
        var scale = range switch
        {
            VoiceRangeMode.Whisper => _cfg.GetCVar(CCVars.VoiceWhisperRangeScale),
            VoiceRangeMode.Shout => _cfg.GetCVar(CCVars.VoiceShoutRangeScale),
            _ => 1f,
        };
        var radius = _cfg.GetCVar(CCVars.VoiceRange) * scale;
        if (radius <= 0f)
            return;

        var center = _xform.GetMapCoordinates(local, xform).Position;
        var color = RangeColor(range);
        // A light fill, and a doubled edge so it reads against dark floors too.
        // Only a whisper's circle is small enough to fill; talking and shouting reach past most of the screen.
        if (range == VoiceRangeMode.Whisper)
            handle.DrawCircle(center, radius, color.WithAlpha(0.12f * alpha));
        handle.DrawCircle(center, radius, color.WithAlpha(alpha), false);
        handle.DrawCircle(center, radius - 1f / EyeManager.PixelsPerMeter, color.WithAlpha(alpha * 0.8f), false);
        handle.DrawCircle(center, radius - 2f / EyeManager.PixelsPerMeter, Color.Black.WithAlpha(alpha * 0.5f), false);
    }

    private void Merge(EntityUid uid, VoiceLevels levels, Color color, bool talking)
    {
        // Someone heard two ways at once (in person and through an intercom...): show the louder.
        if (_meters.TryGetValue(uid, out var existing) && existing.Levels.Overall >= levels.Overall)
        {
            _meters[uid] = existing with { Talking = existing.Talking || talking };
            return;
        }

        _meters[uid] = (levels, color, talking || existing.Talking);
    }

    private static void DrawMeter(DrawingHandleWorld handle, VoiceLevels levels, Color color, float alpha)
    {
        handle.DrawRect(new Box2(BoxOrigin, BoxOrigin + BoxSize), Color.Black.WithAlpha(0.6f * alpha));

        var barColor = color.WithAlpha(color.A * alpha);
        DrawBar(handle, 0, levels.Low, barColor);
        DrawBar(handle, 1, levels.Mid, barColor);
        DrawBar(handle, 2, levels.High, barColor);
    }

    private static void DrawBar(DrawingHandleWorld handle, int index, float level, Color color)
    {
        // Always at least a pixel tall, so a quiet speaker still shows three little dots.
        var height = 1f + (BarMaxHeight - 1f) * Math.Clamp(level, 0f, 1f);
        var bottom = BoxOrigin + new Vector2(2f + index * (BarWidth + BarGap), 1.5f) * Pixel;
        handle.DrawRect(new Box2(bottom, bottom + new Vector2(BarWidth, height) * Pixel), color);
    }

    private Color SelfColor()
    {
        return _transmit.TransmitChannel is { } channel ? ChannelColor(channel) : RangeColor(_transmit.Range);
    }

    /// <summary>
    /// Colour for someone heard in person: blue-grey whispering, white talking, orange shouting.
    /// </summary>
    public static Color RangeColor(VoiceRangeMode range)
    {
        return range switch
        {
            VoiceRangeMode.Whisper => WhisperColor,
            VoiceRangeMode.Shout => ShoutColor,
            _ => TalkColor,
        };
    }

    private Color ChannelColor(string? channel)
    {
        return channel != null && _proto.TryIndex<RadioChannelPrototype>(channel, out var proto)
            ? proto.Color
            : TalkColor;
    }
}
