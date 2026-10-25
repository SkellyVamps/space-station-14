using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared.Radio;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;

namespace Content.Client.Voice;

/// <summary>
/// Small HUD panel the player can drag anywhere: who is talking on the radio right now, and which of their radio
/// channels they hear and talk on.
/// </summary>
/// <remarks>
/// Deliberately not a <see cref="BaseWindow"/>: windows are closed by Escape and "close all windows", and this panel is
/// part of the HUD.
/// </remarks>
public sealed class VoiceRadioPanel : Control
{
    private const float PanelWidth = 230f;

    private readonly IPrototypeManager _proto;

    private readonly BoxContainer _speakers;
    private readonly Label _nobody;
    private readonly BoxContainer _settings;
    private readonly BoxContainer _channelRows;
    private readonly OptionButton _talk;
    private readonly Label _noChannels;
    private readonly Button _expand;

    private readonly List<(string Channel, string Name)> _shownSpeakers = new();
    private readonly List<ProtoId<RadioChannelPrototype>> _shownChannels = new();
    private readonly Dictionary<string, CheckBox> _listenBoxes = new();
    private ProtoId<RadioChannelPrototype>? _shownDefault;
    private bool _updatingTalk;

    /// <summary>
    /// The player dropped the panel somewhere new.
    /// </summary>
    public event Action<Vector2>? Moved;

    public event Action<bool>? ExpandedChanged;

    public event Action<string, bool>? ListenToggled;

    /// <summary>
    /// The player picked the channel to talk on; null for the default channel.
    /// </summary>
    public event Action<ProtoId<RadioChannelPrototype>?>? TalkChannelSelected;

    public VoiceRadioPanel(IPrototypeManager proto)
    {
        _proto = proto;
        MouseFilter = MouseFilterMode.Stop;
        MinWidth = PanelWidth;

        var background = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#1B1B1E").WithAlpha(0.92f),
            BorderColor = Color.FromHex("#3E3E46"),
            BorderThickness = new Thickness(1),
        };
        background.SetContentMarginOverride(StyleBox.Margin.All, 6);

        _expand = new Button
        {
            ToggleMode = true,
            MinWidth = 24,
            ToolTip = Loc.GetString("voice-radio-panel-settings-tooltip"),
        };
        _expand.OnToggled += args =>
        {
            SetExpanded(args.Pressed);
            ExpandedChanged?.Invoke(args.Pressed);
        };

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Children =
            {
                new Label
                {
                    Text = Loc.GetString("voice-radio-panel-title"),
                    StyleClasses = { StyleClass.LabelKeyText },
                    HorizontalExpand = true,
                },
                _expand,
            },
        };

        _nobody = new Label
        {
            Text = Loc.GetString("voice-radio-panel-nobody"),
            StyleClasses = { StyleClass.LabelSubText },
        };

        _speakers = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(0, 2, 0, 2),
            Children = { _nobody },
        };

        _talk = new OptionButton { HorizontalExpand = true };
        _talk.OnItemSelected += OnTalkSelected;

        _channelRows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        _noChannels = new Label
        {
            Text = Loc.GetString("voice-radio-panel-no-channels"),
            StyleClasses = { StyleClass.LabelSubText },
        };

        _settings = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Horizontal,
                    Children =
                    {
                        new Label
                        {
                            Text = Loc.GetString("voice-radio-panel-talk"),
                            Margin = new Thickness(0, 0, 6, 0),
                        },
                        _talk,
                    },
                },
                new Label
                {
                    Text = Loc.GetString("voice-radio-panel-listen-header"),
                    StyleClasses = { StyleClass.LabelSubText },
                    Margin = new Thickness(0, 4, 0, 0),
                },
                _channelRows,
                _noChannels,
            },
        };

        AddChild(new PanelContainer
        {
            PanelOverride = background,
            Children =
            {
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    Children = { header, _speakers, _settings },
                },
            },
        });

        SetExpanded(false);
    }

    public void SetExpanded(bool expanded)
    {
        _settings.Visible = expanded;
        _expand.Pressed = expanded;
        _expand.Text = expanded ? "-" : "+";
    }

    /// <summary>
    /// Shows the radio transmissions being heard right now.
    /// </summary>
    public void UpdateSpeakers(List<(string Channel, string Name)> speakers)
    {
        if (ListEquals(_shownSpeakers, speakers))
            return;

        _shownSpeakers.Clear();
        _shownSpeakers.AddRange(speakers);

        _speakers.RemoveAllChildren();
        if (speakers.Count == 0)
        {
            _speakers.AddChild(_nobody);
            return;
        }

        foreach (var (channelId, name) in speakers)
        {
            var (channelName, color) = GetChannel(channelId);
            _speakers.AddChild(new Label
            {
                Text = Loc.GetString("voice-radio-panel-speaker",
                    ("channel", channelName),
                    ("name", string.IsNullOrEmpty(name) ? Loc.GetString("voice-radio-panel-unknown-speaker") : name)),
                FontColorOverride = color,
                ClipText = true,
            });
        }
    }

    /// <summary>
    /// Shows the channels the player has (the same ones they can use in text chat) and their settings.
    /// </summary>
    public void UpdateChannels(List<ProtoId<RadioChannelPrototype>> channels,
        ProtoId<RadioChannelPrototype>? defaultChannel,
        ProtoId<RadioChannelPrototype>? selected,
        Func<string, bool> isMuted)
    {
        if (!ListEquals(_shownChannels, channels) || _shownDefault != defaultChannel)
            RebuildChannels(channels, defaultChannel);

        foreach (var (channel, box) in _listenBoxes)
        {
            box.Pressed = !isMuted(channel);
        }

        _updatingTalk = true;
        var index = selected is { } sel ? channels.IndexOf(sel) : -1;
        _talk.SelectId(index + 1);
        _updatingTalk = false;
    }

    private void RebuildChannels(List<ProtoId<RadioChannelPrototype>> channels,
        ProtoId<RadioChannelPrototype>? defaultChannel)
    {
        _shownChannels.Clear();
        _shownChannels.AddRange(channels);
        _shownDefault = defaultChannel;

        _channelRows.RemoveAllChildren();
        _listenBoxes.Clear();
        _talk.Clear();

        var defaultName = defaultChannel is { } def ? GetChannel(def.Id).Name : "-";
        _talk.AddItem(Loc.GetString("voice-radio-panel-talk-default", ("channel", defaultName)), 0);

        for (var i = 0; i < channels.Count; i++)
        {
            var channel = channels[i].Id;
            var (name, color) = GetChannel(channel);
            _talk.AddItem(name, i + 1);

            var box = new CheckBox
            {
                Text = name,
                ToolTip = Loc.GetString("voice-radio-panel-listen-tooltip", ("channel", name)),
            };
            box.Label.FontColorOverride = color;
            box.OnToggled += args => ListenToggled?.Invoke(channel, args.Pressed);

            _listenBoxes[channel] = box;
            _channelRows.AddChild(box);
        }

        _talk.Disabled = channels.Count == 0;
        _noChannels.Visible = channels.Count == 0;
    }

    private void OnTalkSelected(OptionButton.ItemSelectedEventArgs args)
    {
        _talk.SelectId(args.Id);
        if (_updatingTalk)
            return;

        var index = args.Id - 1;
        TalkChannelSelected?.Invoke(index >= 0 && index < _shownChannels.Count
            ? _shownChannels[index]
            : (ProtoId<RadioChannelPrototype>?) null);
    }

    private (string Name, Color Color) GetChannel(string id)
    {
        return _proto.TryIndex<RadioChannelPrototype>(id, out var channel)
            ? (channel.LocalizedName, channel.Color)
            : (id, Color.LightGray);
    }

    private static bool ListEquals<T>(List<T> a, List<T> b)
    {
        if (a.Count != b.Count)
            return false;

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < a.Count; i++)
        {
            if (!comparer.Equals(a[i], b[i]))
                return false;
        }

        return true;
    }

    #region Dragging

    // Drag from anywhere that isn't a button or checkbox (those handle the click themselves).
    private bool _dragging;
    private Vector2 _dragOffset;

    public bool IsOpen => Parent != null;

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick || Parent == null)
            return;

        _dragging = true;
        _dragOffset = args.PointerLocation.Position / UIScale - Position;
        args.Handle();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function != EngineKeyFunctions.UIClick || !_dragging)
            return;

        _dragging = false;
        Moved?.Invoke(Position);
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (!_dragging || Parent == null)
            return;

        var position = args.GlobalPosition - _dragOffset;
        position = Vector2.Clamp(position, Vector2.Zero, Vector2.Max(Vector2.Zero, Parent.Size - Size));
        LayoutContainer.SetPosition(this, position);
    }

    #endregion
}
