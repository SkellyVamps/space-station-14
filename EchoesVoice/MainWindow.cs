using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace EchoesVoice;

/// <summary>
/// The app's only window: the link to the game, the microphone, and whether the game is playing your voice.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly IBrush Gold = new SolidColorBrush(Color.Parse("#c9a24a"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#9a9aa6"));
    private static readonly IBrush Good = new SolidColorBrush(Color.Parse("#4ec36b"));
    private static readonly IBrush Bad = new SolidColorBrush(Color.Parse("#e05050"));
    private static readonly IBrush Card = new SolidColorBrush(Color.Parse("#17171d"));

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly MicCapture _mic = new();
    private VoiceConnection? _connection;
    private VoiceLink? _pending;

    private readonly Border _dot = new() { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Muted };
    private readonly TextBlock _status = new() { Text = "Not connected", FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _statusHint = new() { Foreground = Muted, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly TextBox _link = new() { Watermark = "Paste your voice link here" };
    private readonly Button _connect = new() { Content = "Connect" };
    private readonly Border _confirm = new() { IsVisible = false };
    private readonly TextBlock _confirmText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _device = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ProgressBar _level = new() { Minimum = 0, Maximum = 1, Height = 10, Foreground = Good };
    private readonly TextBlock _talking = new() { Text = "Not talking", Foreground = Muted, MinWidth = 120, TextAlignment = TextAlignment.Right };
    private readonly ToggleButton _mute = new() { Content = "Mute" };
    private readonly Slider _gain = new() { Minimum = 0, Maximum = 3, SmallChange = 0.05 };
    private readonly Slider _noise = new() { Minimum = 0, Maximum = 1, SmallChange = 0.05 };

    private float _shownLevel;

    public MainWindow()
    {
        Title = "Echoes Voice";
        Width = 440;
        Height = 620;
        MinWidth = 360;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.Parse("#0e0e12"));
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://EchoesVoice/Assets/icon.png")));

        _mic.TestTone = Program.TestTone;
        _mic.Gain = _settings.Gain;
        _mic.NoiseSuppression = _settings.NoiseSuppression;
        _mic.Frame += OnFrame;

        Content = BuildLayout();
        LoadDevices();

        _connect.Click += (_, _) => OnConnectClicked();
        _mute.IsCheckedChanged += (_, _) =>
        {
            _mic.Muted = _mute.IsChecked == true;
            _mute.Content = _mic.Muted ? "Unmute" : "Mute";
            _mute.Background = _mic.Muted ? Bad : null;
        };
        _gain.Value = _settings.Gain;
        _gain.ValueChanged += (_, e) => { _mic.Gain = (float) e.NewValue; _settings.Gain = _mic.Gain; };
        _noise.Value = _settings.NoiseSuppression;
        _noise.ValueChanged += (_, e) => { _mic.NoiseSuppression = (float) e.NewValue; _settings.NoiseSuppression = _mic.NoiseSuppression; };
        _device.SelectionChanged += (_, _) => OnDeviceChanged();

        Program.LinkReceived += link => Dispatcher.UIThread.Post(() => HandleIncomingLink(link));

        // Opened by a link, or reconnecting to where we were.
        var start = Program.StartLink ?? _settings.LastLink;
        if (start != null)
            HandleIncomingLink(start);

        Closing += (_, _) =>
        {
            _settings.Save();
            _mic.Dispose();
            _connection?.DisposeAsync().AsTask().Wait(2000);
        };
    }

    private Control BuildLayout()
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://EchoesVoice/Assets/icon.png"))), Width = 40, Height = 40 },
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "Echoes Voice", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Gold },
                        new TextBlock { Text = "Voice chat for Space Station 14, Echoes of the Void", Foreground = Muted },
                    },
                },
            },
        };

        var paste = new Button { Content = "Paste" };
        paste.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard && await clipboard.TryGetTextAsync() is { } text)
                _link.Text = text.Trim();
        };

        var linkRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        paste.Margin = new Thickness(6, 0, 0, 0);
        _connect.Margin = new Thickness(6, 0, 0, 0);
        linkRow.Children.Add(_link);
        Grid.SetColumn(paste, 1);
        linkRow.Children.Add(paste);
        Grid.SetColumn(_connect, 2);
        linkRow.Children.Add(_connect);

        var allow = new Button { Content = "Connect", Background = Gold, Foreground = Brushes.Black };
        allow.Click += (_, _) =>
        {
            if (_pending is { } link)
            {
                Trust(link);
                Connect(link);
            }
        };
        var deny = new Button { Content = "Cancel" };
        deny.Click += (_, _) =>
        {
            _pending = null;
            _confirm.IsVisible = false;
        };

        _confirm.Child = new StackPanel
        {
            Spacing = 8,
            Children = { _confirmText, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { allow, deny } } },
        };
        _confirm.Background = new SolidColorBrush(Color.Parse("#2a2210"));
        _confirm.BorderBrush = Gold;
        _confirm.BorderThickness = new Thickness(1);
        _confirm.Padding = new Thickness(12);

        var meterRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _talking.Margin = new Thickness(10, 0, 0, 0);
        meterRow.Children.Add(_level);
        Grid.SetColumn(_talking, 1);
        meterRow.Children.Add(_talking);

        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 14,
                Children =
                {
                    header,
                    MakeCard(
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _dot, _status } },
                        _statusHint),
                    _confirm,
                    MakeCard(
                        Label("Voice link"),
                        linkRow,
                        Hint("In the game: Options > Voice > Copy link for the app, then paste it here. Or use \"Open in the app\" on the voice page.")),
                    MakeCard(
                        Label("Microphone"),
                        _device,
                        meterRow,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _mute } },
                        Label("Volume"),
                        _gain,
                        Label("Background noise removal"),
                        _noise,
                        Hint("Talk with your push to talk keys in the game, as usual. The game decides when you're heard.")),
                    Hint("Your voice is only sent while connected, and only to the game server you linked. Nothing is recorded by this app."),
                },
            },
        };
    }

    private static Border MakeCard(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return new Border
        {
            Background = Card,
            BorderBrush = Gold,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(14),
            Child = panel,
        };
    }

    private static TextBlock Label(string text) => new() { Text = text, Foreground = Muted };

    private static TextBlock Hint(string text) => new() { Text = text, Foreground = Muted, TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    #region Linking

    private void OnConnectClicked()
    {
        if (_connection != null)
        {
            Disconnect("Not connected", "You're using the game's own microphone again.");
            _settings.LastLink = null;
            _settings.Save();
            return;
        }

        if (!VoiceLink.TryParse(_link.Text, out var link))
        {
            SetStatus(Bad, "That isn't a voice link", "Copy it from the game: Options > Voice > Copy link for the app.");
            return;
        }

        // Pasting it and pressing Connect is the player's own choice: trust that server from now on.
        Trust(link);
        Connect(link);
    }

    /// <summary>
    /// A link from outside (a clicked echoesvoice:// link, a second copy of the app, or the last session).
    /// </summary>
    private void HandleIncomingLink(string text)
    {
        Activate();
        if (string.IsNullOrWhiteSpace(text) || !VoiceLink.TryParse(text, out var link))
            return;

        _link.Text = text;

        // Any website could open an app link: never send the microphone to a new server without asking.
        if (!_settings.TrustedServers.Contains(link.Server.Authority))
        {
            _pending = link;
            _confirmText.Text = $"Send your microphone to {link.Server.Authority}? Only connect to the game server you're playing on.";
            _confirm.IsVisible = true;
            return;
        }

        Connect(link);
    }

    private void Trust(VoiceLink link)
    {
        if (!_settings.TrustedServers.Contains(link.Server.Authority))
            _settings.TrustedServers.Add(link.Server.Authority);
    }

    private void Connect(VoiceLink link)
    {
        _pending = null;
        _confirm.IsVisible = false;
        Disconnect(null, null);

        var error = _mic.Start(SelectedDevice());
        if (error != null)
        {
            SetStatus(Bad, "No microphone", error);
            return;
        }

        _settings.LastLink = $"{VoiceLink.Scheme}://connect?server={Uri.EscapeDataString(link.Server.ToString())}&code={link.Code}";
        _settings.Save();

        var connection = new VoiceConnection(link);
        connection.StatusChanged += (ok, text) => Dispatcher.UIThread.Post(() =>
        {
            if (_connection == connection)
                SetStatus(ok ? Good : null, text, ok ? "Keep this app open while you play. You can minimise it." : "");
        });
        connection.TalkingChanged += (talking, adminMuted) => Dispatcher.UIThread.Post(() =>
        {
            if (_connection != connection)
                return;

            _talking.Text = adminMuted ? "Muted by an admin" : talking ? "Talking" : "Not talking";
            _talking.Foreground = talking ? Brushes.White : adminMuted ? Bad : Muted;
            _talking.FontWeight = talking ? FontWeight.SemiBold : FontWeight.Normal;
        });
        connection.Ended += reason => Dispatcher.UIThread.Post(() =>
        {
            if (_connection != connection)
                return;

            Disconnect("Disconnected", reason);
            _settings.LastLink = null;
            _settings.Save();
        });

        _connection = connection;
        _connect.Content = "Disconnect";
        connection.Start();
    }

    private void Disconnect(string? status, string? hint)
    {
        var old = _connection;
        _connection = null;
        _mic.Stop();
        _connect.Content = "Connect";
        _talking.Text = "Not talking";
        _talking.Foreground = Muted;
        _level.Value = 0;
        if (old != null)
            _ = old.DisposeAsync();

        if (status != null)
            SetStatus(Bad, status, hint);
    }

    private void SetStatus(IBrush? color, string text, string? hint)
    {
        _dot.Background = color ?? Muted;
        _status.Text = text;
        _statusHint.Text = hint ?? string.Empty;
        _statusHint.IsVisible = !string.IsNullOrEmpty(hint);
    }

    #endregion

    #region Microphone

    private void LoadDevices()
    {
        var devices = MicCapture.ListDevices();
        var items = new List<string> { "Default microphone" };
        items.AddRange(devices);
        _device.ItemsSource = items;
        _device.SelectedIndex = _settings.Device != null && devices.IndexOf(_settings.Device) is var i and >= 0 ? i + 1 : 0;
    }

    private string? SelectedDevice()
    {
        return _device.SelectedIndex > 0 ? _device.SelectedItem as string : null;
    }

    private void OnDeviceChanged()
    {
        _settings.Device = SelectedDevice();
        _settings.Save();

        if (_connection != null)
        {
            var error = _mic.Start(SelectedDevice());
            if (error != null)
                SetStatus(Bad, "No microphone", error);
        }
    }

    private void OnFrame(byte[] frame, float level)
    {
        _connection?.SendFrame(frame);

        // Decibels, so normal speech fills most of the meter.
        var db = 20 * Math.Log10(Math.Max(level, 1e-5));
        var target = (float) Math.Clamp((db + 60) / 50, 0, 1);
        _shownLevel = target > _shownLevel ? target : _shownLevel * 0.85f + target * 0.15f;
        var shown = _shownLevel;
        Dispatcher.UIThread.Post(() => _level.Value = shown, DispatcherPriority.Background);
    }

    #endregion
}
