using Content.Client.Voice;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.Radio.Ui;

[UsedImplicitly]
public sealed class IntercomBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private IntercomMenu? _menu;

    public IntercomBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {

    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<IntercomMenu>();

        if (EntMan.TryGetComponent(Owner, out IntercomComponent? intercom))
        {
            _menu.Update((Owner, intercom));
        }

        _menu.OnMicPressed += enabled =>
        {
            SendMessage(new ToggleIntercomMicMessage(enabled));
        };
        _menu.OnSpeakerPressed += enabled =>
        {
            SendMessage(new ToggleIntercomSpeakerMessage(enabled));
        };
        _menu.OnChannelSelected += channel =>
        {
            SendMessage(new SelectIntercomChannelMessage(channel));
        };
        _menu.OnTransmitHeld += held =>
        {
            var voice = EntMan.System<VoiceTransmitSystem>();
            if (held)
                voice.SetDeviceTransmit(EntMan.GetNetEntity(Owner));
            else
                voice.ReleaseDeviceTransmit(EntMan.GetNetEntity(Owner));
        };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Closing the window (or walking away) lets go of the button.
        if (disposing && EntMan.TrySystem<VoiceTransmitSystem>(out var voice))
            voice.ReleaseDeviceTransmit(EntMan.GetNetEntity(Owner));
    }

    public void Update(Entity<IntercomComponent> ent)
    {
        _menu?.Update(ent);
    }
}
