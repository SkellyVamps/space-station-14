using System.Linq;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

/// <summary>Live mic meter, local-only loopback test, and opaque stream mute/volume controls.</summary>
public sealed partial class VoiceControls : BoxContainer
{
    [Dependency] private IEntitySystemManager _systems = default!;
    private float _refreshAfter;
    private readonly Label _status = new();
    private readonly ProgressBar _meter = new() { MinValue = 0, MaxValue = 1 };
    private readonly Button _test = new();
    private readonly BoxContainer _list = new() { Orientation = LayoutOrientation.Vertical };
    private readonly Dictionary<Robust.Shared.Network.NetUserId, Label> _indicators = new();

    public VoiceControls()
    {
        IoCManager.InjectDependencies(this);
        Orientation = LayoutOrientation.Vertical;
        _test.Text = Loc.GetString("voice-test-start");
        _test.OnPressed += _ =>
        {
            if (_systems.TryGetEntitySystem<VoiceClientSystem>(out var voice))
                voice.SetMicrophoneTest(!voice.MicrophoneTest);
        };
        var refresh = new Button { Text = Loc.GetString("voice-player-refresh") };
        refresh.OnPressed += _ => RefreshPlayers();
        AddChild(_status);
        AddChild(_meter);
        AddChild(_test);
        AddChild(refresh);
        AddChild(_list);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        RefreshPlayers();
    }

    protected override void ExitedTree()
    {
        if (_systems.TryGetEntitySystem<VoiceClientSystem>(out var voice))
            voice.SetMicrophoneTest(false);
        base.ExitedTree();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (!_systems.TryGetEntitySystem<VoiceClientSystem>(out var voice))
            return;
        _refreshAfter -= args.DeltaSeconds;
        if (_refreshAfter <= 0)
        {
            _refreshAfter = 1;
            if (!_indicators.Keys.ToHashSet().SetEquals(voice.HeardStreams)) RefreshPlayers();
        }
        _meter.Value = voice.MicrophoneLevel;
        _test.Text = Loc.GetString(voice.MicrophoneTest ? "voice-test-stop" : "voice-test-start");
        _status.Text = voice.CaptureError ?? Loc.GetString(voice.Transmitting ? "voice-mic-active" : "voice-mic-closed");
        foreach (var (user, label) in _indicators)
            label.Text = voice.IsSpeaking(user) ? Loc.GetString("voice-speaking") : "";
    }

    private void RefreshPlayers()
    {
        _list.RemoveAllChildren();
        _indicators.Clear();
        if (!_systems.TryGetEntitySystem<VoiceClientSystem>(out var voice))
            return;
        foreach (var stream in voice.HeardStreams)
        {
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
            var indicator = new Label { MinWidth = 65 };
            var volume = new Slider { MinValue = 0, MaxValue = 2, Value = voice.GetPlayerVolume(stream),
                HorizontalExpand = true, MinWidth = 100 };
            volume.OnReleased += _ => voice.SetPlayerVolume(stream, volume.Value);
            var mute = new Button { Text = Loc.GetString("voice-player-mute") };
            mute.OnPressed += _ =>
            {
                volume.Value = volume.Value > 0 ? 0 : 1;
                voice.SetPlayerVolume(stream, volume.Value);
            };
            row.AddChild(new Label { Text = "Voice " + stream.UserId.ToString("N")[..6], MinWidth = 150 });
            row.AddChild(indicator);
            row.AddChild(volume);
            row.AddChild(mute);
            _list.AddChild(row);
            _indicators[stream] = indicator;
        }
    }
}
