using Avalonia.Controls.Templates;
using Dock.Model.Core;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GalNet.Editor.Dock;

public class DockViewLocator : IDataTemplate
{
    public Avalonia.Controls.Control? Build(object? data)
    {
        if (data is IDockable dockable && dockable.Context is not null)
        {
            var services = App.ServiceProvider;
            var panel = services?.GetRequiredService<IEditorExtensionRegistry>()
                .FindDockPanel(dockable.Id?.Split(':')[0] ?? "");
            if (panel is not null)
                return panel.CreateView(services!, dockable.Context) as Avalonia.Controls.Control;
        }

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
        if (data is IDockable dockable)
        {
            var panel = App.ServiceProvider?.GetRequiredService<IEditorExtensionRegistry>()
                .FindDockPanel(dockable.Id?.Split(':')[0] ?? "");
            if (panel is not null)
                return true;
        }

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
