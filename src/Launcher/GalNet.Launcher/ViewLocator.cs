using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls.Templates;
using GalNet.Launcher.ViewModels;
using AvaloniaControl = Avalonia.Controls.Control;
using AvaloniaTextBlock = Avalonia.Controls.TextBlock;

namespace GalNet.Launcher;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public AvaloniaControl? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = Type.GetType(name);

        if (type != null)
        {
            return (AvaloniaControl)Activator.CreateInstance(type)!;
        }

        return new AvaloniaTextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
