using Avalonia.Controls.Templates;
using Dock.Model.Core;
using GalNet.Editor.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.Dock;

public class DockViewLocator : IDataTemplate
{
    public Avalonia.Controls.Control? Build(object? data)
    {
        var context = ExtractContext(data);
        if (context is null)
            return null;

        var factory = App.ServiceProvider?.GetRequiredService<IEditorViewFactory>();
        var control = factory?.CreateViewForViewModel(context);

        if (control is null)
        {
            Log.Warning("[DockViewLocator] No view registered for {VMType}", context.GetType().Name);
        }

        return control;
    }

    public bool Match(object? data)
    {
        var context = ExtractContext(data);
        if (context is null)
            return false;

        var factory = App.ServiceProvider?.GetRequiredService<IEditorViewFactory>();
        var matches = factory?.CanCreateViewForViewModel(context) == true;
        return matches;
    }

    private static object? ExtractContext(object? data)
    {
        if (data is null)
            return null;

        return data is IDockable { Context: not null } dockable
            ? dockable.Context
            : data;
    }
}
