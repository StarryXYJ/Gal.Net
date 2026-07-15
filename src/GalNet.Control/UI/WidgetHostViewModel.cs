using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Control.Abstraction.UI;

namespace GalNet.Control.UI;

using AvaloniaControl = Avalonia.Controls.Control;

/// <summary>The single adapter between a persisted widget instance and a Screen ContentControl.</summary>
public sealed partial class WidgetHostViewModel : ObservableObject
{
    private readonly IWidgetFactory _factory;
    private readonly WidgetBuildContext _context;
    private readonly string _instanceId;
    private readonly string _category;

    [ObservableProperty] private AvaloniaControl? _view;
    [ObservableProperty] private object? _widget;

    public WidgetHostViewModel(IWidgetFactory factory, WidgetBuildContext context, string instanceId, string category)
    {
        _factory = factory; _context = context; _instanceId = instanceId; _category = category;
        Rebuild();
    }

    public void Rebuild()
    {
        var presentation = _factory.Build(_instanceId, _context, _category);
        Widget = presentation.ViewModel;
        View = presentation.View;
    }

    public TCategory RequireWidget<TCategory>() where TCategory : class =>
        Widget as TCategory ?? throw new InvalidOperationException($"Widget instance '{_instanceId}' must provide {typeof(TCategory).Name}.");
}
