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

/// <summary>Strongly typed contribution base for panels that do not accept an instance parameter.</summary>
public abstract class DockPanelContributionBase<TViewModel> : DockPanelContributionBase
    where TViewModel : class
{
    public sealed override object CreateViewModel(IServiceProvider services, object? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (parameter is not null)
            throw ContributionTypeGuard.UnexpectedParameter(GetType(), parameter);
        return CreateViewModel(services);
    }

    public sealed override object CreateView(IServiceProvider services, object viewModel)
    {
        ArgumentNullException.ThrowIfNull(services);
        return CreateView(services, ContributionTypeGuard.Require<TViewModel>(GetType(), viewModel, nameof(viewModel)));
    }

    protected abstract TViewModel CreateViewModel(IServiceProvider services);
    protected abstract object CreateView(IServiceProvider services, TViewModel viewModel);
}

/// <summary>Strongly typed contribution base for panels that require an instance parameter.</summary>
public abstract class DockPanelContributionBase<TViewModel, TParameter> : DockPanelContributionBase
    where TViewModel : class
    where TParameter : class
{
    public sealed override object CreateViewModel(IServiceProvider services, object? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        return CreateViewModel(
            services,
            ContributionTypeGuard.Require<TParameter>(GetType(), parameter, nameof(parameter)));
    }

    public sealed override object CreateView(IServiceProvider services, object viewModel)
    {
        ArgumentNullException.ThrowIfNull(services);
        return CreateView(services, ContributionTypeGuard.Require<TViewModel>(GetType(), viewModel, nameof(viewModel)));
    }

    protected abstract TViewModel CreateViewModel(IServiceProvider services, TParameter parameter);
    protected abstract object CreateView(IServiceProvider services, TViewModel viewModel);
}

/// <summary>Strongly typed bridge between one dock ViewModel and its inspector.</summary>
public abstract class InspectorControlContributionBase<TDockViewModel, TInspectorViewModel> : IInspectorControlContribution
    where TDockViewModel : class
    where TInspectorViewModel : class, IInspectorControlViewModel
{
    public IInspectorControlViewModel CreateViewModel(IServiceProvider services, object dockViewModel)
    {
        ArgumentNullException.ThrowIfNull(services);
        return CreateViewModel(
            services,
            ContributionTypeGuard.Require<TDockViewModel>(GetType(), dockViewModel, nameof(dockViewModel)));
    }

    public object CreateView(IServiceProvider services, IInspectorControlViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(services);
        return CreateView(
            services,
            ContributionTypeGuard.Require<TInspectorViewModel>(GetType(), viewModel, nameof(viewModel)));
    }

    protected abstract TInspectorViewModel CreateViewModel(IServiceProvider services, TDockViewModel dockViewModel);
    protected abstract object CreateView(IServiceProvider services, TInspectorViewModel viewModel);
}

internal static class ContributionTypeGuard
{
    public static T Require<T>(Type contributionType, object? value, string parameterName) where T : class
    {
        if (value is T typed)
            return typed;

        var actual = value?.GetType().FullName ?? "null";
        throw new ArgumentException(
            $"Contribution '{contributionType.FullName}' requires {parameterName} of type '{typeof(T).FullName}', but received '{actual}'.",
            parameterName);
    }

    public static ArgumentException UnexpectedParameter(Type contributionType, object parameter) =>
        new(
            $"Contribution '{contributionType.FullName}' does not accept a parameter, but received '{parameter.GetType().FullName}'.",
            nameof(parameter));
}

public interface IEditorExtensionRegistry
{
    IEnumerable<IDockPanelContribution> DockPanelContributions { get; }
    void RegisterDockPanel(IDockPanelContribution contribution);
    IDockPanelContribution? FindDockPanel(string panelId);
}
