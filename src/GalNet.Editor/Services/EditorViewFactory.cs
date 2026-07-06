using System;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed class EditorViewFactory : IEditorViewFactory
{
    private readonly IServiceProvider _serviceProvider;

    public EditorViewFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Avalonia.Controls.Control CreateView(Type viewType, object dataContext)
    {
        var view = (Avalonia.Controls.Control)(_serviceProvider.GetService(viewType)
                   ?? Activator.CreateInstance(viewType)!);
        view.DataContext = dataContext;
        return view;
    }
}
