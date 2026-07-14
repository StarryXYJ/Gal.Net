using GalNet.Core.Services;
using GalNet.Control.Views;
using GalNet.Control.UI.Instances;
using GalNet.Core.UI;

namespace GalNet.Control.ViewModels;

public class GamePageHostViewModel
{
    public INavigationService InternalNav { get; }
    private readonly GameFlowOptions? _options;
    public IGameScreenRouter Router { get; }

    public GamePageHostViewModel(INavigationService parentNav, IGameFlowFactory gameFlowFactory, IScreenTemplateRegistry templates, GameFlowOptions? options = null)
    {
        _options = options;
        InternalNav = parentNav.CreateScope();
        InternalNav.RegisterMap(typeof(GameStartViewModel), typeof(GameStartView));
        InternalNav.RegisterMap(typeof(GameRunViewModel), typeof(GameRunView));
        InternalNav.RegisterMap(typeof(SettingsViewModel), typeof(SettingsView));
        InternalNav.RegisterMap(typeof(SaveLoadViewModel), typeof(SaveLoadView));
        InternalNav.RegisterMap(typeof(GalleryViewModel), typeof(GalleryView));
        if (options is null) throw new ArgumentNullException(nameof(options));
        Router = new GalNet.Control.UI.GameScreenRouter(options.UiProjectProvider, (screen, parameter) =>
        {
            if (!templates.TryGet(screen.TemplateId, out var template) || template is not IScreenBuilderTemplate builder)
                throw new InvalidOperationException($"Unsupported screen template '{screen.TemplateId}'.");
            return builder.Build(screen, new ScreenBuildContext(InternalNav, gameFlowFactory, options, parameter));
        });
        Router.CurrentScreenChanged += screen =>
        {
            InternalNav.Clear();
            if (screen is not null) InternalNav.NavigateTo(screen);
        };
        _ = Router.NavigateAsync("title");
    }
}
