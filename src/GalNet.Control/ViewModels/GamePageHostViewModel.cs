using GalNet.Core.Services;
using GalNet.Control.Views;

namespace GalNet.Control.ViewModels;

/// <summary>
/// 游戏自包含页面主机 ViewModel。
/// 持有自己的作用域导航，管理开始页/运行页的切换。
/// 通过构造拿到所有所需依赖，注册后即可独立运行。
/// </summary>
public class GamePageHostViewModel
{
    /// <summary>内部导航服务（子导航器）。</summary>
    public INavigationService InternalNav { get; }

    public GamePageHostViewModel(INavigationService parentNav, IServiceProvider serviceProvider)
    {
        InternalNav = parentNav.CreateScope();
        InternalNav.RegisterMap(typeof(GameStartViewModel), typeof(GameStartView));
        InternalNav.RegisterMap(typeof(GameRunViewModel), typeof(GameRunView));
        InternalNav.RegisterMap(typeof(SettingsViewModel), typeof(SettingsView));

        // 手动创建开始页 VM，传入作用域导航和服务提供者
        var startVm = new GameStartViewModel(InternalNav, serviceProvider);
        InternalNav.NavigateTo(startVm);
    }
}
