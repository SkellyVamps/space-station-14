using System.Numerics;
using Content.Client.Stylesheets;
using Content.Shared.Radio;
using Content.Shared.Voice;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;

namespace Content.Client.Voice;

/// <summary>
/// One line of the voice panel: someone being heard, or ourselves.
/// </summary>
/// <param name="Key">Stays the same while it's the same person heard the same way, so their card is kept.</param>
/// <param name="Route">How they're heard (channel name...), or null when in person.</param>
/// <param name="Level">How loud they are right now, 0-1.</param>
public readonly record struct VoicePanelSpeaker(string Key, string Name, string? Route, Color Color, float Level,
    bool Talking);

/// <summary>
/// Small HUD panel the player can drag anywhere: a card for everyone talking right now (nearby, on the radio, through
/// an intercom, and yourself) with a live waveform, and which of their radio channels they hear and talk on.
/// </summary>
/// <remarks>
/// Deliberately not a <see cref="BaseWindow"/>: windows are closed by Escape and "close all windows", and this panel is
/// part of the HUD.
/// </remarks>
public sealed class VoiceRadioPanel : Control
{
    private const float PanelWidth = 230f;
    private const int MaxCards = 8;
    private const float MaxListenHeight = 220f;

    private readonly IPrototypeManager _proto;

    private readonly BoxContainer _speakers;
    private readonly Dictionary<string, VoiceSpeakerCard> _cards = new();
    private readonly HashSet<string> _seen = new();
    private readonly List<VoiceSpeakerCard> _expired = new();
    private readonly BoxContainer _settings;
    private readonly BoxContainer _channelRows;
    private readonly OptionButton _talk;
    private readonly BoxContainer _talkRow;
    private readonly ScrollContainer _listenScroll;
    private readonly Label _noChannels;
    private readonly Button _expand;
    private readonly Slider _range;
    private readonly Label _rangeLabel;
    private readonly Button _mute;

    private static readonly Color MutedColor = Color.FromHex("#E04545");
    private static readonly Color AdminMutedColor = Color.FromHex("#A05BE0");

    private readonly List<ProtoId<RadioChannelPrototype>> _shownChannels = new();
    private readonly List<ProtoId<RadioChannelPrototype>> _shownListen = new();
    private readonly Dictionary<string, CheckBox> _listenBoxes = new();
    private readonly Dictionary<string, Slider> _volumeSliders = new();
    private ProtoId<RadioChannelPrototype>? _shownDefault;
    private bool _updatingTalk;
    private bool _talkBuilt;

    /// <summary>
    /// The player dropped the panel somewhere new.
    /// </summary>
    public event Action<Vector2>? Moved;

    public event Action<bool>? ExpandedChanged;

    /// <summary>
    /// The player moved the whisper / talk / shout slider.
    /// </summary>
    public event Action<VoiceRangeMode>? RangeSelected;

    /// <summary>
    /// The microphone mute button was clicked.
    /// </summary>
    public event Action? MutePressed;

    public event Action<string, bool>? ListenToggled;

    /// <summary>
    /// The player moved a channel's volume slider (0-2).
    /// </summary>
    public event Action<string, float>? VolumeChanged;

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
            BackgroundColor = Color.FromHex("#0E0E12").WithAlpha(0.88f),
        };
        background.SetContentMarginOverride(StyleBox.Margin.All, 5);

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

        // Three stops: whisper, talk, shout.
        _range = new Slider
        {
            MinValue = 0,
            MaxValue = 2,
            Rounded = true,
            Value = (int) VoiceRangeMode.Talk,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            ToolTip = Loc.GetString("voice-panel-range-tooltip"),
        };
        _range.OnValueChanged += range =>
        {
            var mode = (VoiceRangeMode) Math.Clamp((int) MathF.Round(range.Value), 0, 2);
            RangeSelected?.Invoke(mode);
        };

        _rangeLabel = new Label
        {
            MinWidth = 58,
            VerticalAlignment = VAlignment.Center,
            ToolTip = Loc.GetString("voice-panel-range-tooltip"),
        };

        var rangeRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(0, 3, 0, 0),
            Children =
            {
                _rangeLabel,
                _range,
            },
        };

        _mute = new Button
        {
            MinWidth = 62,
            Margin = new Thickness(3, 0),
        };
        _mute.OnPressed += _ => MutePressed?.Invoke();

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
                    VerticalAlignment = VAlignment.Center,
                },
                _mute,
                _expand,
            },
        };

        var headerBlock = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children = { header, rangeRow },
        };

        _speakers = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 2,
            Margin = new Thickness(0, 2, 0, 0),
        };

        _talk = new OptionButton { HorizontalExpand = true };
        _talk.OnItemSelected += OnTalkSelected;

        _talkRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
            Children =
            {
                new Label
                {
                    Text = Loc.GetString("voice-radio-panel-talk"),
                    Margin = new Thickness(0, 0, 6, 0),
                },
                _talk,
            },
        };

        _channelRows = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };

        // Observers hear every channel: scroll rather than growing off screen. A scroll container doesn't size itself
        // to its contents, so its height is set whenever the channel list changes.
        _listenScroll = new ScrollContainer
        {
            HScrollEnabled = false,
            Children = { _channelRows },
        };

        _noChannels = new Label
        {
            Text = Loc.GetString("voice-radio-panel-no-channels"),
            StyleClasses = { StyleClass.LabelSubText },
        };

        var settings = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children =
            {
                _talkRow,
                new Label
                {
                    Text = Loc.GetString("voice-radio-panel-listen-header"),
                    StyleClasses = { StyleClass.LabelSubText },
                },
                // Observers hear every channel: keep the panel a sensible size.
                _listenScroll,
                _noChannels,
            },
        };

        _settings = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(0, 2, 0, 0),
            Children = { new PanelContainer { PanelOverride = background, Children = { settings } } },
        };

        // A slim header to grab and open settings by, then the cards, each on its own dark strip.
        AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children =
            {
                new PanelContainer { PanelOverride = background, Children = { headerBlock } },
                _speakers,
                _settings,
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
    /// Shows whether we whisper, talk or shout, and whether our microphone is muted (by us, or by an admin: then the
    /// button flashes purple and red).
    /// </summary>
    /// <param name="time">Real time in seconds, for the flashing.</param>
    public void UpdateControls(VoiceRangeMode range, Color rangeColor, bool selfMuted, bool adminMuted,
        TimeSpan? adminMuteRemaining, double time)
    {
        var rangeText = Loc.GetString(range switch
        {
            VoiceRangeMode.Whisper => "voice-range-whisper",
            VoiceRangeMode.Shout => "voice-range-shout",
            _ => "voice-range-talk",
        });

        if (_rangeLabel.Text != rangeText)
            _rangeLabel.Text = rangeText;

        _rangeLabel.FontColorOverride = rangeColor;

        // Follow changes made elsewhere (the keybind), but never fight the player dragging it.
        if (!_range.Grabbed && (int) MathF.Round(_range.Value) != (int) range)
            _range.SetValueWithoutEvent((int) range);

        string muteText;
        string tooltip;
        Color? tint;
        if (adminMuted)
        {
            muteText = Loc.GetString("voice-panel-mute-admin");
            tooltip = adminMuteRemaining is { } remaining
                ? Loc.GetString("voice-panel-mute-admin-tooltip-timed", ("minutes", Math.Max(1, (int) Math.Ceiling(remaining.TotalMinutes))))
                : Loc.GetString("voice-panel-mute-admin-tooltip");
            // Alternates twice a second so it's impossible to miss.
            tint = (int) (time * 2) % 2 == 0 ? AdminMutedColor : MutedColor;
        }
        else if (selfMuted)
        {
            muteText = Loc.GetString("voice-panel-mute-on");
            tooltip = Loc.GetString("voice-panel-mute-on-tooltip");
            tint = MutedColor;
        }
        else
        {
            muteText = Loc.GetString("voice-panel-mute-off");
            tooltip = Loc.GetString("voice-panel-mute-off-tooltip");
            tint = null;
        }

        if (_mute.Text != muteText)
            _mute.Text = muteText;

        _mute.ToolTip = tooltip;
        _mute.ModulateSelfOverride = tint;
    }

    /// <summary>
    /// Shows who is talking right now. Cards of people who stopped stay a moment and fade out.
    /// </summary>
    public void UpdateSpeakers(List<VoicePanelSpeaker> speakers, float frameTime)
    {
        _seen.Clear();
        foreach (var speaker in speakers)
        {
            if (!_cards.TryGetValue(speaker.Key, out var card))
            {
                if (_cards.Count >= MaxCards)
                    continue;

                card = new VoiceSpeakerCard(speaker.Key);
                _cards[speaker.Key] = card;
                _speakers.AddChild(card);
            }

            _seen.Add(speaker.Key);
            card.Update(frameTime, speaker.Talking, speaker.Level, speaker.Name, speaker.Route, speaker.Color);
        }

        // Gone from the list (stream ended): let the card fade out on its own.
        foreach (var (key, card) in _cards)
        {
            if (!_seen.Contains(key))
                card.Fade(frameTime);

            if (card.Expired)
                _expired.Add(card);
        }

        foreach (var card in _expired)
        {
            _cards.Remove(card.Key);
            card.Orphan();
        }

        _expired.Clear();
    }

    /// <summary>
    /// Shows the channels the player can hear and talk on (the same ones they have in text chat) and their settings.
    /// </summary>
    public void UpdateChannels(List<ProtoId<RadioChannelPrototype>> listen,
        List<ProtoId<RadioChannelPrototype>> talk,
        ProtoId<RadioChannelPrototype>? defaultChannel,
        ProtoId<RadioChannelPrototype>? selected,
        Func<string, bool> isMuted,
        Func<string, float> getVolume)
    {
        if (!ListEquals(_shownListen, listen))
            RebuildListen(listen);

        if (!_talkBuilt || !ListEquals(_shownChannels, talk) || _shownDefault != defaultChannel)
            RebuildTalk(talk, defaultChannel);

        foreach (var (channel, box) in _listenBoxes)
        {
            box.Pressed = !isMuted(channel);
        }

        foreach (var (channel, slider) in _volumeSliders)
        {
            if (!slider.Grabbed)
                slider.SetValueWithoutEvent(getVolume(channel));

            slider.Disabled = isMuted(channel);
        }

        // A channel picked on an earlier body may not exist on this one: fall back to the default.
        var index = selected is { } sel ? talk.IndexOf(sel) : -1;
        var id = index + 1;
        if (_talk.SelectedId != id)
        {
            _updatingTalk = true;
            _talk.TrySelectId(id);
            _updatingTalk = false;
        }
    }

    private void RebuildListen(List<ProtoId<RadioChannelPrototype>> channels)
    {
        _shownListen.Clear();
        _shownListen.AddRange(channels);

        _channelRows.RemoveAllChildren();
        _listenBoxes.Clear();
        _volumeSliders.Clear();

        foreach (var protoId in channels)
        {
            var channel = protoId.Id;
            var (name, color) = GetChannel(channel);

            var box = new CheckBox
            {
                Text = name,
                ToolTip = Loc.GetString("voice-radio-panel-listen-tooltip", ("channel", name)),
            };
            box.Label.FontColorOverride = color;
            box.HorizontalExpand = true;
            box.OnToggled += args => ListenToggled?.Invoke(channel, args.Pressed);

            var slider = new Slider
            {
                MinValue = 0f,
                MaxValue = 2f,
                Value = 1f,
                MinWidth = 70,
                VerticalAlignment = VAlignment.Center,
                ToolTip = Loc.GetString("voice-radio-panel-volume-tooltip", ("channel", name)),
            };
            slider.OnValueChanged += range => VolumeChanged?.Invoke(channel, range.Value);

            _listenBoxes[channel] = box;
            _volumeSliders[channel] = slider;
            _channelRows.AddChild(new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                Children = { box, slider },
            });
        }

        _noChannels.Visible = channels.Count == 0;

        _channelRows.Measure(new Vector2(PanelWidth, float.PositiveInfinity));
        _listenScroll.MinHeight = Math.Min(MaxListenHeight, _channelRows.DesiredSize.Y);
    }

    private void RebuildTalk(List<ProtoId<RadioChannelPrototype>> channels,
        ProtoId<RadioChannelPrototype>? defaultChannel)
    {
        _shownChannels.Clear();
        _shownChannels.AddRange(channels);
        _shownDefault = defaultChannel;
        _talkBuilt = true;

        _talk.Clear();

        // Nothing to talk into (no headset, observers): say so instead of showing an empty selector.
        if (channels.Count == 0)
        {
            _talk.AddItem(Loc.GetString("voice-radio-panel-talk-none"), 0);
            _talk.Disabled = true;
            return;
        }

        _talk.Disabled = false;

        var defaultName = defaultChannel is { } def ? GetChannel(def.Id).Name : "-";
        _talk.AddItem(Loc.GetString("voice-radio-panel-talk-default", ("channel", defaultName)), 0);

        for (var i = 0; i < channels.Count; i++)
        {
            _talk.AddItem(GetChannel(channels[i].Id).Name, i + 1);
        }
    }

    private void OnTalkSelected(OptionButton.ItemSelectedEventArgs args)
    {
        _talk.TrySelectId(args.Id);
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
