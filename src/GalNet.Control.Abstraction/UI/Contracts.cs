using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using GalNet.Core.UI;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.Abstraction.UI;

/// <summary>Plugin-facing contract for a project-owned colour palette.</summary>
public interface IColorPalette : INotifyPropertyChanged
{
    IBrush this[string key] { get; }
}

public interface IWidgetInstanceProvider
{
    bool TryGetWidget(string id, out WidgetInstanceDefinition? instance);
}

public interface IScreenInstanceProvider
{
    bool TryGetScreen(string id, out ScreenInstanceDefinition? instance);
    bool TryGetDefaultScreen(string categoryKey, out ScreenInstanceDefinition? instance);
}

public interface IWidgetTemplateRegistry
{
    IEnumerable<IWidgetTemplate> Templates { get; }
    bool TryGet(string id, out IWidgetTemplate? template);
}

/// <summary>Builds a fresh widget presentation from a project instance.</summary>
public interface IWidgetFactory
{
    WidgetPresentation Build(string instanceId, WidgetBuildContext context, string? expectedCategory = null);
}

public interface IScreenTemplateRegistry
{
    IEnumerable<IScreenTemplate> Templates { get; }
    bool TryGet(string id, out IScreenTemplate? template);
}

public interface IWidgetTemplate
{
    string Id { get; }
    string Category { get; }
    IReadOnlyCollection<string> ColorKeys { get; }
    void Validate(WidgetInstanceDefinition instance, ICollection<UiValidationError> errors);
    WidgetPresentation Build(WidgetInstanceDefinition instance, WidgetBuildContext context);
}

public interface IScreenTemplate
{
    string Id { get; }
    string Category { get; }
    void Validate(ScreenInstanceDefinition instance, ICollection<UiValidationError> errors);
    ScreenPresentation Build(ScreenInstanceDefinition instance, ScreenBuildContext context);
}

public sealed record WidgetBuildContext(
    IServiceProvider Services,
    IColorPalette Palette,
    IWidgetInstanceProvider Widgets,
    IGameScreenNavigator Navigator);

public sealed record ScreenBuildContext(
    IServiceProvider Services,
    IColorPalette Palette,
    IWidgetInstanceProvider Widgets,
    IScreenInstanceProvider Screens,
    IGameScreenNavigator Navigator,
    object? Parameter = null,
    object? Session = null);

public sealed record WidgetPresentation(AvaloniaControl View, object ViewModel);
public sealed record ScreenPresentation(AvaloniaControl View, object ViewModel, string Category);

/// <summary>Bindable navigation state used by a game's ContentControl host.</summary>
public interface IGameScreenNavigator : INotifyPropertyChanged
{
    ScreenPresentation? Current { get; }
    bool CanGoBack { get; }
    Task NavigateAsync(string categoryKey, object? parameter = null, CancellationToken cancellationToken = default);
    Task GoBackAsync(CancellationToken cancellationToken = default);
}
