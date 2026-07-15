using System;
using System.Collections.Generic;
using Avalonia.Controls;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Inspector.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Services;

public sealed class EditorViewFactory : IEditorViewFactory
{
    private readonly IServiceProvider _serviceProvider;
    private static readonly IReadOnlyDictionary<Type, Type> ViewModelToViewMap = new Dictionary<Type, Type>
    {
        [typeof(ProjectSettingsPanelViewModel)] = typeof(ProjectSettingsPanelView),
        [typeof(EditorSettingsPanelViewModel)] = typeof(EditorSettingsPanelView),
        [typeof(EditorWorkspaceViewModel)] = typeof(NodeGraphPanelView),
        [typeof(InspectorHostViewModel)] = typeof(InspectorHostView),
        [typeof(GroupEditorPanelViewModel)] = typeof(GroupEditorPanelView),
        [typeof(NewProjectPanelViewModel)] = typeof(NewProjectPanelView),
        [typeof(GamePreviewPanelViewModel)] = typeof(GamePreviewPanelView),
        [typeof(LogPanelViewModel)] = typeof(LogPanelView)
        , [typeof(AssetPanelViewModel)] = typeof(AssetPanelView)
    };

    public EditorViewFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Avalonia.Controls.Control CreateView(Type viewType, object dataContext)
    {
        var view = (Avalonia.Controls.Control)(_serviceProvider.GetService(viewType)
                   ?? Activator.CreateInstance(viewType)!);
        view.DataContext = dataContext;
        return view;
    }

    public Avalonia.Controls.Control? CreateViewForViewModel(object viewModel)
    {
        if (!ViewModelToViewMap.TryGetValue(viewModel.GetType(), out var viewType))
            return null;

        return CreateView(viewType, viewModel);
    }

    public bool CanCreateViewForViewModel(object viewModel) =>
        ViewModelToViewMap.ContainsKey(viewModel.GetType());
}
