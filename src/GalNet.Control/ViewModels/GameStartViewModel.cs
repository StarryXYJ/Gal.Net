using GalNet.Control.View;
using GalNet.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.Control.ViewModels;

/// <summary>
/// 游戏开始页 ViewModel。
/// 持有标题文本和菜单按钮，按钮回调通过 INavigationService 导航。
/// </summary>
public class GameStartViewModel
{
    public string Title { get; }
    public string[] Buttons { get; }

    public GameStartViewModel(INavigationService nav, IServiceProvider serviceProvider)
    {
        Title = "GalNet Demo";
        Buttons = ["New Game", "Settings", "Quit"];
        _nav = nav;
        _sp = serviceProvider;
    }

    private readonly INavigationService _nav;
    private readonly IServiceProvider _sp;

    public void OnButtonClicked(int index)
    {
        switch (index)
        {
            case 0: // New Game
            {
                var gameView = _sp.GetRequiredService<DefaultGameView>();
                var settings = _sp.GetRequiredService<ISettingsService>();
                var runVm = new GameRunViewModel(gameView, settings, () =>
                {
                    // 游戏结束 → 清空导航栈，回到开始页
                    _nav.Clear();
                    var startVm = new GameStartViewModel(_nav, _sp);
                    _nav.NavigateTo(startVm);
                });
                _nav.NavigateTo(runVm);
                break;
            }
            case 1: // Settings
            {
                var settingsVm = new SettingsViewModel(_sp.GetRequiredService<ISettingsService>(), _nav);
                _nav.NavigateTo(settingsVm);
                break;
            }
            case 2: // Quit
                Environment.Exit(0);
                break;
        }
    }
}
