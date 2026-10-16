using System;
using System.Linq;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Gallery;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Inspector.Views;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.Shared.Commands;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Content;
using GalNet.Runtime.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Composition;

public static class EditorServiceCollectionExtensions
{
    public static IServiceCollection AddEditorServices(this IServiceCollection services)
    {
        services.AddScoped<ProjectSchemaSession>();
        services.AddScoped<IEntryCatalog>(sp => sp.GetRequiredService<ProjectSchemaSession>().Catalog);
        services.AddEditorCoreServices();
        services.AddEditorCommands();
        services.AddEditorNavigation();
        services.AddEditorViews();
        services.AddEditorViewModels();
        services.AddEditorFactories();

        return services;
    }

    private static IServiceCollection AddEditorCoreServices(this IServiceCollection services)
    {
        var resourceTypes = BuiltinResourceTypes.CreateCatalog();
        services.AddSingleton<IResourceTypeCatalog>(resourceTypes);
        services.AddSingleton<IGalleryTypeCatalog>(BuiltinGalleryTypes.CreateCatalog(resourceTypes));
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IEditorSettingsService, EditorSettingsService>();
        services.AddSingleton<IEditorLocalizationService, EditorLocalizationService>();
        services.AddSingleton<IThemeRegistry, ThemeRegistry>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IGameExitService, EditorGameExitService>();
        services.AddScoped<IEditorPlayerVariableStore, EditorPlayerVariableStore>();
        services.AddScoped<GraphAuthoringService>();
        services.AddScoped<IGraphEditingService, GraphEditingService>();
        services.AddScoped<GraphSelectionState>();
        services.AddScoped<EditorWorkspacePersistence>();
        services.AddScoped<EditorGroupProjectionStore>();
        services.AddSingleton<IProjectFileCommandCatalog, AssetFileCommandCatalog>();
        services.AddScoped<IProjectFileCommandExecutor, AssetFileCommandExecutor>();
        services.AddScoped<EditorHistories>();
        services.AddScoped<IProjectEditLifetime>(sp => sp.GetRequiredService<EditorHistories>());
        services.AddSingleton<IProjectClosePrompt, ProjectClosePrompt>();
        services.AddScoped<IProjectContentSession, ProjectContentSession>();
        services.AddSingleton(new ProjectSessionOptions());
        services.AddScoped<UndoRedoRouter>();
        services.AddScoped<IEditorDocumentRepository, EditorDocumentRepository>();
        services.AddScoped<IEditorDocumentService, EditorDocumentService>();
        services.AddScoped<IEditorSaveCoordinator, EditorSaveCoordinator>();
        services.AddScoped<IProjectSaveScheduler, ProjectSaveScheduler>();
        services.AddScoped<IProjectEditLifetime>(sp => (ProjectSaveScheduler)sp.GetRequiredService<IProjectSaveScheduler>());
        services.AddScoped<IVariableDefinitionService, VariableDefinitionService>();
        services.AddScoped<EditorVariableService>();
        services.AddScoped<IVariableService>(sp => sp.GetRequiredService<EditorVariableService>());
        services.AddSingleton<EditorGameDataProvider>();
        services.AddSingleton<IGameContentProvider>(sp => sp.GetRequiredService<EditorGameDataProvider>());
        services.AddSingleton<IEditorExtensionRegistry>(_ =>
        {
            var registry = new EditorExtensionRegistry();
            BuiltInDockContributions.Register(registry);
            return registry;
        });
        services.AddScoped<EditorDockFactory>();
        services.AddSingleton<DockLayoutSerializer>();
        services.AddSingleton<IAssetCatalogService, AssetCatalogService>();
        services.AddSingleton<EditorAssetManager>();
        services.AddSingleton<IAssetManager>(sp => sp.GetRequiredService<EditorAssetManager>());
        services.AddScoped<IGameExportService, GameExportService>();

        return services;
    }

    private static IServiceCollection AddEditorCommands(this IServiceCollection services)
    {
        services.AddSingleton<SaveProjectCommand>();
        services.AddSingleton<CloseProjectCommand>();
        services.AddSingleton<IEditorShortcutCommandDefinition>(sp => sp.GetRequiredService<SaveProjectCommand>());
        services.AddSingleton<IEditorShortcutCommandDefinition>(sp => sp.GetRequiredService<CloseProjectCommand>());
        services.AddScoped<UndoEditorCommand>();
        services.AddScoped<RedoEditorCommand>();
        services.AddScoped<IEditorShortcutCommandDefinition>(sp => sp.GetRequiredService<UndoEditorCommand>());
        services.AddScoped<IEditorShortcutCommandDefinition>(sp => sp.GetRequiredService<RedoEditorCommand>());
        services.AddScoped<EditorShortcutService>(sp => new EditorShortcutService(
            sp.GetServices<IEditorShortcutCommandDefinition>().Concat(
                sp.GetRequiredService<IEntryCatalog>().Definitions.Select(definition => new EntryTypeShortcutCommand(definition))),
            sp.GetRequiredService<IEditorSettingsService>()));

        return services;
    }

    private static IServiceCollection AddEditorNavigation(this IServiceCollection services)
    {
        services.AddSingleton<INavigationService>(sp =>
        {
            var nav = new EditorNavigationService(sp);
            nav.RegisterMap(typeof(StartupPageViewModel), typeof(StartupPageView));
            nav.RegisterMap(typeof(NewProjectPanelViewModel), typeof(NewProjectPanelView));
            nav.RegisterMap(typeof(EditorPageViewModel), typeof(EditorPageView));
            return nav;
        });

        return services;
    }

    private static IServiceCollection AddEditorViews(this IServiceCollection services)
    {
        services.AddTransient<StartupPageView>();
        services.AddTransient<NewProjectPanelView>();
        services.AddTransient<EditorPageView>();
        services.AddTransient<MainWindow>();
        services.AddTransient<AssetPanelView>();
        services.AddTransient<InspectorHostView>();
        services.AddTransient<NodeInspectorControl>();
        services.AddTransient<PreviewVariablesInspectorControl>();
        services.AddTransient<AssetInspectorControl>();
        services.AddTransient<ProjectSettingsWindow>();
        services.AddTransient<EditorSettingsWindow>();
        services.AddTransient<ExportWindow>();
        services.AddTransient<ExportPanelView>();

        return services;
    }

    private static IServiceCollection AddEditorViewModels(this IServiceCollection services)
    {
        services.AddTransient<StartupPageViewModel>();
        services.AddScoped<EditorPageViewModel>();
        services.AddScoped<EditorWorkspaceViewModel>();
        services.AddTransient<InspectorHostViewModel>();
        services.AddTransient<NodeInspectorControlViewModel>();
        services.AddTransient<AssetInspectorControlViewModel>();
        services.AddTransient<NewProjectPanelViewModel>();
        services.AddTransient<LogPanelViewModel>();
        services.AddScoped<AssetPanelViewModel>();
        services.AddTransient<ProjectSettingsPanelViewModel>();
        services.AddTransient<EditorSettingsPanelViewModel>();
        services.AddTransient<ExportPanelViewModel>();
        services.AddTransient<GamePreviewPanelViewModel>();
        services.AddTransient<MainWindowViewModel>();

        return services;
    }

    private static IServiceCollection AddEditorFactories(this IServiceCollection services)
    {
        services.AddSingleton<IEditorPageFactory, EditorPageFactory>();
        services.AddSingleton<IEditorViewFactory, EditorViewFactory>();
        services.AddSingleton<IEditorWindowFactory, EditorWindowFactory>();

        return services;
    }

}
