using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Verbs;
using Content.Shared.Voice;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Voice;

/// <summary>
/// Voice moderation: admin voice mutes, and each player's personal mute/volume choices for other players.
/// </summary>
/// <remarks>
/// Personal choices are stored by account on the server, never sent to the client as account IDs, and enforced by
/// not sending muted players' voice at all. Nothing here records or stores voice content.
/// </remarks>
public sealed partial class VoiceModerationSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IAdminManager _admin = default!;
    [Dependency] private IPlayerManager _players = default!;

    /// <summary>
    /// Upper bound on how many personal volume entries a single player can have, to bound memory.
    /// </summary>
    private const int MaxListenerEntries = 512;

    public static readonly VerbCategory VoiceCategory = new("verb-categories-voice", null);

    private readonly Dictionary<NetUserId, AdminVoiceMute> _adminMutes = new();
    private readonly Dictionary<NetUserId, Dictionary<NetUserId, float>> _listenerVolumes = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<VoiceSetPlayerVolumeEvent>(OnSetPlayerVolume);
        SubscribeLocalEvent<ActorComponent, GetVerbsEvent<Verb>>(OnGetVerbs);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        // Tell reconnecting players about an existing mute.
        if (e.NewStatus == SessionStatus.InGame && TryGetAdminMute(e.Session.UserId, out var mute))
            RaiseNetworkEvent(new VoiceAdminMuteStateEvent(true, mute.Expires), e.Session);
    }

    #region Admin mutes

    public bool IsAdminMuted(NetUserId user)
    {
        return TryGetAdminMute(user, out _);
    }

    public bool TryGetAdminMute(NetUserId user, out AdminVoiceMute mute)
    {
        if (!_adminMutes.TryGetValue(user, out mute))
            return false;

        if (mute.Expires is { } expires && _timing.RealTime >= expires)
        {
            _adminMutes.Remove(user);
            if (_players.TryGetSessionById(user, out var session))
                RaiseNetworkEvent(new VoiceAdminMuteStateEvent(false, null), session);

            return false;
        }

        return true;
    }

    public IEnumerable<(NetUserId User, AdminVoiceMute Mute)> GetAdminMutes()
    {
        foreach (var (user, _) in new List<KeyValuePair<NetUserId, AdminVoiceMute>>(_adminMutes))
        {
            if (TryGetAdminMute(user, out var mute))
                yield return (user, mute);
        }
    }

    /// <param name="duration">Null for until the server restarts or an admin unmutes.</param>
    public void AdminMute(NetUserId user, string userName, TimeSpan? duration, string reason, ICommonSession? admin)
    {
        var expires = duration is { } d ? _timing.RealTime + d : (TimeSpan?) null;
        _adminMutes[user] = new AdminVoiceMute(expires, reason, admin?.Name ?? "server");

        var durationText = duration is { } dt ? $"{dt.TotalMinutes:0.#} minutes" : "indefinitely";
        _adminLog.Add(LogType.Voice, LogImpact.Medium,
            $"{admin?.Name ?? "Server"} voice muted {userName} ({user}) {durationText}. Reason: {reason}");

        if (_players.TryGetSessionById(user, out var session))
            RaiseNetworkEvent(new VoiceAdminMuteStateEvent(true, expires), session);
    }

    public bool AdminUnmute(NetUserId user, string userName, ICommonSession? admin)
    {
        if (!_adminMutes.Remove(user))
            return false;

        _adminLog.Add(LogType.Voice, LogImpact.Medium, $"{admin?.Name ?? "Server"} voice unmuted {userName} ({user})");

        if (_players.TryGetSessionById(user, out var session))
            RaiseNetworkEvent(new VoiceAdminMuteStateEvent(false, null), session);

        return true;
    }

    #endregion

    #region Personal volume

    /// <summary>
    /// Volume multiplier <paramref name="listener"/> chose for <paramref name="speaker"/>; 0 = muted, 1 = default.
    /// </summary>
    public float GetListenerVolume(NetUserId listener, NetUserId speaker)
    {
        if (!_listenerVolumes.TryGetValue(listener, out var volumes))
            return 1f;

        return volumes.GetValueOrDefault(speaker, 1f);
    }

    public void SetListenerVolume(NetUserId listener, NetUserId speaker, float volume)
    {
        volume = Math.Clamp(volume, 0f, 2f);

        if (!_listenerVolumes.TryGetValue(listener, out var volumes))
        {
            volumes = new Dictionary<NetUserId, float>();
            _listenerVolumes[listener] = volumes;
        }

        if (MathF.Abs(volume - 1f) < 0.001f)
        {
            volumes.Remove(speaker);
            return;
        }

        if (volumes.Count >= MaxListenerEntries && !volumes.ContainsKey(speaker))
            return;

        volumes[speaker] = volume;
    }

    private void OnSetPlayerVolume(VoiceSetPlayerVolumeEvent ev, EntitySessionEventArgs args)
    {
        if (!float.IsFinite(ev.Volume)
            || !TryGetEntity(ev.Target, out var target)
            || !TryComp(target, out ActorComponent? actor)
            || actor.PlayerSession == args.SenderSession)
        {
            return;
        }

        SetListenerVolume(args.SenderSession.UserId, actor.PlayerSession.UserId, ev.Volume);
        RaiseNetworkEvent(new VoicePlayerVolumeChangedEvent(ev.Target, Math.Clamp(ev.Volume, 0f, 2f)), args.SenderSession);
    }

    #endregion

    #region Verbs

    private void OnGetVerbs(Entity<ActorComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!TryComp(args.User, out ActorComponent? userActor))
            return;

        var user = userActor.PlayerSession;
        var target = ent.Comp.PlayerSession;
        if (user == target)
            return;

        var volume = GetListenerVolume(user.UserId, target.UserId);
        var targetUser = target.UserId;
        var targetNet = GetNetEntity(ent);

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(volume <= 0 ? "voice-verb-unmute" : "voice-verb-mute"),
            Category = VoiceCategory,
            Priority = 10,
            Act = () =>
            {
                SetListenerVolume(user.UserId, targetUser, volume <= 0 ? 1f : 0f);
                RaiseNetworkEvent(new VoicePlayerVolumeChangedEvent(targetNet, GetListenerVolume(user.UserId, targetUser)), user);
            },
        });

        foreach (var option in new[] { 0.5f, 1f, 1.5f, 2f })
        {
            var value = option;
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("voice-verb-volume", ("percent", (int) (value * 100))),
                Category = VoiceCategory,
                Disabled = MathF.Abs(volume - value) < 0.01f,
                Priority = (int) (-value * 10),
                Act = () =>
                {
                    SetListenerVolume(user.UserId, targetUser, value);
                    RaiseNetworkEvent(new VoicePlayerVolumeChangedEvent(targetNet, value), user);
                },
            });
        }

        if (_admin.HasAdminFlag(user, AdminFlags.Moderator))
        {
            var muted = IsAdminMuted(targetUser);
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString(muted ? "voice-verb-admin-unmute" : "voice-verb-admin-mute"),
                Category = VerbCategory.Admin,
                Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/gavel.svg.192dpi.png")),
                Impact = LogImpact.Medium,
                Act = () =>
                {
                    if (muted)
                        AdminUnmute(targetUser, target.Name, user);
                    else
                        AdminMute(targetUser, target.Name, null, "Admin verb", user);
                },
            });
        }
    }

    #endregion
}

public readonly record struct AdminVoiceMute(TimeSpan? Expires, string Reason, string Admin);
