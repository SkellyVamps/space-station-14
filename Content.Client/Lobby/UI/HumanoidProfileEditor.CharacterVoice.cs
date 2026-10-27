using Content.Client.Voice;
using Content.Shared.CCVar;
using Content.Shared.Preferences;
using Content.Shared.Voice;
using Content.Shared.Voice.Components;
using Robust.Client.UserInterface;
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
    private readonly List<VoicePresetPrototype> _voicePresets = new();
    private readonly List<HumanoidCharacterProfile> _voiceCopySources = new();
    private Button? _voicePreviewButton;
    private OptionButton? _voicePresetButton;
    private OptionButton? _voiceCopyButton;
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

        // Presets and copying from another character.
        _voicePresets.AddRange(_prototypeManager.EnumeratePrototypes<VoicePresetPrototype>());
        _voicePresets.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.ID, b.ID));

        _voicePresetButton = new OptionButton { HorizontalExpand = true };
        _voicePresetButton.AddItem(Loc.GetString("humanoid-profile-editor-character-voice-preset-custom"), 0);
        for (var i = 0; i < _voicePresets.Count; i++)
        {
            _voicePresetButton.AddItem(Loc.GetString(_voicePresets[i].Name), i + 1);
        }

        _voicePresetButton.OnItemSelected += args =>
        {
            _voicePresetButton.SelectId(args.Id);
            if (args.Id > 0 && args.Id <= _voicePresets.Count)
                ApplyCharacterVoice(_voicePresets[args.Id - 1].Profile);
        };
        CharacterVoiceContainer.AddChild(VoiceRow("preset", _voicePresetButton));

        _voiceCopyButton = new OptionButton { HorizontalExpand = true };
        _voiceCopyButton.OnItemSelected += args =>
        {
            _voiceCopyButton.SelectId(0);
            if (args.Id > 0 && args.Id <= _voiceCopySources.Count)
                ApplyCharacterVoice(_voiceCopySources[args.Id - 1].CharacterVoice);
        };
        CharacterVoiceContainer.AddChild(VoiceRow("copy-from", _voiceCopyButton));

        AddVoiceSlider("pitch", -maxPitch, maxPitch, (p, v) => p with { Pitch = v });
        AddVoiceSlider("formant", -maxFormant, maxFormant, (p, v) => p with { Formant = v });
        AddVoiceSlider("brightness", -1f, 1f, (p, v) => p with { Brightness = v });
        AddVoiceSlider("roughness", 0f, 1f, (p, v) => p with { Roughness = v });
        AddVoiceSlider("breathiness", 0f, 1f, (p, v) => p with { Breathiness = v });
        AddVoiceSlider("nasality", 0f, 1f, (p, v) => p with { Nasality = v });
        AddVoiceSlider("tremble", 0f, 1f, (p, v) => p with { Tremble = v });

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
            // Mostly subtle: a few characters get a nasal or shaky voice.
            ApplyCharacterVoice(new VoiceProfile(
                random.NextFloat(-maxPitch, maxPitch) * 0.7f,
                random.NextFloat(-maxFormant, maxFormant) * 0.6f,
                random.NextFloat(-0.6f, 0.6f),
                random.NextFloat(0f, 0.3f),
                random.NextFloat(0f, 0.3f),
                random.Prob(0.2f) ? random.NextFloat(0.2f, 0.6f) : 0f,
                random.Prob(0.1f) ? random.NextFloat(0.2f, 0.5f) : 0f));
        };
        buttons.AddChild(randomize);

        var reset = new Button { Text = Loc.GetString("humanoid-profile-editor-character-voice-reset") };
        reset.OnPressed += _ => ApplyCharacterVoice(VoiceProfile.Neutral);
        buttons.AddChild(reset);

        CharacterVoiceContainer.AddChild(buttons);

        // Share a voice as text.
        var codeButtons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        var copyCode = new Button
        {
            Text = Loc.GetString("humanoid-profile-editor-character-voice-copy-code"),
            ToolTip = Loc.GetString("humanoid-profile-editor-character-voice-copy-code-tooltip"),
        };
        copyCode.OnPressed += _ =>
        {
            if (Profile != null)
                IoCManager.Resolve<IClipboardManager>().SetText(Profile.CharacterVoice.ToCode());
        };
        codeButtons.AddChild(copyCode);

        var pasteCode = new Button
        {
            Text = Loc.GetString("humanoid-profile-editor-character-voice-paste-code"),
            ToolTip = Loc.GetString("humanoid-profile-editor-character-voice-paste-code-tooltip"),
        };
        pasteCode.OnPressed += async _ =>
        {
            var text = await IoCManager.Resolve<IClipboardManager>().GetText();
            if (VoiceProfile.TryParseCode(text, out var pasted))
                ApplyCharacterVoice(pasted.Clamped(maxPitch, maxFormant));
        };
        codeButtons.AddChild(pasteCode);

        CharacterVoiceContainer.AddChild(codeButtons);
    }

    private static BoxContainer VoiceRow(string key, Control control)
    {
        return new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            Children =
            {
                new Label
                {
                    Text = Loc.GetString($"humanoid-profile-editor-character-voice-{key}"),
                    MinWidth = 110,
                },
                control,
            },
        };
    }

    /// <summary>
    /// Sets the whole voice at once (preset, copy, paste, randomize, reset) and updates the controls.
    /// </summary>
    private void ApplyCharacterVoice(VoiceProfile voice)
    {
        SetCharacterVoice(voice);
        UpdateCharacterVoiceControls();
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
        UpdateVoicePresetSelection();
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
        SetSlider("nasality", voice.Nasality);
        SetSlider("tremble", voice.Tremble);
        UpdateVoicePresetSelection();
        UpdateVoiceCopySources();
        UpdateCharacterVoicePreview();
    }

    /// <summary>
    /// Shows the matching preset, or "Custom".
    /// </summary>
    private void UpdateVoicePresetSelection()
    {
        if (_voicePresetButton == null || Profile == null)
            return;

        var id = 0;
        for (var i = 0; i < _voicePresets.Count; i++)
        {
            if (_voicePresets[i].Profile == Profile.CharacterVoice)
            {
                id = i + 1;
                break;
            }
        }

        _voicePresetButton.SelectId(id);
    }

    /// <summary>
    /// The player's other characters, to copy a voice from.
    /// </summary>
    private void UpdateVoiceCopySources()
    {
        if (_voiceCopyButton == null)
            return;

        _voiceCopySources.Clear();
        _voiceCopyButton.Clear();
        _voiceCopyButton.AddItem(Loc.GetString("humanoid-profile-editor-character-voice-copy-from-choose"), 0);

        if (_preferencesManager.Preferences is { } prefs)
        {
            foreach (var (slot, character) in prefs.Characters)
            {
                if (slot == CharacterSlot || character is not { } humanoid)
                    continue;

                _voiceCopySources.Add(humanoid);
                _voiceCopyButton.AddItem(humanoid.Name, _voiceCopySources.Count);
            }
        }

        _voiceCopyButton.Disabled = _voiceCopySources.Count == 0;
        _voiceCopyButton.SelectId(0);
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
