using Content.Shared.Radio;
using Content.Shared.Radio.Components;

namespace Content.Server.Radio.EntitySystems;

public sealed partial class RadioSystem
{
    /// <summary>Voice uses the same telecom, map, channel, and cancellation rules as text radio.</summary>
    public bool CanRelayVoice(EntityUid source, EntityUid receiver, RadioChannelPrototype channel)
    {
        if (!TryComp<ActiveRadioComponent>(receiver, out var radio) ||
            (!radio.ReceiveAllChannels && !radio.Channels.Contains(channel.ID)))
            return false;
        var sourceMap = Transform(source).MapID;
        if (!channel.LongRange && Transform(receiver).MapID != sourceMap && !radio.GlobalReceive)
            return false;
        if (!channel.LongRange && !_exemptQuery.HasComp(source) && !HasActiveServer(sourceMap, channel.ID))
            return false;
        var sending = new RadioSendAttemptEvent(channel, source);
        RaiseLocalEvent(ref sending);
        RaiseLocalEvent(source, ref sending);
        if (sending.Cancelled)
            return false;
        var receiving = new RadioReceiveAttemptEvent(channel, source, receiver);
        RaiseLocalEvent(ref receiving);
        RaiseLocalEvent(receiver, ref receiving);
        return !receiving.Cancelled;
    }
}
