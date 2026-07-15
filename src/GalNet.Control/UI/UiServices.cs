using System.ComponentModel;
using System.Runtime.CompilerServices;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.UI;

namespace GalNet.Control.UI;

/// <summary>Indexes DI-created templates and rejects duplicate plugin template ids.</summary>
public sealed class TemplateRegistry : IWidgetTemplateRegistry, IScreenTemplateRegistry
{
    private readonly Dictionary<string, IWidgetTemplate> _widgets;
    private readonly Dictionary<string, IScreenTemplate> _screens;

    public TemplateRegistry(IEnumerable<IWidgetTemplate> widgets, IEnumerable<IScreenTemplate> screens)
    {
        _widgets = widgets.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        _screens = screens.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        if (_widgets.Count != widgets.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() ||
            _screens.Count != screens.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new InvalidOperationException("Duplicate UI template id registered.");
    }

    public IEnumerable<IWidgetTemplate> Templates => _widgets.Values;
    IEnumerable<IScreenTemplate> IScreenTemplateRegistry.Templates => _screens.Values;
    public bool TryGet(string id, out IWidgetTemplate? template) => _widgets.TryGetValue(id, out template);
    public bool TryGet(string id, out IScreenTemplate? template) => _screens.TryGetValue(id, out template);
}

/// <summary>Single resolution path for widgets used by all screen templates.</summary>
public sealed class WidgetFactory(IWidgetTemplateRegistry templates) : IWidgetFactory
{
    public WidgetPresentation Build(string instanceId, WidgetBuildContext context, string? expectedCategory = null)
    {
        if (!context.Widgets.TryGetWidget(instanceId, out var instance) || instance is null)
            throw new InvalidOperationException($"Widget instance '{instanceId}' is not registered.");
        if (!templates.TryGet(instance.TemplateId, out var template) || template is null)
            throw new InvalidOperationException($"Widget template '{instance.TemplateId}' is not registered.");
        if (expectedCategory is not null && !string.Equals(expectedCategory, template.Category, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Widget '{instanceId}' must be category '{expectedCategory}', but template '{template.Id}' is '{template.Category}'.");
        return template.Build(instance, context);
    }
}

/// <summary>Builds a fresh presentation for each navigation and exposes it as bindable state.</summary>
public sealed class GameScreenNavigator : IGameScreenNavigator
{
    private readonly IScreenInstanceProvider _screens;
    private readonly IScreenTemplateRegistry _templates;
    private readonly Func<object?, ScreenBuildContext> _contextFactory;
    private readonly Stack<ScreenPresentation> _backStack = [];
    private ScreenPresentation? _current;

    public GameScreenNavigator(IScreenInstanceProvider screens, IScreenTemplateRegistry templates,
        Func<object?, ScreenBuildContext> contextFactory)
    { _screens = screens; _templates = templates; _contextFactory = contextFactory; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ScreenPresentation? Current { get => _current; private set { _current = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoBack)); } }
    public bool CanGoBack => _backStack.Count > 0;

    public Task NavigateAsync(string categoryKey, object? parameter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_screens.TryGetDefaultScreen(categoryKey, out var instance) || instance is null)
            throw new InvalidOperationException($"No screen instance is registered for '{categoryKey}'.");
        if (!_templates.TryGet(instance.TemplateId, out var template) || template is null)
            throw new InvalidOperationException($"Screen template '{instance.TemplateId}' is not registered.");
        if (_current is not null) _backStack.Push(_current);
        Current = template.Build(instance, _contextFactory(parameter));
        return Task.CompletedTask;
    }

    public Task GoBackAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_backStack.TryPop(out var previous)) Current = previous;
        return Task.CompletedTask;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
