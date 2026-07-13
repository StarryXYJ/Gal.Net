using System.Collections.Generic;
using System;
using System.ComponentModel;

namespace GalNet.Editor.Abstraction.Extensibility;

public enum DockPanelPlacement
{
    MainDocument,
    BottomDocument,
    InspectorDocument
}

public interface IInspectorControlViewModel : IDisposable, INotifyPropertyChanged
{
    /// <summary>Whether the inspector is applicable to the current state of its dock panel.</summary>
    bool IsAvailable { get; }
}

public interface IInspectorControlContribution
{
    IInspectorControlViewModel CreateViewModel(IServiceProvider services, object dockViewModel);
    object CreateView(IServiceProvider services, IInspectorControlViewModel viewModel);
}

public interface IDockPanelContribution
{
    string PanelId { get; }
    /// <summary>Localization key used for the dock tab title.</summary>
    string TitleKey { get; }
    DockPanelPlacement Placement { get; }
    bool IsDefaultPanel { get; }
    bool CanClose { get; }
    bool CanFloat { get; }
    IInspectorControlContribution? Inspector { get; }
    object CreateViewModel(IServiceProvider services, object? parameter = null);
    object CreateView(IServiceProvider services, object viewModel);
}

public interface IEditorExtensionRegistry
{
    IEnumerable<IDockPanelContribution> DockPanelContributions { get; }
    void RegisterDockPanel(IDockPanelContribution contribution);
    IDockPanelContribution? FindDockPanel(string panelId);
}
