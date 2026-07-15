using GalNet.Control.Services;
using GalNet.Control.View;
using GalNet.Control.ViewModels;
using GalNet.Control.Views;
using GalNet.Core.Services;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Commands;
using GalNet.Editor.Dock;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Inspector.Views;
using GalNet.Control.UI;
using GalNet.Control.Abstraction.UI;
using GalNet.Core.Assets;
using GalNet.Core.UI;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Composition;

public static class EditorServiceCollectionExtensions
{
    public static IServiceCollection AddEditorServices(this IServiceCollection services)
    {
        services.AddEditorCoreServices();
        services.AddEditorCommands();
        services.AddEditorNavigation();
        services.AddEditorViews();
        services.AddEditorViewModels();
        services.AddEditorFactories();
        services.AddGamePreviewServices();

        return services;
    }

    private static IServiceCollection AddEditorCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IEditorSettingsService, EditorSettingsService>();
        services.AddSingleton<IEditorLocalizationService, EditorLocalizationService>();
        services.AddSingleton<IThemeRegistry, ThemeRegistry>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IGameExitService, EditorGameExitService>();
        services.AddSingleton<IEditorPlayerVariableStore, EditorPlayerVariableStore>();
        services.AddSingleton<IGraphEditingService, GraphEditingService>();
        services.AddSingleton<IEditorDocumentRepository, EditorDocumentRepository>();
        services.AddSingleton<IEditorDocumentService, EditorDocumentService>();
        services.AddSingleton<IEditorSaveCoordinator, EditorSaveCoordinator>();
        services.AddSingleton<IVariableDefinitionService, VariableDefinitionService>();
        services.AddSingleton<EditorVariableService>();
        services.AddSingleton<IVariableService>(sp => sp.GetRequiredService<EditorVariableService>());
        services.AddSingleton<EditorGameDataProvider>();
        services.AddSingleton<IGameContentProvider>(sp => sp.GetRequiredService<EditorGameDataProvider>());
        services.AddSingleton<IEditorExtensionRegistry>(_ =>
        {
            var registry = new EditorExtensionRegistry();
            BuiltInDockContributions.Register(registry);
            return registry;
        });
        services.AddSingleton<EditorDockFactory>();
        services.AddSingleton<DockLayoutSerializer>();
        services.AddSingleton<IAssetCatalogService, AssetCatalogService>();
        services.AddSingleton<EditorAssetManager>();
        services.AddSingleton<IAssetManager>(sp => sp.GetRequiredService<EditorAssetManager>());
        services.AddSingleton<IUiPresetRegistry, BuiltInUiPresetRegistry>();

        return services;
    }

    private static IServiceCollection AddEditorCommands(this IServiceCollection services)
    {
        services.AddSingleton<CommandService>();
        services.AddSingleton<SaveProjectCommand>();
        services.AddSingleton<CloseProjectCommand>();
        services.AddSingleton<EditorCommand>(sp => sp.GetRequiredService<SaveProjectCommand>());
        services.AddSingleton<EditorCommand>(sp => sp.GetRequiredService<CloseProjectCommand>());

        return services;
    }

    private static IServiceCollection AddEditorNavigation(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService>(sp =>
        {
            var nav = new NavigationService(sp);
            nav.RegisterMap(typeof(StartupPageViewModel), typeof(StartupPageView));
            nav.RegisterMap(typeof(NewProjectPanelViewModel), typeof(NewProjectPanelView));
            nav.RegisterMap(typeof(EditorPageViewModel), typeof(EditorPageView));
            nav.RegisterMap(typeof(GamePageHostViewModel), typeof(GamePageHostView));
            return nav;
        });

        return services;
    }

    private static IServiceCollection AddEditorViews(this IServiceCollection services)
    {
        services.AddTransient<StartupPageView>();
        services.AddTransient<NewProjectPanelView>();
        services.AddTransient<EditorPageView>();
        services.AddTransient<GamePageHostView>();
        services.AddTransient<MainWindow>();
        services.AddTransient<AssetPanelView>();
        services.AddTransient<UiCustomizationPanelView>();
        services.AddTransient<InspectorHostView>();
        services.AddTransient<NodeInspectorControl>();
        services.AddTransient<PreviewVariablesInspectorControl>();
        services.AddTransient<AssetInspectorControl>();
        services.AddTransient<ProjectSettingsWindow>();
        services.AddTransient<EditorSettingsWindow>();

        return services;
    }

    private static IServiceCollection AddEditorViewModels(this IServiceCollection services)
    {
        services.AddTransient<StartupPageViewModel>();
        services.AddTransient<EditorPageViewModel>();
        services.AddScoped<EditorWorkspaceViewModel>();
        services.AddTransient<InspectorHostViewModel>();
        services.AddTransient<NodeInspectorControlViewModel>();
        services.AddTransient<AssetInspectorControlViewModel>();
        services.AddTransient<NewProjectPanelViewModel>();
        services.AddTransient<LogPanelViewModel>();
        services.AddScoped<AssetPanelViewModel>();
        services.AddTransient<ProjectSettingsPanelViewModel>();
        services.AddTransient<EditorSettingsPanelViewModel>();
        services.AddTransient<GamePreviewPanelViewModel>();
        services.AddScoped<UiCustomizationPanelViewModel>();
        services.AddTransient<MainWindowViewModel>();

        return services;
    }

    private static IServiceCollection AddEditorFactories(this IServiceCollection services)
    {
        services.AddSingleton<IEditorPageFactory, EditorPageFactory>();
        services.AddSingleton<IEditorViewFactory, EditorViewFactory>();
        services.AddSingleton<IEditorWindowFactory, EditorWindowFactory>();
        services.AddSingleton<IGamePreviewPanelFactory, GamePreviewPanelFactory>();
        services.AddSingleton<IGameFlowFactory, GameFlowFactory>();

        return services;
    }

    private static IServiceCollection AddGamePreviewServices(this IServiceCollection services)
    {
        return services;
    }
}
