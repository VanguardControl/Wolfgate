using Content.Client._WF.Stylesheets;
using Content.Shared._WF.Voice;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Voice;

/// <summary>Creator card with the voice's sliders, filter and a Test button.</summary>
public sealed partial class SpeechVoicePicker : PanelContainer
{
    private static readonly SpeechVoiceFilter[] Filters =
    {
        SpeechVoiceFilter.None,
        SpeechVoiceFilter.Dog,
        SpeechVoiceFilter.Cat,
        SpeechVoiceFilter.Bird,
        SpeechVoiceFilter.Lizard,
        SpeechVoiceFilter.Mouse,
        SpeechVoiceFilter.Slime,
        SpeechVoiceFilter.Plant,
        SpeechVoiceFilter.Robot,
        SpeechVoiceFilter.Alien,
        SpeechVoiceFilter.Insect,
        SpeechVoiceFilter.Ghost,
    };

    /// <summary>Any setting changed. The voice passed is a new copy.</summary>
    public event Action<SpeechVoice>? OnVoiceChanged;

    /// <summary>The Test button was pressed with this text.</summary>
    public event Action<string>? OnTestPressed;

    private SpeechVoice _voice = new();
    private readonly List<Action> _refreshers = new();

    private readonly OptionButton _filter;
    private readonly LineEdit _testText;

    public SpeechVoicePicker()
    {
        StyleClasses.Add(StyleWolfgate.StyleClassCreatorCard);
        HorizontalExpand = true;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
        };
        AddChild(box);

        AddSlider(box, "speech-voice-pitch", SpeechVoice.MinPitch, SpeechVoice.MaxPitch,
            v => v.Pitch, (v, x) => v.Pitch = x);
        AddSlider(box, "speech-voice-pitch-variance", 0f, SpeechVoice.MaxPitchVariance,
            v => v.PitchVariance, (v, x) => v.PitchVariance = x);
        // Speed is seconds per syllable, so the slider runs backwards to put faster on the right.
        AddSlider(box, "speech-voice-speed", SpeechVoice.MinSpeed, SpeechVoice.MaxSpeed,
            v => SpeechVoice.MinSpeed + SpeechVoice.MaxSpeed - v.Speed,
            (v, x) => v.Speed = SpeechVoice.MinSpeed + SpeechVoice.MaxSpeed - x);
        AddSlider(box, "speech-voice-intonation", 0f, SpeechVoice.MaxIntonation,
            v => v.Intonation, (v, x) => v.Intonation = x);
        AddSlider(box, "speech-voice-vibrato-depth", 0f, SpeechVoice.MaxVibratoDepth,
            v => v.VibratoDepth, (v, x) => v.VibratoDepth = x);
        AddSlider(box, "speech-voice-vibrato-rate", SpeechVoice.MinVibratoRate, SpeechVoice.MaxVibratoRate,
            v => v.VibratoRate, (v, x) => v.VibratoRate = x);
        AddSlider(box, "speech-voice-tremolo", 0f, SpeechVoice.MaxTremolo,
            v => v.Tremolo, (v, x) => v.Tremolo = x);

        var filterRow = NewRow("speech-voice-filter");
        _filter = new OptionButton { MinWidth = 220 };
        foreach (var filter in Filters)
        {
            _filter.AddItem(Loc.GetString($"speech-voice-filter-{filter.ToString().ToLowerInvariant()}"), (int) filter);
        }

        _filter.OnItemSelected += args =>
        {
            _filter.SelectId(args.Id);
            Change(v => v.Filter = (SpeechVoiceFilter) args.Id);
        };
        filterRow.AddChild(_filter);
        box.AddChild(filterRow);

        AddSlider(box, "speech-voice-filter-strength", 0f, 1f,
            v => v.FilterStrength, (v, x) => v.FilterStrength = x);

        var testRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        _testText = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("speech-voice-test-placeholder"),
        };
        var test = new Button { Text = Loc.GetString("speech-voice-test") };
        test.OnPressed += _ => OnTestPressed?.Invoke(string.IsNullOrWhiteSpace(_testText.Text)
            ? Loc.GetString("speech-voice-test-placeholder")
            : _testText.Text);
        testRow.AddChild(_testText);
        testRow.AddChild(test);
        box.AddChild(testRow);
    }

    /// <summary>Shows a profile's voice without raising <see cref="OnVoiceChanged"/>.</summary>
    public void SetVoice(SpeechVoice voice)
    {
        _voice = voice;

        _filter.SelectId((int) voice.Filter);

        foreach (var refresh in _refreshers)
        {
            refresh();
        }
    }

    private void Change(Action<SpeechVoice> change)
    {
        var voice = _voice.Clone();
        change(voice);
        _voice = voice;
        OnVoiceChanged?.Invoke(voice);
    }

    private static BoxContainer NewRow(LocId label)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };

        row.AddChild(new Label
        {
            Text = Loc.GetString(label),
            VAlign = Label.VAlignMode.Center,
            MinWidth = 160,
            StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
        });
        return row;
    }

    private void AddSlider(BoxContainer box,
        LocId label,
        float min,
        float max,
        Func<SpeechVoice, float> get,
        Action<SpeechVoice, float> set)
    {
        var row = NewRow(label);
        var slider = new Slider { MinValue = min, MaxValue = max, HorizontalExpand = true };
        var value = new Label { MinWidth = 50, Align = Label.AlignMode.Right };
        row.AddChild(slider);
        row.AddChild(value);
        box.AddChild(row);

        void ShowValue()
        {
            value.Text = Loc.GetString("ui-options-value-percent", ("value", (slider.Value - min) / (max - min)));
        }

        slider.OnValueChanged += _ =>
        {
            ShowValue();
            Change(v => set(v, slider.Value));
        };

        _refreshers.Add(() =>
        {
            slider.SetValueWithoutEvent(get(_voice));
            ShowValue();
        });
    }
}
