using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.UI;
using AvaloniaControl = Avalonia.Controls.Control;
using GalNet.Control.Widget;

namespace GalNet.Control.Widget.BuiltIn;

/// <summary>Built-in typed template factories. They are the only DI registrations for runtime widgets.</summary>
public sealed class ButtonWidgetTemplate : WidgetTemplate<DefaultButtonTemplate, DefaultButtonConfig>
{
    public ButtonWidgetTemplate() : base("builtin.button", "button") { }
    protected override DefaultButtonTemplate Create(DefaultButtonConfig config, WidgetBuildContext context) =>
        WidgetPresentationStyle.Apply(new DefaultButtonTemplate(config), config);
    protected override object CreateViewModel(DefaultButtonConfig config, DefaultButtonTemplate view, WidgetBuildContext context) => new ButtonWidgetViewModel();
}

public sealed class TitleButtonWidgetTemplate : WidgetTemplate<DefaultTitleButtonTemplate, DefaultTitleButtonConfig>
{
    public TitleButtonWidgetTemplate() : base("builtin.title-button", "button") { }
    protected override DefaultTitleButtonTemplate Create(DefaultTitleButtonConfig config, WidgetBuildContext context) =>
        WidgetPresentationStyle.Apply(new DefaultTitleButtonTemplate(config), config);
    protected override object CreateViewModel(DefaultTitleButtonConfig config, DefaultTitleButtonTemplate view, WidgetBuildContext context) => new ButtonWidgetViewModel();
}

public sealed class DialogueWidgetTemplate : WidgetTemplate<DefaultDialogueTemplate, DefaultDialogueConfig>
{
    public DialogueWidgetTemplate() : base("builtin.dialogue", "dialogue") { }
    protected override DefaultDialogueTemplate Create(DefaultDialogueConfig config, WidgetBuildContext context) =>
        WidgetPresentationStyle.Apply(new DefaultDialogueTemplate(config), config);
    protected override object CreateViewModel(DefaultDialogueConfig config, DefaultDialogueTemplate view, WidgetBuildContext context) => new DialogueWidgetViewModel();
}

public sealed class NvlWidgetTemplate : WidgetTemplate<DefaultNvlTemplate, DefaultNvlConfig>
{
    public NvlWidgetTemplate() : base("builtin.nvl", "nvl") { }
    protected override DefaultNvlTemplate Create(DefaultNvlConfig config, WidgetBuildContext context) =>
        WidgetPresentationStyle.Apply(new DefaultNvlTemplate(config), config);
    protected override object CreateViewModel(DefaultNvlConfig config, DefaultNvlTemplate view, WidgetBuildContext context) => new NvlWidgetViewModel { MaxLines = config.MaxLines };
}

public sealed class ChoiceWidgetTemplate : WidgetTemplate<DefaultChoiceTemplate, DefaultChoiceConfig>
{
    public ChoiceWidgetTemplate() : base("builtin.choice", "choice") { }
    protected override DefaultChoiceTemplate Create(DefaultChoiceConfig config, WidgetBuildContext context) =>
        WidgetPresentationStyle.Apply(new DefaultChoiceTemplate(config), config);
    protected override object CreateViewModel(DefaultChoiceConfig config, DefaultChoiceTemplate view, WidgetBuildContext context) => new ChoicePanelWidgetViewModel();
}

public sealed class SliderWidgetTemplate : WidgetTemplate<DefaultSliderTemplate, DefaultSliderConfig>
{
    public SliderWidgetTemplate() : base("builtin.slider", "slider") { }
    protected override DefaultSliderTemplate Create(DefaultSliderConfig config, WidgetBuildContext context) =>
        WidgetPresentationStyle.Apply(new DefaultSliderTemplate(config), config);
    protected override object CreateViewModel(DefaultSliderConfig config, DefaultSliderTemplate view, WidgetBuildContext context) => new SliderWidgetViewModel { Minimum = config.Minimum, Maximum = config.Maximum, Value = config.Value, Step = config.Step, Label = config.Label, ShowValue = config.ShowValue };
}

public sealed class ToggleWidgetTemplate : WidgetTemplate<DefaultToggleTemplate, DefaultToggleConfig>
{
    public ToggleWidgetTemplate() : base("builtin.toggle", "toggle") { }
    protected override DefaultToggleTemplate Create(DefaultToggleConfig config, WidgetBuildContext context) =>
        WidgetPresentationStyle.Apply(new DefaultToggleTemplate(config), config);
    protected override object CreateViewModel(DefaultToggleConfig config, DefaultToggleTemplate view, WidgetBuildContext context) => new ToggleWidgetViewModel { Label = config.Label, IsChecked = config.IsChecked };
}

public sealed class CommandToggleWidgetTemplate : WidgetTemplate<CommandToggleTemplate, DefaultToggleConfig>
{
    public CommandToggleWidgetTemplate() : base("builtin.command-toggle", "toggle") { }
    protected override CommandToggleTemplate Create(DefaultToggleConfig config, WidgetBuildContext context) =>
        WidgetPresentationStyle.Apply(new CommandToggleTemplate(config), config);
    protected override object CreateViewModel(DefaultToggleConfig config, CommandToggleTemplate view, WidgetBuildContext context) =>
        new ToggleWidgetViewModel { Label = config.Label, IsChecked = config.IsChecked };
}

public sealed class SaveSlotWidgetTemplate : WidgetTemplate<DefaultSlotTemplate, DefaultSlotConfig>
{
    public SaveSlotWidgetTemplate() : base("builtin.save-slot", "save-slot") { }
    protected override DefaultSlotTemplate Create(DefaultSlotConfig config, WidgetBuildContext context) =>
        WidgetPresentationStyle.Apply(new DefaultSlotTemplate(config), config);
    protected override object CreateViewModel(DefaultSlotConfig config, DefaultSlotTemplate view, WidgetBuildContext context)
        => new SaveSlotWidgetViewModel { SlotIndex = config.SlotIndex };
}

public static class WidgetPresentationStyle
{
    /// <summary>Applies non-colour geometry. Palette bindings remain live through PaletteScope.</summary>
    public static T Apply<T>(T control, PresentationConfig config) where T : AvaloniaControl
    {
        if (config.Width is { } width) control.Width = width;
        if (config.Height is { } height) control.Height = height;
        if (config.MinWidth is { } minWidth) control.MinWidth = minWidth;
        if (config.MinHeight is { } minHeight) control.MinHeight = minHeight;
        if (control is TemplatedControl templated)
        {
            if (config.FontSize is { } fontSize) templated.FontSize = fontSize;
            if (config.CornerRadius is { } cornerRadius) templated.CornerRadius = new CornerRadius(cornerRadius);
            if (config.BorderThickness is { } borderThickness) templated.BorderThickness = new Thickness(borderThickness);
            Bind(templated, TemplatedControl.ForegroundProperty, config.Foreground);
            Bind(templated, TemplatedControl.BackgroundProperty, config.Background);
            Bind(templated, TemplatedControl.BorderBrushProperty, config.BorderBrush);
        }
        return control;
    }

    private static void Bind<T>(StyledElement target, AvaloniaProperty<T> property, string? key)
    {
        if (!string.IsNullOrWhiteSpace(key)) target.Bind(property, PaletteBinding.Create(target, key));
    }
}
