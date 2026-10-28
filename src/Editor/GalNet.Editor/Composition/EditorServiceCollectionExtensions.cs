using System;
using System.Collections.Generic;
using System.Linq;
using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Commands;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Commands;
using GalNet.Editor.Controls;
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
using GalNet.Game.Controls.Forms;
using GalNet.GameHost.Content;
using GalNet.Rendering.Scene;
using GalNet.Runtime.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Editor.Composition;

public static class EditorServiceCollectionExtensions
{
    public static IServiceCollection AddEditorServices(this IServiceCollection services)
    {
        services.AddScoped<ProjectSchemaSession>();
        services.AddScoped<ExternalGameDebugClient>();
        services.AddScoped<ProjectDebugPreparation>();
        services.AddScoped<ExternalDebugPanelViewModel>();
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
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IEditorSettingsService, EditorSettingsService>();
        services.AddSingleton<IEditorLocalizationService, EditorLocalizationService>();
        services.AddSingleton<IThemeRegistry, ThemeRegistry>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddScoped<GraphAuthoringService>();
        services.AddScoped<GroupEntryAuthoringService>();
        services.AddScoped<EntryFieldAuthoringService>();
        services.AddScoped<EntrySelectionContext>();
        services.AddScoped<EntryEditSession>();
        services.AddScoped<AuthorNavigationService>();
        services.AddScoped<ProblemsPanelViewModel>();
        services.AddScoped<TimelineEditSession>();
        services.AddScoped<ProjectSceneTargets>();
        services.AddScoped<IEntryParameterMetadataSource>(sp => sp.GetRequiredService<ProjectSceneTargets>());
        services.AddScoped<ProjectLocalizationQuery>();
        services.AddScoped<LocalizationDocumentStore>();
        services.AddScoped<LocalizationAuthoringService>();
        services.AddScoped<GameTextAuthoringService>();
        services.AddScoped<GameLocalizationPanelService>();
        services.AddScoped<LocalizationExchangeService>();
        services.AddScoped<GameLocalizationPanelViewModel>();
        services.AddScoped<ResourceOverrideAuthoringService>();
        services.AddScoped<ResourceOverridesPanelViewModel>();
        services.AddScoped<CharacterAuthoringService>();
        services.AddScoped<CharacterPanelState>();
        services.AddTransient<CharactersPanelViewModel>();
        services.AddScoped<SceneTargetCatalog>();
        services.AddTransient<TimelinePanelViewModel>();
        services.AddScoped<VariableAuthoringService>();
        services.AddScoped<VariablePanelState>();
        services.AddTransient<VariablesPanelViewModel>();
        services.AddScoped<EditorRegistry>(sp =>
        {
            var definitions = sp.GetRequiredService<IVariableDefinitionService>();
            IReadOnlyList<GalNet.Core.Variable.ProjectVariableDefinition> Variables() =>
                [.. definitions.GetDefinitions(GalNet.Core.Variable.VariableScope.Player), .. definitions.GetDefinitions(GalNet.Core.Variable.VariableScope.Save)];
            var registry = EditorFieldEditors.CreateDefault(sp.GetRequiredService<SceneTargetCatalog>(), context =>
            {
                if (context.IsEnabled && context.HostContext is Models.Graph.IEntryFieldOrigin { GroupId: { } groupId } origin)
                    sp.GetRequiredService<EditorDockFactory>().OpenTimeline(groupId, origin.EntryId);
            }, sp.GetRequiredService<IAssetManager>(), sp.GetRequiredService<CharacterAuthoringService>(), sp.GetRequiredService<GameTextAuthoringService>(),
                sp.GetRequiredService<IProjectContentSession>(), sp.GetRequiredService<ProjectSceneTargets>());
            registry.Replace("entry.expression", context => AuthoringExpressionFieldEditors.Expression(context,
                () => definitions.GetDefinitions(GalNet.Core.Variable.VariableScope.Player).Select(variable => new ConditionVariableSuggestion { Name = variable.Name, Scope = GalNet.Core.Variable.VariableScope.Player })
                    .Concat(definitions.GetDefinitions(GalNet.Core.Variable.VariableScope.Save).Select(variable => new ConditionVariableSuggestion { Name = variable.Name, Scope = GalNet.Core.Variable.VariableScope.Save })).ToArray(),
                Variables));
            registry.Replace("entry.variablename", context => AuthoringExpressionFieldEditors.Variable(context,
                () => Variables().Select(variable => variable.Name).ToArray()));
            return registry;
        });
        services.AddScoped<IGraphEditingService, GraphEditingService>();
        services.AddScoped<GraphSelectionState>();
        services.AddScoped<EditorWorkspacePersistence>();
        services.AddScoped<EditorGroupProjectionStore>();
        services.AddSingleton<IProjectFileCommandCatalog, AssetFileCommandCatalog>();
        services.AddScoped<ProjectFileAuthoringService>();
        services.AddScoped<IProjectFileCommandExecutor>(provider => provider.GetRequiredService<ProjectFileAuthoringService>());
        services.AddScoped<EditorHistories>();
        services.AddScoped<ProjectExternalChanges>();
        services.AddScoped<IProjectEditLifetime>(sp => sp.GetRequiredService<ProjectExternalChanges>());
        services.AddScoped<IProjectEditLifetime>(sp => sp.GetRequiredService<EditorHistories>());
        services.AddSingleton<IProjectClosePrompt, ProjectClosePrompt>();
        services.AddScoped<IProjectContentSession, ProjectContentSession>();
        services.AddSingleton(new ProjectSessionOptions());
        services.AddScoped<UndoRedoRouter>();
        services.AddScoped<IEditorDocumentRepository>(provider => new EditorDocumentRepository(provider.GetRequiredService<IEntryCatalog>(), EditorDocumentValidation.Authoring));
        services.AddScoped<IEditorDocumentService, EditorDocumentService>();
        services.AddScoped<IProjectSaveScheduler, ProjectSaveScheduler>();
        services.AddScoped<IProjectEditLifetime>(sp => (ProjectSaveScheduler)sp.GetRequiredService<IProjectSaveScheduler>());
        services.AddScoped<IVariableDefinitionService, VariableDefinitionService>();
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
        services.AddSingleton<IAssetManager>(sp =>
        {
            var assets = sp.GetRequiredService<EditorAssetManager>();
            assets.RegisterDecoder<SceneTexture>("sprite", new SceneTextureAssetDecoder());
            return assets;
        });
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
        services.AddTransient<TimelinePanelView>();
        services.AddTransient<ExternalDebugPanelView>();
        services.AddTransient<InspectorHostView>();
        services.AddTransient<NodeInspectorControl>();
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
        services.AddScoped<ProjectFilesIndex>();
        services.AddScoped<ProjectTextFiles>();
        services.AddTransient<ProjectSettingsPanelViewModel>();
        services.AddTransient<EditorSettingsPanelViewModel>();
        services.AddTransient<ExportPanelViewModel>();
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
