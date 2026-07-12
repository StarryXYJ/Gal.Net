using System;
using System.Collections.Generic;

namespace GalNet.Editor.Abstraction.Extensibility;

public interface IEditorPanelViewModel
{
    string PanelId { get; }
    string Title { get; }
}

public interface IEditorSelectionContext
{
    object? PrimarySelection { get; }
    IReadOnlyList<object> Selections { get; }
    event EventHandler? SelectionChanged;
}

public interface IInspectorViewModel : IDisposable
{
    IEditorSelectionContext Selection { get; }
}

public interface IInspectorContribution
{
    string InspectorId { get; }
    int Priority { get; }
    bool CanInspect(IEditorSelectionContext selection);
    IInspectorViewModel CreateViewModel(IServiceProvider services, IEditorSelectionContext selection);
    object CreateView(IServiceProvider services, IInspectorViewModel viewModel);
}

public interface IDockPanelContribution
{
    string PanelId { get; }
    string Title { get; }
    object CreateViewModel(IServiceProvider services);
    object CreateView(IServiceProvider services, object viewModel);
}

public interface IEditorExtensionRegistry
{
    IEnumerable<IInspectorContribution> InspectorContributions { get; }
    IEnumerable<IDockPanelContribution> DockPanelContributions { get; }
    void RegisterInspector(IInspectorContribution contribution);
    void RegisterDockPanel(IDockPanelContribution contribution);
}
