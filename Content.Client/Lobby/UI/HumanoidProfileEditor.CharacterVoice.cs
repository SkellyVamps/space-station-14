using Content.Client.Voice;
using Content.Shared.CCVar;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Client.Lobby.UI;

/// <summary>
/// Voice chat section of the character editor: the character's procedural voice and a live preview.
/// </summary>
public sealed partial class HumanoidProfileEditor
{
    private readonly Dictionary<string, (Slider Slider, Label Value)> _voiceSliders = new();
    private Button? _voicePreviewButton;
    private bool _voicePreviewing;

    private void InitializeCharacterVoice()
    {
        var header = new Label
        {
            Text = Loc.GetString("humanoid-profile-editor-character-voice-label"),
            StyleClasses = { "LabelSubText" },
            Margin = new(0, 6, 0, 0),
        };
        CharacterVoiceContainer.AddChild(header);

        var maxPitch = _cfgManager.GetCVar(CCVars.VoiceProfileMaxPitch);
        var maxFormant = _cfgManager.GetCVar(CCVars.VoiceProfileMaxFormant);

        AddVoiceSlider("pitch", -maxPitch, maxPitch, (p, v) => p with { Pitch = v });
        AddVoiceSlider("formant", -maxFormant, maxFormant, (p, v) => p with { Formant = v });
        AddVoiceSlider("brightness", -1f, 1f, (p, v) => p with { Brightness = v });
        AddVoiceSlider("roughness", 0f, 1f, (p, v) => p with { Roughness = v });
        AddVoiceSlider("breathiness", 0f, 1f, (p, v) => p with { Breathiness = v });

        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };

        _voicePreviewButton = new Button
        {
            Text = Loc.GetString("humanoid-profile-editor-character-voice-preview"),
            ToolTip = Loc.GetString("humanoid-profile-editor-character-voice-preview-tooltip"),
            ToggleMode = true,
        };
        _voicePreviewButton.OnToggled += args =>
        {
            if (args.Pressed)
                StartCharacterVoicePreview();
            else
                StopCharacterVoicePreview();
        };
        buttons.AddChild(_voicePreviewButton);

        var randomize = new Button { Text = Loc.GetString("humanoid-profile-editor-character-voice-randomize") };
        randomize.OnPressed += _ =>
        {
            var random = IoCManager.Resolve<IRobustRandom>();
            SetCharacterVoice(new VoiceProfile(
                random.NextFloat(-maxPitch, maxPitch) * 0.7f,
                random.NextFloat(-maxFormant, maxFormant) * 0.6f,
                random.NextFloat(-0.6f, 0.6f),
                random.NextFloat(0f, 0.3f),
                random.NextFloat(0f, 0.3f)));
            UpdateCharacterVoiceControls();
        };
        buttons.AddChild(randomize);

        var reset = new Button { Text = Loc.GetString("humanoid-profile-editor-character-voice-reset") };
        reset.OnPressed += _ =>
        {
            SetCharacterVoice(VoiceProfile.Neutral);
            UpdateCharacterVoiceControls();
        };
        buttons.AddChild(reset);

        CharacterVoiceContainer.AddChild(buttons);
    }

    private void AddVoiceSlider(string key, float min, float max, Func<VoiceProfile, float, VoiceProfile> apply)
    {
        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true };
        row.AddChild(new Label
        {
            Text = Loc.GetString($"humanoid-profile-editor-character-voice-{key}"),
            MinWidth = 110,
        });

        var slider = new Slider { MinValue = min, MaxValue = max, HorizontalExpand = true, MinWidth = 120 };
        var value = new Label { MinWidth = 40, Align = Label.AlignMode.Right };
        slider.OnValueChanged += range =>
        {
            if (Profile == null)
                return;

            value.Text = range.Value.ToString("0.0");
            SetCharacterVoice(apply(Profile.CharacterVoice, range.Value));
        };

        row.AddChild(slider);
        row.AddChild(value);
        CharacterVoiceContainer.AddChild(row);
        _voiceSliders[key] = (slider, value);
    }

    private void SetCharacterVoice(VoiceProfile voice)
    {
        if (Profile == null)
            return;

        Profile = Profile.WithCharacterVoice(voice);
        SetDirty();
        UpdateCharacterVoicePreview();
    }

    private void UpdateCharacterVoiceControls()
    {
        if (Profile == null)
            return;

        var voice = Profile.CharacterVoice;
        SetSlider("pitch", voice.Pitch);
        SetSlider("formant", voice.Formant);
        SetSlider("brightness", voice.Brightness);
        SetSlider("roughness", voice.Roughness);
        SetSlider("breathiness", voice.Breathiness);
        UpdateCharacterVoicePreview();
    }

    private void SetSlider(string key, float value)
    {
        if (!_voiceSliders.TryGetValue(key, out var entry))
            return;

        entry.Slider.SetValueWithoutEvent(value);
        entry.Value.Text = value.ToString("0.0");
    }

    /// <summary>
    /// The voice as it would sound in game for this character: profile plus the species' innate colouring.
    /// </summary>
    private (VoiceProfile, List<ProtoId<VoiceFilterPrototype>>) GetPreviewVoice()
    {
        var profile = Profile?.CharacterVoice ?? VoiceProfile.Neutral;
        var filters = new List<ProtoId<VoiceFilterPrototype>>();

        if (Profile != null
            && _prototypeManager.TryIndex(Profile.Species, out var species)
            && _prototypeManager.TryIndex(species.Prototype, out var entity)
            && entity.TryGetComponent<VoiceModifierComponent>(out var modifier, _entManager.ComponentFactory))
        {
            profile = profile.Add(modifier.Profile);
            filters.AddRange(modifier.Filters);
        }

        return (profile, filters);
    }

    private void StartCharacterVoicePreview()
    {
        var (profile, filters) = GetPreviewVoice();
        _entManager.System<VoiceTransmitSystem>().StartTest(profile, filters);
        _voicePreviewing = true;
    }

    private void UpdateCharacterVoicePreview()
    {
        if (!_voicePreviewing)
            return;

        var (profile, filters) = GetPreviewVoice();
        _entManager.System<VoiceTransmitSystem>().SetTestProfile(profile, filters);
    }

    private void StopCharacterVoicePreview()
    {
        if (!_voicePreviewing)
            return;

        _voicePreviewing = false;
        if (_voicePreviewButton != null)
            _voicePreviewButton.Pressed = false;

        if (_entManager.TrySystem<VoiceTransmitSystem>(out var transmit))
            transmit.StopTest();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        StopCharacterVoicePreview();
    }
}
