using Robust.Client.Player;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client.Voice;

/// <summary>Live mic meter, local-only loopback test, and account-based mute/volume controls.</summary>
public sealed partial class VoiceControls : BoxContainer
{
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IPlayerManager _players = default!;
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
        foreach (var player in _players.Sessions)
        {
            if (player == _players.LocalSession)
                continue;
            var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
            var indicator = new Label { MinWidth = 65 };
            var volume = new Slider { MinValue = 0, MaxValue = 2, Value = voice.GetPlayerVolume(player.UserId),
                HorizontalExpand = true, MinWidth = 100 };
            volume.OnReleased += _ => voice.SetPlayerVolume(player.UserId, volume.Value);
            var mute = new Button { Text = Loc.GetString("voice-player-mute") };
            mute.OnPressed += _ =>
            {
                volume.Value = volume.Value > 0 ? 0 : 1;
                voice.SetPlayerVolume(player.UserId, volume.Value);
            };
            row.AddChild(new Label { Text = player.Name, MinWidth = 150 });
            row.AddChild(indicator);
            row.AddChild(volume);
            row.AddChild(mute);
            _list.AddChild(row);
            _indicators[player.UserId] = indicator;
        }
    }
}
