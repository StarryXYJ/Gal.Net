using System;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Inspector.Views;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Dock;

public static class EditorDockPanelIds
{
    public const string NodeGraph = "NodeGraph";
    public const string GamePreview = "GamePreview";
    public const string Assets = "Assets";
    public const string Log = "Log";
    public const string GroupEditor = "GroupEditor";
    public const string FileEditor = "FileEditor";
    public const string Inspector = "Inspector";
}

public static class BuiltInDockContributions
{
    public static void Register(IEditorExtensionRegistry registry)
    {
        registry.RegisterDockPanel(new DelegateDockPanelContribution<EditorWorkspaceViewModel>(
            EditorDockPanelIds.NodeGraph, "Dock.Panel.NodeGraph", DockPanelPlacement.MainDocument, true, true, true, true, true,
            sp => sp.GetRequiredService<EditorWorkspaceViewModel>(), typeof(NodeGraphPanelView),
            new DelegateInspectorContribution<EditorWorkspaceViewModel, NodeInspectorControlViewModel>(
                (sp, _) => sp.GetRequiredService<NodeInspectorControlViewModel>(), typeof(NodeInspectorControl))));
        registry.RegisterDockPanel(new DelegateDockPanelContribution<AssetPanelViewModel>(
            EditorDockPanelIds.Assets, "Dock.Panel.Assets", DockPanelPlacement.BottomDocument, true, true, true, true, true,
            sp => sp.GetRequiredService<AssetPanelViewModel>(), typeof(AssetPanelView),
            new DelegateInspectorContribution<AssetPanelViewModel, AssetInspectorControlViewModel>(
                (sp, _) => sp.GetRequiredService<AssetInspectorControlViewModel>(), typeof(AssetInspectorControl))));
        registry.RegisterDockPanel(new DelegateDockPanelContribution<LogPanelViewModel>(
            EditorDockPanelIds.Log, "Dock.Panel.Log", DockPanelPlacement.BottomDocument, true, true, true, true, true,
            sp => sp.GetRequiredService<LogPanelViewModel>(), typeof(LogPanelView), null));
        registry.RegisterDockPanel(new DelegateDockPanelContribution<GroupEditorPanelViewModel, string>(
            EditorDockPanelIds.GroupEditor, "Dock.Panel.GroupEditor", DockPanelPlacement.MainDocument, false, true, true, false, false,
            (sp, node) => new GroupEditorPanelViewModel(
                sp.GetRequiredService<EditorWorkspaceViewModel>(),
                node,
                sp.GetRequiredService<IGraphEditingService>(),
                sp.GetRequiredService<IProjectService>(),
                sp.GetRequiredService<IAssetManager>(),
                sp.GetRequiredService<EditorShortcutService>(),
                sp.GetRequiredService<IEffectCatalog>(),
                sp.GetRequiredService<IEntryCatalog>()),
            typeof(GroupEditorPanelView), null));
        registry.RegisterDockPanel(new DelegateDockPanelContribution<FileDocumentPanelViewModel, string>(
            EditorDockPanelIds.FileEditor, "Files.Document", DockPanelPlacement.MainDocument, false, true, true, false, false,
            (sp, id) => new FileDocumentPanelViewModel(sp.GetRequiredService<ProjectTextFiles>().Get(id)), typeof(FileDocumentPanelView), null));
        registry.RegisterDockPanel(new DelegateDockPanelContribution<InspectorHostViewModel>(
            EditorDockPanelIds.Inspector, "Dock.Panel.Inspector", DockPanelPlacement.InspectorDocument, false, true, true, true, true,
            sp => sp.GetRequiredService<InspectorHostViewModel>(), typeof(InspectorHostView), null));
    }
}

internal sealed class DelegateDockPanelContribution<TViewModel> : DockPanelContributionBase<TViewModel>
    where TViewModel : class
{
    private readonly Func<IServiceProvider, TViewModel> _createViewModel;
    private readonly Type _viewType;

    public DelegateDockPanelContribution(
        string panelId,
        string title,
        DockPanelPlacement placement,
        bool isGlobal,
        bool canClose,
        bool canFloat,
        bool isDefaultPanel,
        bool showInViewMenu,
        Func<IServiceProvider, TViewModel> createViewModel,
        Type viewType,
        IInspectorControlContribution? inspector)
    {
        PanelId = panelId;
        TitleKey = title;
        Placement = placement;
        IsGlobal = isGlobal;
        CanClose = canClose;
        CanFloat = canFloat;
        IsDefaultPanel = isDefaultPanel;
        ShowInViewMenu = showInViewMenu;
        _createViewModel = createViewModel;
        _viewType = viewType;
        Inspector = inspector;
    }

    public override string PanelId { get; }
    public override string TitleKey { get; }
    public override DockPanelPlacement Placement { get; }
    public override bool IsGlobal { get; }
    public override bool ShowInViewMenu { get; }
    public override bool CanClose { get; }
    public override bool CanFloat { get; }
    public override bool IsDefaultPanel { get; }
    public override IInspectorControlContribution? Inspector { get; }

    protected override TViewModel CreateViewModel(IServiceProvider services) => _createViewModel(services);

    protected override object CreateView(IServiceProvider services, TViewModel viewModel) =>
        EditorViewFactory.CreateControl(services, _viewType, viewModel);
}

internal sealed class DelegateDockPanelContribution<TViewModel, TParameter> :
    DockPanelContributionBase<TViewModel, TParameter>
    where TViewModel : class
    where TParameter : class
{
    private readonly Func<IServiceProvider, TParameter, TViewModel> _createViewModel;
    private readonly Type _viewType;

    public DelegateDockPanelContribution(
        string panelId,
        string title,
        DockPanelPlacement placement,
        bool isGlobal,
        bool canClose,
        bool canFloat,
        bool isDefaultPanel,
        bool showInViewMenu,
        Func<IServiceProvider, TParameter, TViewModel> createViewModel,
        Type viewType,
        IInspectorControlContribution? inspector)
    {
        PanelId = panelId;
        TitleKey = title;
        Placement = placement;
        IsGlobal = isGlobal;
        CanClose = canClose;
        CanFloat = canFloat;
        IsDefaultPanel = isDefaultPanel;
        ShowInViewMenu = showInViewMenu;
        _createViewModel = createViewModel;
        _viewType = viewType;
        Inspector = inspector;
    }

    public override string PanelId { get; }
    public override string TitleKey { get; }
    public override DockPanelPlacement Placement { get; }
    public override bool IsGlobal { get; }
    public override bool ShowInViewMenu { get; }
    public override bool CanClose { get; }
    public override bool CanFloat { get; }
    public override bool IsDefaultPanel { get; }
    public override IInspectorControlContribution? Inspector { get; }

    protected override TViewModel CreateViewModel(IServiceProvider services, TParameter parameter) =>
        _createViewModel(services, parameter);

    protected override object CreateView(IServiceProvider services, TViewModel viewModel) =>
        EditorViewFactory.CreateControl(services, _viewType, viewModel);
}

internal sealed class DelegateInspectorContribution<TDockViewModel, TInspectorViewModel> :
    InspectorControlContributionBase<TDockViewModel, TInspectorViewModel>
    where TDockViewModel : class
    where TInspectorViewModel : class, IInspectorControlViewModel
{
    private readonly Func<IServiceProvider, TDockViewModel, TInspectorViewModel> _createViewModel;
    private readonly Type _viewType;

    public DelegateInspectorContribution(
        Func<IServiceProvider, TDockViewModel, TInspectorViewModel> createViewModel,
        Type viewType)
    {
        _createViewModel = createViewModel;
        _viewType = viewType;
    }

    protected override TInspectorViewModel CreateViewModel(
        IServiceProvider services,
        TDockViewModel dockViewModel) => _createViewModel(services, dockViewModel);

    protected override object CreateView(IServiceProvider services, TInspectorViewModel viewModel) =>
        EditorViewFactory.CreateControl(services, _viewType, viewModel);
}
