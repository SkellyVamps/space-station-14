using System.Numerics;
using Content.Shared.Voice;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Voice;

/// <summary>
/// The whisper / talk / shout picker: a slider with the three words written in its bar and a see-through knob one
/// third of the bar wide, so it sits exactly over the word that's selected.
/// </summary>
public sealed class VoiceRangeSlider : Slider
{
    private static readonly Color Unselected = Color.FromHex("#8A8A96");

    private readonly Label[] _labels = new Label[3];

    public VoiceRangeSlider()
    {
        MinValue = 0;
        MaxValue = 2;
        Rounded = true;
        MinHeight = 22;

        // No coloured fill: just a dark bar with the words in it.
        FillStyleBoxOverride = new StyleBoxEmpty();
        BackgroundStyleBoxOverride = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#0A0A0D"),
            BorderColor = Color.FromHex("#3E3E46"),
            BorderThickness = new Thickness(1),
        };

        // The usual knob, see-through so the word under it stays readable.
        _grabber.Modulate = Color.White.WithAlpha(0.45f);

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            MouseFilter = MouseFilterMode.Ignore,
        };

        string[] names = { "voice-range-whisper", "voice-range-talk", "voice-range-shout" };
        for (var i = 0; i < _labels.Length; i++)
        {
            _labels[i] = new Label
            {
                Text = Loc.GetString(names[i]),
                HorizontalExpand = true,
                Align = Label.AlignMode.Center,
                VAlign = Label.VAlignMode.Center,
                MouseFilter = MouseFilterMode.Ignore,
                ClipText = true,
                FontColorOverride = Unselected,
            };
            row.AddChild(_labels[i]);
        }

        // Drawn over the knob, so the selected word isn't dimmed by it.
        _grabber.Parent!.AddChild(row);
        LayoutContainer.SetAnchorPreset(row, LayoutContainer.LayoutPreset.Wide);
    }

    /// <summary>
    /// Highlights the selected mode's word in its colour.
    /// </summary>
    public void SetSelected(VoiceRangeMode mode, Color color)
    {
        for (var i = 0; i < _labels.Length; i++)
        {
            _labels[i].FontColorOverride = i == (int) mode ? color : Unselected;
        }
    }

    protected override void Resized()
    {
        // A third of the bar: at each stop the knob covers exactly one word.
        _grabber.MinWidth = Width / 3f;
        _grabber.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
        base.Resized();
    }
}
