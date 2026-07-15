using System.Windows.Input;

namespace GalNet.Core.Widget;

public interface IWidgetCategory
{
    string Category { get; }
}

public interface IButtonWidget : IWidgetCategory
{
    string Text { get; set; }
    bool IsEnabled { get; set; }
    ICommand? Command { get; set; }
}

public interface IChoicePanel : IWidgetCategory
{
    IReadOnlyList<string> Choices { get; }
    bool IsEnabled { get; set; }
    ICommand SelectCommand { get; }
    void SetChoices(IEnumerable<string> choices);
    event Action<int>? ChoiceSelected;
}

public interface ISliderWidget : IWidgetCategory
{
    string? Label { get; set; }
    double Value { get; set; }
    double Minimum { get; set; }
    double Maximum { get; set; }
    double Step { get; set; }
    bool ShowValue { get; set; }
    event Action<double>? ValueChanged;
}

public interface IToggleWidget : IWidgetCategory
{
    string Label { get; set; }
    bool IsChecked { get; set; }
    event Action<bool>? CheckedChanged;
}

public interface ISaveSlot : IWidgetCategory
{
    int SlotIndex { get; set; }
    DateTime? Timestamp { get; set; }
    string Description { get; set; }
    bool IsCorrupt { get; set; }
    bool IsEnabled { get; set; }
    bool IsEmpty { get; }
    ICommand? Command { get; set; }
}

public interface IDialogueWidget : IWidgetCategory
{
    string? Speaker { get; set; }
    string Content { get; set; }
    string? AvatarId { get; set; }
    bool IsAvatarVisible { get; set; }
}

public sealed record DialogueLine(string Text, string? Speaker = null);

public interface INvlWidget : IWidgetCategory
{
    IReadOnlyList<DialogueLine> Lines { get; }
    int MaxLines { get; set; }
    void AppendText(string text, string? speaker = null);
    void Clear();
}
