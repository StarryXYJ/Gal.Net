using Avalonia.Controls.Templates;
using Dock.Model.Core;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Serilog;

namespace GalNet.Editor.Dock;

/// <summary>
/// Resolves ViewModel to View for Dock.Avalonia.
/// Dock passes IDockable objects to Match/Build, NOT the Context directly.
/// We extract Context from IDockable and resolve the corresponding View.
/// </summary>
public class DockViewLocator : IDataTemplate
{
    public Avalonia.Controls.Control? Build(object? data)
    {
        if (data is null) return null;

        Log.Information("[DockViewLocator] Build called with: {Type}", data.GetType().Name);

        // Extract Context from IDockable (Document/Tool)
        object? context = data;
        if (data is IDockable dockable && dockable.Context is not null)
        {
            context = dockable.Context;
            Log.Information("[DockViewLocator] Extracted Context: {Type}", context.GetType().Name);
        }

        Avalonia.Controls.Control? control = context switch
        {
            GamePreviewPanelViewModel => new GamePreviewPanelView(),
            _ => null
        };

        if (control is not null)
        {
            Log.Information("[DockViewLocator] Built {ViewType} for {VMType}",
                control.GetType().Name, context.GetType().Name);
        }
        else
        {
            Log.Warning("[DockViewLocator] No view registered for {VMType}", context.GetType().Name);
        }

        return control;
    }

    public bool Match(object? data)
    {
        if (data is null) return false;

        // Match on IDockable — Dock passes Document/Tool objects, not Context directly
        if (data is IDockable dockable)
        {
            bool matches = dockable.Context is GamePreviewPanelViewModel;
            Log.Information("[DockViewLocator] Match called: {DockType} -> Context={CtxType}, matches={M}",
                data.GetType().Name, dockable.Context?.GetType().Name ?? "null", matches);
            return matches;
        }

        return data is GamePreviewPanelViewModel;
    }
}
