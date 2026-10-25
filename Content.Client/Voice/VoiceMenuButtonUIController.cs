using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared.CCVar;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.Client.Voice;

/// <summary>
/// The microphone button in the top menu bar: shows or hides the radio panel, like its keybind.
/// </summary>
public sealed partial class VoiceMenuButtonUIController : UIController
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;

    private MenuButton? VoiceButton => UIManager.GetActiveUIWidgetOrNull<GameTopMenuBar>()?.VoiceButton;

    public override void Initialize()
    {
        base.Initialize();
        _cfg.OnValueChanged(CCVars.VoiceShowRadioPanel, UpdatePressed);
    }

    public void LoadButton()
    {
        if (VoiceButton is not { } button)
            return;

        button.OnPressed += OnButtonPressed;
        UpdatePressed(_cfg.GetCVar(CCVars.VoiceShowRadioPanel));
    }

    public void UnloadButton()
    {
        if (VoiceButton is not { } button)
            return;

        button.OnPressed -= OnButtonPressed;
    }

    private void OnButtonPressed(BaseButton.ButtonEventArgs args)
    {
        _entMan.System<VoiceIndicatorSystem>().ToggleRadioPanel();
    }

    private void UpdatePressed(bool shown)
    {
        if (VoiceButton is { } button)
            button.Pressed = shown;
    }
}
