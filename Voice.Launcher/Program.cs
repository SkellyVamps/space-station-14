using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Themes.Simple;
using System.Diagnostics;
using System.Net;

namespace Skelly.VoiceLauncher;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (UpdateChannel.TryStartInstalled()) return;
        AppBuilder.Configure<LauncherApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
public sealed class LauncherApp : Application
{
    public override void Initialize() => Styles.Add(new SimpleTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new LoginWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
public sealed class LoginWindow : Window
{
    // Never redirect a password-bearing request to a different endpoint.
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource _closed = new();
    private readonly TextBox _username = new() { Watermark = "SS14 username" };
    private readonly TextBox _password = new() { Watermark = "SS14 password", PasswordChar = '●' };
    private readonly TextBox _mfa = new() { Watermark = "Two-factor code, if enabled", PasswordChar = '●' };
    private readonly TextBox _server = new() { Watermark = "ss14://your-server:1212" };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _play = new() { Content = "Sign in and play", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _logout = new() { Content = "Sign out", IsEnabled = false };
    private readonly Button _update = new() { Content = "Check for signed updates" };
    private Ss14Authentication.Session? _session;
    private bool _busy;
    public LoginWindow()
    {
        Title = "Skelly SS14 Voice — Development"; Width = 480; Height = 540; CanResize = false;
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Sign in with your SS14 account", FontSize = 22 },
                new TextBlock { Text = "Credentials go directly to auth.spacestation14.com.\nYour session stays in memory only." },
                _username, _password, _mfa, _server, _play, _logout, _update, _status
            }
        };
        _update.IsEnabled = UpdateChannel.Enabled;
        if (!_update.IsEnabled) _status.Text = "Development build. Automatic updates are not configured.";
        _play.Click += async (_, _) => await Play();
        _logout.Click += async (_, _) => await Logout();
        _update.Click += async (_, _) => await Update();
        Closed += (_, _) => { _closed.Cancel(); _http.Dispose(); };
    }
    private void Busy(bool busy)
    {
        _busy = busy;
        _play.IsEnabled = !busy; _logout.IsEnabled = !busy && _session != null;
        _update.IsEnabled = !busy && UpdateChannel.Enabled;
        _username.IsEnabled = _password.IsEnabled = _mfa.IsEnabled = !busy && _session == null;
        _server.IsEnabled = !busy;
    }
    private async Task Play()
    {
        if (_busy) return;
        Busy(true);
        try
        {
            var address = ServerConnection.ParseAddress(_server.Text?.Trim() ?? "");
            _status.Text = "Checking server…";
            var target = await ServerConnection.GetTarget(_http, address, _closed.Token);
            var auth = new Ss14Authentication(_http);
            if (_session == null)
            {
                _status.Text = "Signing in to SS14…";
                _session = await auth.Login(_username.Text?.Trim() ?? "", _password.Text ?? "",
                    string.IsNullOrWhiteSpace(_mfa.Text) ? null : _mfa.Text.Trim(), _closed.Token);
            }
            else if (_session.ExpireTime <= DateTimeOffset.UtcNow.AddMinutes(5)) _session = await auth.Refresh(_session, _closed.Token);
            _password.Text = _mfa.Text = "";
            _status.Text = "Game running. Close the game before signing out.";
            var start = ServerConnection.CreateLaunch(target, _session);
            if (!File.Exists(start.FileName)) throw new FileNotFoundException("Extract the full bundle, including the Client folder.");
            using var game = Process.Start(start) ?? throw new IOException("Could not start game");
            start.Environment.Remove("ROBUST_AUTH_TOKEN");
            await game.WaitForExitAsync(_closed.Token);
            _status.Text = game.ExitCode == 0 ? "Game closed. Ready to reconnect." : "Game exited with an error. Check the client logs.";
        }
        catch (Ss14Authentication.LoginDenied denied)
        {
            _status.Text = denied.Code is "TfaRequired" or "TfaInvalid" or "3" or "4"
                ? "Enter your password and a current two-factor code." : "SS14 sign-in was refused. Check your credentials and account status.";
        }
        catch (HttpRequestException error)
        {
            if (error.StatusCode == HttpStatusCode.Unauthorized) _session = null;
            _status.Text = "Connection failed. Check the server address and connection; sign in again if the session expired.";
        }
        catch (Exception e) when (UpdateChannel.IsExpected(e))
        {
            _status.Text = e is FileNotFoundException ? e.Message : "Could not connect. Check the server address, bundle and connection.";
        }
        finally { _password.Text = _mfa.Text = ""; Busy(false); }
    }
    private async Task Logout()
    {
        if (_busy || _session == null) return;
        Busy(true);
        try { await new Ss14Authentication(_http).Logout(_session, _closed.Token); _session = null; _status.Text = "Signed out."; }
        catch (Exception e) when (UpdateChannel.IsExpected(e)) { _status.Text = "Could not revoke the session. Retry when connected."; }
        finally { Busy(false); }
    }
    private async Task Update()
    {
        if (_busy) return;
        Busy(true);
        try
        {
            _status.Text = "Checking and verifying update…";
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
            deadline.CancelAfter(TimeSpan.FromMinutes(10));
            await UpdateChannel.InstallLatest(deadline.Token);
            Close();
        }
        catch (Exception e) when (UpdateChannel.IsExpected(e)) { _status.Text = "No newer verified update was installed. Check your connection or contact the server owner."; }
        finally { Busy(false); }
    }
}
