using System;
using GalNet.Editor.Abstraction.Extensibility;
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
    public const string Inspector = "Inspector";
}

public static class BuiltInDockContributions
{
    public static void Register(IEditorExtensionRegistry registry)
    {
        registry.RegisterDockPanel(new DelegateDockPanelContribution(EditorDockPanelIds.NodeGraph, "Dock.Panel.NodeGraph", DockPanelPlacement.MainDocument, false, true, true,
            (sp, _) => sp.GetRequiredService<NodeGraphPanelViewModel>(), typeof(NodeGraphPanelView), new DelegateInspectorContribution(
                (sp, _) => sp.GetRequiredService<NodeInspectorControlViewModel>(), typeof(NodeInspectorControl))));
        registry.RegisterDockPanel(new DelegateDockPanelContribution(EditorDockPanelIds.GamePreview, "Dock.Panel.GamePreview", DockPanelPlacement.MainDocument, true, true, true,
            (sp, _) => sp.GetRequiredService<IGamePreviewPanelFactory>().Create(), typeof(GamePreviewPanelView), new DelegateInspectorContribution(
                (_, dock) => new PreviewVariablesInspectorControlViewModel((GamePreviewPanelViewModel)dock), typeof(PreviewVariablesInspectorControl))));
        registry.RegisterDockPanel(new DelegateDockPanelContribution(EditorDockPanelIds.Assets, "Dock.Panel.Assets", DockPanelPlacement.BottomDocument, true, true, true,
            (sp, _) => sp.GetRequiredService<AssetPanelViewModel>(), typeof(AssetPanelView), new DelegateInspectorContribution(
                (sp, _) => sp.GetRequiredService<AssetInspectorControlViewModel>(), typeof(AssetInspectorControl))));
        registry.RegisterDockPanel(new DelegateDockPanelContribution(EditorDockPanelIds.Log, "Dock.Panel.Log", DockPanelPlacement.BottomDocument, true, true, true,
            (sp, _) => sp.GetRequiredService<LogPanelViewModel>(), typeof(LogPanelView), null));
        registry.RegisterDockPanel(new DelegateDockPanelContribution(EditorDockPanelIds.GroupEditor, "Dock.Panel.GroupEditor", DockPanelPlacement.MainDocument, true, true, false,
            (sp, parameter) => new GroupEditorPanelViewModel(sp.GetRequiredService<EditorWorkspaceViewModel>(), (GraphNode)parameter!, sp.GetRequiredService<IGraphEditingService>()), typeof(GroupEditorPanelView), null));
        registry.RegisterDockPanel(new DelegateDockPanelContribution(EditorDockPanelIds.Inspector, "Dock.Panel.Inspector", DockPanelPlacement.InspectorDocument, false, true, true,
            (sp, _) => sp.GetRequiredService<InspectorHostViewModel>(), typeof(InspectorHostView), null));
    }
}

internal sealed class DelegateDockPanelContribution : IDockPanelContribution
{
    private readonly Func<IServiceProvider, object?, object> _createViewModel; private readonly Type _viewType;
    public string PanelId { get; } public string TitleKey { get; } public DockPanelPlacement Placement { get; }
    public bool CanClose { get; } public bool CanFloat { get; } public bool IsDefaultPanel { get; } public IInspectorControlContribution? Inspector { get; }
    public DelegateDockPanelContribution(string panelId, string title, DockPanelPlacement placement, bool canClose, bool canFloat, bool isDefaultPanel, Func<IServiceProvider, object?, object> createViewModel, Type viewType, IInspectorControlContribution? inspector)
    { PanelId = panelId; TitleKey = title; Placement = placement; CanClose = canClose; CanFloat = canFloat; IsDefaultPanel = isDefaultPanel; _createViewModel = createViewModel; _viewType = viewType; Inspector = inspector; }
    public object CreateViewModel(IServiceProvider services, object? parameter = null) => _createViewModel(services, parameter);
    public object CreateView(IServiceProvider services, object viewModel) => CreateControl(services, _viewType, viewModel);
    internal static Avalonia.Controls.Control CreateControl(IServiceProvider services, Type type, object dataContext)
    {
        var control = (Avalonia.Controls.Control)(services.GetService(type) ?? Activator.CreateInstance(type)!);
        control.DataContext = dataContext;
        return control;
    }
}

internal sealed class DelegateInspectorContribution : IInspectorControlContribution
{
    private readonly Func<IServiceProvider, object, IInspectorControlViewModel> _createViewModel; private readonly Type _viewType;
    public DelegateInspectorContribution(Func<IServiceProvider, object, IInspectorControlViewModel> createViewModel, Type viewType) { _createViewModel = createViewModel; _viewType = viewType; }
    public IInspectorControlViewModel CreateViewModel(IServiceProvider services, object dockViewModel) => _createViewModel(services, dockViewModel);
    public object CreateView(IServiceProvider services, IInspectorControlViewModel viewModel) => DelegateDockPanelContribution.CreateControl(services, _viewType, viewModel);
}
