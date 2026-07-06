using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.Services;
using GalNet.Editor.Models;

namespace GalNet.Editor.ViewModels;

/// <summary>
/// 主窗口 ViewModel —— 持有导航服务，提供全局状态。
/// ContentControl 绑定到 CurrentPage（由 INavigationService 事件驱动）。
/// MenuItems 由当前页面的 IMenuProvider 提供。
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

    /// <summary>窗口标题</summary>
    [ObservableProperty]
    private string _windowTitle = "GalNet Editor";

    /// <summary>当前菜单项集合 —— 由当前页面的 IMenuProvider 提供</summary>
    [ObservableProperty]
    private AvaloniaList<MenuData> _menuItems = new();

    public MainWindowViewModel(INavigationService navigation)
    {
        Navigation = navigation;

        // 监听导航变化，更新可绑定的 CurrentPage 属性
        Navigation.CurrentPageChanged += page =>
        {
            CurrentPage = page;
            if (page is PageViewModelBase pvm)
                WindowTitle = pvm.Title;

            // 从页面获取菜单数据
            UpdateMenuItems(page);
        };
    }

    private void UpdateMenuItems(object? page)
    {
        MenuItems.Clear();
        if (page is IMenuProvider provider && provider.MenuItems is { } items)
        {
            foreach (var item in items)
                MenuItems.Add(item);
        }
    }
}
