namespace GalNet.Core.Widget;

/// <summary>
/// Base marker interface for all widget categories.
/// Each category defines the minimum behavior contract for its widget type.
/// </summary>
public interface IWidgetCategory
{
    /// <summary>Category identifier (matches WidgetCategory.Name).</summary>
    string Category { get; }
}

/// <summary>
/// Dialogue box widget — displays speaker + text with optional avatar.
/// </summary>
public interface IDialogueWidget : IWidgetCategory
{
    /// <summary>Set the speaker name (can be null/empty for narration).</summary>
    void SetSpeaker(string? speaker);

    /// <summary>Set dialogue text content.</summary>
    void SetContent(string text);

    /// <summary>Show/hide the avatar area.</summary>
    void SetAvatarVisible(bool visible);

    /// <summary>Set avatar image placeholder (future: asset-based).</summary>
    void SetAvatar(string? avatarId);
}

/// <summary>
/// NVL-style full-screen text box.
/// </summary>
public interface INvlWidget : IWidgetCategory
{
    /// <summary>Append a new line of text.</summary>
    void AppendText(string text, string? speaker = null);

    /// <summary>Clear all text.</summary>
    void Clear();

    /// <summary>Max visible lines.</summary>
    int MaxLines { get; set; }
}

/// <summary>
/// Choice panel widget — presents selectable options.
/// </summary>
public interface IChoicePanel : IWidgetCategory
{
    /// <summary>Set the choice options.</summary>
    void SetChoices(string[] options);

    /// <summary>Fired when a choice is selected (index).</summary>
    event Action<int>? ChoiceSelected;

    /// <summary>Enable/disable the panel.</summary>
    bool IsEnabled { get; set; }
}

/// <summary>
/// Standard button widget.
/// </summary>
public interface IButtonWidget : IWidgetCategory
{
    /// <summary>Button text.</summary>
    void SetText(string text);

    /// <summary>Click event.</summary>
    event Action? Clicked;
}

/// <summary>
/// Slider widget for numeric values.
/// </summary>
public interface ISliderWidget : IWidgetCategory
{
    /// <summary>Current value.</summary>
    double Value { get; set; }

    /// <summary>Minimum value.</summary>
    double Minimum { get; set; }

    /// <summary>Maximum value.</summary>
    double Maximum { get; set; }

    /// <summary>Fired when value changes.</summary>
    event Action<double>? ValueChanged;
}

/// <summary>
/// Toggle/checkbox widget.
/// </summary>
public interface IToggleWidget : IWidgetCategory
{
    /// <summary>Label text.</summary>
    void SetLabel(string text);

    /// <summary>Checked state.</summary>
    bool IsChecked { get; set; }

    /// <summary>Fired when checked state changes.</summary>
    event Action<bool>? CheckedChanged;
}

/// <summary>
/// Save slot widget — displays save data.
/// </summary>
public interface ISaveSlot : IWidgetCategory
{
    /// <summary>Set slot data.</summary>
    void SetSlotData(int slotIndex, DateTime? timestamp, string? description);

    /// <summary>Whether the slot is empty.</summary>
    bool IsEmpty { get; }

    /// <summary>Fired when slot is clicked (with slot index).</summary>
    event Action<int>? SlotSelected;
}
