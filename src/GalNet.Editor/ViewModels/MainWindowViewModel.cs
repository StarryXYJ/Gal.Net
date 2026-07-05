using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Services;

namespace GalNet.Editor.ViewModels;

/// <summary>
/// 主窗口 ViewModel —— 持有导航服务，提供全局状态。
/// ContentControl 绑定到 CurrentPage（由 INavigationService 事件驱动）。
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>导航服务（共享给子页面）。</summary>
    public INavigationService Navigation { get; }

    /// <summary>
    /// 当前页面 —— 由 Navigation.CurrentPageChanged 事件驱动更新，
    /// ContentControl 直接绑定此属性以响应 INotifyPropertyChanged。
    /// </summary>
    [ObservableProperty]
    private object? _currentPage;

    public MainWindowViewModel(INavigationService navigation)
    {
        Navigation = navigation;

        // 监听导航变化，更新可绑定的 CurrentPage 属性
        Navigation.CurrentPageChanged += page =>
        {
            CurrentPage = page;
            if (page is PageViewModelBase pvm)
                WindowTitle = pvm.Title;
        };
    }

    [ObservableProperty]
    private string _windowTitle = "GalNet Editor MVP";
}
