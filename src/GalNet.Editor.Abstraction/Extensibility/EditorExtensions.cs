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

/// <summary>Optional contract for inspector controls that must freeze their own selection state.</summary>
public interface IInspectorLockAware
{
    void SetLocked(bool isLocked);
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
    /// <summary>True when one project may have only one instance of this panel.</summary>
    bool IsGlobal { get; }
    /// <summary>Whether this panel can be created directly from the View menu.</summary>
    bool ShowInViewMenu { get; }
    bool IsDefaultPanel { get; }
    bool CanClose { get; }
    bool CanFloat { get; }
    IInspectorControlContribution? Inspector { get; }
    object CreateViewModel(IServiceProvider services, object? parameter = null);
    object CreateView(IServiceProvider services, object viewModel);
}

/// <summary>
/// Base implementation for Document-based editor dock contributions.  Contributions
/// only describe a panel; the dock factory owns its lifetime and placement.
/// </summary>
public abstract class DockPanelContributionBase : IDockPanelContribution
{
    public abstract string PanelId { get; }
    public abstract string TitleKey { get; }
    public abstract DockPanelPlacement Placement { get; }
    public abstract bool IsGlobal { get; }
    public virtual bool ShowInViewMenu => true;
    public virtual bool IsDefaultPanel => false;
    public virtual bool CanClose => true;
    public virtual bool CanFloat => true;
    public virtual IInspectorControlContribution? Inspector => null;
    public abstract object CreateViewModel(IServiceProvider services, object? parameter = null);
    public abstract object CreateView(IServiceProvider services, object viewModel);
}

public interface IEditorExtensionRegistry
{
    IEnumerable<IDockPanelContribution> DockPanelContributions { get; }
    void RegisterDockPanel(IDockPanelContribution contribution);
    IDockPanelContribution? FindDockPanel(string panelId);
}
