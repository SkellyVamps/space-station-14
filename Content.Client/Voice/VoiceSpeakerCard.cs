using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Voice;

/// <summary>
/// One person talking, in the voice panel: their name, how they're heard (radio channel, intercom...), a coloured edge
/// and a scrolling waveform of their voice. Stays a moment after they stop, then fades away.
/// </summary>
public sealed class VoiceSpeakerCard : Control
{
    private const int HistoryLength = 60;

    /// <summary>
    /// Seconds per waveform column.
    /// </summary>
    private const float ColumnTime = 0.05f;

    private const float HoldTime = 1.2f;
    private const float FadeTime = 0.5f;

    private static readonly Color Background = Color.FromHex("#0E0E12").WithAlpha(0.72f);

    private readonly float[] _history = new float[HistoryLength];
    private readonly Label _name;
    private readonly Label _route;
    private int _head;
    private int _count;
    private float _sinceColumn;
    private float _level;
    private float _silentFor;
    private Color _color = Color.White;

    public readonly string Key;

    /// <summary>
    /// Finished fading out: remove it.
    /// </summary>
    public bool Expired => _silentFor >= HoldTime + FadeTime;

    public VoiceSpeakerCard(string key)
    {
        Key = key;
        MinSize = new Vector2(0, 24);
        HorizontalExpand = true;
        RectClipContent = true;

        _name = new Label
        {
            HorizontalExpand = true,
            ClipText = true,
            VerticalAlignment = VAlignment.Center,
        };

        _route = new Label
        {
            VerticalAlignment = VAlignment.Center,
            Visible = false,
        };

        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            Margin = new Thickness(9, 0, 6, 0),
            Children = { _name, _route },
        });
    }

    /// <param name="talking">Still talking. Once false the card holds briefly, then fades.</param>
    /// <param name="level">How loud they are right now, 0-1.</param>
    /// <param name="route">How they're heard (channel name...), or null when in person.</param>
    public void Update(float frameTime, bool talking, float level, string name, string? route, Color color)
    {
        _level = Math.Clamp(level, 0f, 1f);
        _silentFor = talking ? 0f : _silentFor + frameTime;
        _color = color;

        if (_name.Text != name)
            _name.Text = name;

        _route.Visible = route != null;
        if (route != null && _route.Text != route)
            _route.Text = route;

        _route.FontColorOverride = color;

        // Scroll the waveform along at a steady speed whatever the frame rate.
        _sinceColumn = MathF.Min(_sinceColumn + frameTime, ColumnTime * HistoryLength);
        while (_sinceColumn >= ColumnTime)
        {
            _sinceColumn -= ColumnTime;
            _history[_head] = _level;
            _head = (_head + 1) % HistoryLength;
            _count = Math.Min(_count + 1, HistoryLength);
        }

        var fade = _silentFor <= HoldTime ? 1f : 1f - (_silentFor - HoldTime) / FadeTime;
        Modulate = Color.White.WithAlpha(Math.Clamp(fade, 0f, 1f));
    }

    /// <summary>
    /// They're no longer heard at all: keep what the card says and let it fade out.
    /// </summary>
    public void Fade(float frameTime)
    {
        Update(frameTime, false, 0f, _name.Text ?? string.Empty, _route.Visible ? _route.Text : null, _color);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var width = (float) PixelWidth;
        var height = (float) PixelHeight;
        handle.DrawRect(new UIBox2(0f, 0f, width, height), Background);

        // Newest on the right, moving left. The partial column keeps the motion smooth between steps.
        var fill = _color.WithAlpha(0.32f);
        var columnWidth = width / (HistoryLength - 1);
        var offset = _sinceColumn / ColumnTime * columnWidth;

        DrawColumn(handle, width - offset, width, _level, height, fill);
        for (var i = 0; i < _count; i++)
        {
            var right = width - offset - i * columnWidth;
            if (right <= 0f)
                break;

            var level = _history[(_head - 1 - i + HistoryLength) % HistoryLength];
            DrawColumn(handle, MathF.Max(0f, right - columnWidth), right, level, height, fill);
        }

        // Coloured edge: who's talking how, at a glance.
        handle.DrawRect(new UIBox2(0f, 0f, MathF.Max(2f, 3f * UIScale), height), _color);
    }

    private static void DrawColumn(DrawingHandleScreen handle, float left, float right, float level, float height,
        Color color)
    {
        if (level <= 0.01f || right <= left)
            return;

        // Mirrored around the middle, like an audio waveform.
        var half = level * (height - 4f) * 0.5f;
        var middle = height * 0.5f;
        handle.DrawRect(new UIBox2(left, middle - half, right, middle + half), color);
    }
}
