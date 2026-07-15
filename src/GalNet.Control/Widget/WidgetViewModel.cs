using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Widget;

namespace GalNet.Control.Widget;

public abstract partial class WidgetViewModel : ObservableObject, IWidgetCategory
{
    public abstract string Category { get; }
}

public sealed partial class ButtonWidgetViewModel : WidgetViewModel, IButtonWidget
{
    public override string Category => "button";
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private ICommand? _command;
}

public sealed partial class ChoicePanelWidgetViewModel : WidgetViewModel, IChoicePanel
{
    public override string Category => "choice";
    public ObservableCollection<string> Options { get; } = [];
    public IReadOnlyList<string> Choices => Options;
    [ObservableProperty] private bool _isEnabled = true;
    public event Action<int>? ChoiceSelected;
    ICommand IChoicePanel.SelectCommand => SelectCommand;

    public void SetChoices(IEnumerable<string> choices)
    {
        Options.Clear();
        foreach (var choice in choices) Options.Add(choice);
    }

    [RelayCommand]
    private void Select(object? parameter)
    {
        if (!IsEnabled) return;
        var index = parameter switch
        {
            int value => value,
            string text => Options.IndexOf(text),
            _ => -1
        };
        if (index >= 0) ChoiceSelected?.Invoke(index);
    }
}

public sealed partial class SliderWidgetViewModel : WidgetViewModel, ISliderWidget
{
    public override string Category => "slider";
    [ObservableProperty] private string? _label;
    [ObservableProperty] private double _value;
    [ObservableProperty] private double _minimum;
    [ObservableProperty] private double _maximum = 100;
    [ObservableProperty] private double _step = 1;
    [ObservableProperty] private bool _showValue = true;
    public string DisplayValue => $"{Value:0}";
    public event Action<double>? ValueChanged;
    partial void OnValueChanged(double value) { OnPropertyChanged(nameof(DisplayValue)); ValueChanged?.Invoke(value); }
}

public sealed partial class ToggleWidgetViewModel : WidgetViewModel, IToggleWidget
{
    public override string Category => "toggle";
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private bool _isChecked;
    public event Action<bool>? CheckedChanged;
    partial void OnIsCheckedChanged(bool value) => CheckedChanged?.Invoke(value);
}

public sealed partial class SaveSlotWidgetViewModel : WidgetViewModel, ISaveSlot
{
    public override string Category => "save-slot";
    [ObservableProperty] private int _slotIndex;
    [ObservableProperty] private DateTime? _timestamp;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isCorrupt;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private ICommand? _command;
    public bool IsEmpty => Timestamp is null;
    public string Title => Timestamp is null ? $"Slot {SlotIndex + 1}" : Timestamp.Value.ToString("yyyy/MM/dd HH:mm");
    partial void OnTimestampChanged(DateTime? value) { OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(Title)); }
    partial void OnSlotIndexChanged(int value) => OnPropertyChanged(nameof(Title));
}

public enum RichTextSegmentKind { Text, LineBreak }

public sealed partial class RichTextSegmentViewModel : ObservableObject
{
    public RichTextSegmentKind Kind { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public string FullText { get; init; } = "";
    [ObservableProperty] private string _visibleText = "";
}

public sealed partial class DialogueWidgetViewModel : WidgetViewModel, IDialogueWidget
{
    public override string Category => "dialogue";
    [ObservableProperty] private string? _speaker;
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private string? _avatarId;
    [ObservableProperty] private bool _isAvatarVisible;
    public ObservableCollection<RichTextSegmentViewModel> Segments { get; } = [];
}

public sealed partial class NvlWidgetViewModel : WidgetViewModel, INvlWidget
{
    public override string Category => "nvl";
    public ObservableCollection<DialogueLine> MutableLines { get; } = [];
    public IReadOnlyList<DialogueLine> Lines => MutableLines;
    public string DisplayText => string.Join("\n", MutableLines.Select(x => x.Speaker is null ? x.Text : $"{x.Speaker}: {x.Text}"));
    [ObservableProperty] private int _maxLines = 20;
    public void AppendText(string text, string? speaker = null) { MutableLines.Add(new(text, speaker)); OnPropertyChanged(nameof(DisplayText)); }
    public void Clear() { MutableLines.Clear(); OnPropertyChanged(nameof(DisplayText)); }
}
