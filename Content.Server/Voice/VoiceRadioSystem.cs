using Content.Server.Power.Components;
using Content.Shared.Chat;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Voice;

/// <summary>
/// RADIO layer: which channels a speaker can transmit voice on, and which players hear a transmission.
/// Follows the same rules as text radio (<see cref="Content.Server.Radio.EntitySystems.RadioSystem"/>): encryption
/// keys, telecom servers for short-range channels, long-range channels, jammers (via the same attempt events).
/// Radio voice has nothing to do with acoustic proximity.
/// </summary>
public sealed partial class VoiceRadioSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;

    private readonly HashSet<Entity<RadioMicrophoneComponent>> _micLookup = new();

    private EntityQuery<HeadsetComponent> _headsetQuery;
    private EntityQuery<EncryptionKeyHolderComponent> _keyQuery;
    private EntityQuery<WearingHeadsetComponent> _wearingQuery;
    private EntityQuery<IntrinsicRadioTransmitterComponent> _intrinsicTxQuery;
    private EntityQuery<IntercomComponent> _intercomQuery;
    private EntityQuery<TelecomExemptComponent> _exemptQuery;
    private EntityQuery<ActorComponent> _actorQuery;
    private EntityQuery<RadioSpeakerComponent> _speakerQuery;

    public override void Initialize()
    {
        base.Initialize();

        _headsetQuery = GetEntityQuery<HeadsetComponent>();
        _keyQuery = GetEntityQuery<EncryptionKeyHolderComponent>();
        _wearingQuery = GetEntityQuery<WearingHeadsetComponent>();
        _intrinsicTxQuery = GetEntityQuery<IntrinsicRadioTransmitterComponent>();
        _intercomQuery = GetEntityQuery<IntercomComponent>();
        _exemptQuery = GetEntityQuery<TelecomExemptComponent>();
        _actorQuery = GetEntityQuery<ActorComponent>();
        _speakerQuery = GetEntityQuery<RadioSpeakerComponent>();
    }

    /// <summary>
    /// Collects the channels <paramref name="speaker"/> can transmit on and the default one.
    /// </summary>
    public void GetTransmitChannels(EntityUid speaker, List<ProtoId<RadioChannelPrototype>> channels,
        out ProtoId<RadioChannelPrototype>? defaultChannel)
    {
        channels.Clear();
        defaultChannel = null;

        if (TryGetHeadset(speaker, out var headset, out var keys))
        {
            foreach (var channel in keys.Channels)
            {
                if (!channels.Contains(channel))
                    channels.Add(channel);
            }

            if (keys.DefaultChannel != null && keys.Channels.Contains(keys.DefaultChannel))
                defaultChannel = keys.DefaultChannel;
        }

        if (_intrinsicTxQuery.TryComp(speaker, out var intrinsic))
        {
            foreach (var channel in intrinsic.Channels)
            {
                if (!channels.Contains(channel))
                    channels.Add(channel);
            }
        }

        channels.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

        if (defaultChannel == null && channels.Count > 0)
        {
            ProtoId<RadioChannelPrototype> common = SharedChatSystem.CommonChannel;
            defaultChannel = channels.Contains(common) ? common : channels[0];
        }
    }

    /// <summary>
    /// Collects the channels <paramref name="listener"/> can hear voice on: its own receivers (observers hear
    /// everything) and its headset's.
    /// </summary>
    public void GetReceiveChannels(EntityUid listener, List<ProtoId<RadioChannelPrototype>> channels)
    {
        channels.Clear();

        if (TryComp(listener, out ActiveRadioComponent? own))
            AddReceiverChannels(own, channels);

        if (_wearingQuery.TryComp(listener, out var wearing)
            && _headsetQuery.TryComp(wearing.Headset, out var headset)
            && headset.Enabled
            && headset.IsEquipped
            && TryComp(wearing.Headset, out ActiveRadioComponent? headsetRadio))
        {
            AddReceiverChannels(headsetRadio, channels);
        }

        channels.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
    }

    private void AddReceiverChannels(ActiveRadioComponent radio, List<ProtoId<RadioChannelPrototype>> channels)
    {
        if (radio.ReceiveAllChannels)
        {
            foreach (var channel in _proto.EnumeratePrototypes<RadioChannelPrototype>())
            {
                if (!channels.Contains(channel.ID))
                    channels.Add(channel.ID);
            }

            return;
        }

        foreach (var channel in radio.Channels)
        {
            if (!channels.Contains(channel))
                channels.Add(channel);
        }
    }

    /// <summary>
    /// Resolves the entity actually transmitting <paramref name="channel"/> for <paramref name="speaker"/>.
    /// </summary>
    public bool TryGetTransmitter(EntityUid speaker, ProtoId<RadioChannelPrototype> channel, out EntityUid radioSource)
    {
        if (TryGetHeadset(speaker, out var headset, out var keys) && keys.Channels.Contains(channel))
        {
            radioSource = headset;
            return true;
        }

        if (_intrinsicTxQuery.TryComp(speaker, out var intrinsic) && intrinsic.Channels.Contains(channel))
        {
            radioSource = speaker;
            return true;
        }

        radioSource = default;
        return false;
    }

    private bool TryGetHeadset(EntityUid speaker, out EntityUid headset, out EncryptionKeyHolderComponent keys)
    {
        headset = default;
        keys = default!;

        if (!_wearingQuery.TryComp(speaker, out var wearing)
            || !_headsetQuery.TryComp(wearing.Headset, out var headsetComp)
            || !headsetComp.Enabled
            || !headsetComp.IsEquipped
            || !_keyQuery.TryComp(wearing.Headset, out var keyComp))
        {
            return false;
        }

        headset = wearing.Headset;
        keys = keyComp;
        return true;
    }

    /// <summary>
    /// Finds every player session that hears a voice transmission on <paramref name="channelId"/> from
    /// <paramref name="radioSource"/> through their own receiver (headset, intrinsic radio), and every device that
    /// plays it out loud (intercoms and handheld radios with their speaker on).
    /// </summary>
    public void CollectListeners(EntityUid radioSource, ProtoId<RadioChannelPrototype> channelId,
        HashSet<ICommonSession> output, List<EntityUid>? loudspeakers = null)
    {
        output.Clear();
        loudspeakers?.Clear();

        if (!_proto.TryIndex(channelId, out var channel))
            return;

        var sendAttempt = new RadioSendAttemptEvent(channel, radioSource);
        RaiseLocalEvent(ref sendAttempt);
        RaiseLocalEvent(radioSource, ref sendAttempt);
        if (sendAttempt.Cancelled)
            return;

        var sourceMap = Transform(radioSource).MapID;
        var needServer = !channel.LongRange && !_exemptQuery.HasComp(radioSource);
        if (needServer && !HasActiveServer(sourceMap, channel.ID))
            return;

        var query = EntityQueryEnumerator<ActiveRadioComponent, TransformComponent>();
        while (query.MoveNext(out var receiver, out var radio, out var xform))
        {
            if (!radio.ReceiveAllChannels)
            {
                if (!radio.Channels.Contains(channel.ID))
                    continue;

                if (_intercomQuery.TryComp(receiver, out var intercom) && !intercom.SupportedChannels.Contains(channel.ID))
                    continue;
            }

            if (!channel.LongRange && xform.MapID != sourceMap && !radio.GlobalReceive)
                continue;

            // A device with its speaker on plays it for everyone around (never the one transmitting, no feedback).
            var loudspeaker = loudspeakers != null
                              && receiver != radioSource
                              && _speakerQuery.TryComp(receiver, out var speaker)
                              && speaker.Enabled;

            ICommonSession? session = null;
            if (!loudspeaker && !TryGetListener(receiver, xform, out session))
                continue;

            var receiveAttempt = new RadioReceiveAttemptEvent(channel, radioSource, receiver);
            RaiseLocalEvent(ref receiveAttempt);
            RaiseLocalEvent(receiver, ref receiveAttempt);
            if (receiveAttempt.Cancelled)
                continue;

            if (loudspeaker)
                loudspeakers!.Add(receiver);
            else
                output.Add(session!);
        }
    }

    /// <summary>
    /// Collects the switched-on, powered radio microphones (intercoms, handheld radios) within
    /// <paramref name="range"/> of <paramref name="coordinates"/>. Callers still check each one's
    /// <see cref="RadioMicrophoneComponent.ListenRange"/> and whether it can actually hear the speaker.
    /// </summary>
    public void CollectMicrophones(MapCoordinates coordinates, float range, List<Entity<RadioMicrophoneComponent>> output)
    {
        output.Clear();
        _micLookup.Clear();
        _lookup.GetEntitiesInRange(coordinates, range, _micLookup);

        foreach (var mic in _micLookup)
        {
            if (!mic.Comp.Enabled || mic.Comp.PowerRequired && !_power.IsPowered(mic.Owner))
                continue;

            output.Add(mic);
        }
    }

    /// <summary>
    /// Maps a radio receiver to the player that hears it: an intrinsic receiver's own player, or the wearer of an
    /// equipped headset.
    /// </summary>
    private bool TryGetListener(EntityUid receiver, TransformComponent xform, out ICommonSession session)
    {
        if (_actorQuery.TryComp(receiver, out var actor))
        {
            session = actor.PlayerSession;
            return true;
        }

        if (_headsetQuery.TryComp(receiver, out var headset) && headset.IsEquipped
            && _actorQuery.TryComp(xform.ParentUid, out var wearer)
            && _wearingQuery.TryComp(xform.ParentUid, out var wearing) && wearing.Headset == receiver)
        {
            session = wearer.PlayerSession;
            return true;
        }

        session = default!;
        return false;
    }

    private bool HasActiveServer(MapId mapId, string channelId)
    {
        var servers = EntityQueryEnumerator<TelecomServerComponent, EncryptionKeyHolderComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (servers.MoveNext(out _, out _, out var keys, out var power, out var xform))
        {
            if (xform.MapID == mapId && power.Powered && keys.Channels.Contains(channelId))
                return true;
        }

        return false;
    }
}
