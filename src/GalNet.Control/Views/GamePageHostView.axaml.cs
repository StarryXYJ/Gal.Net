using System;
using Avalonia.Controls;
using GalNet.Control.ViewModels;
using GalNet.Core.Services;
using Serilog;

namespace GalNet.Control.Views;

/// <summary>
/// 游戏主机页面 —— 持有 InternalContent 用于子页面切换。
/// 订阅 GamePageHostViewModel 内部导航服务的 CurrentPageChanged 事件。
/// </summary>
public partial class GamePageHostView : UserControl
{
    public GamePageHostView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is GamePageHostViewModel vm)
        {
            vm.InternalNav.CurrentPageChanged += OnCurrentPageChanged;

            // 处理构造时已导航好的初始页面
            if (vm.InternalNav.CurrentPage != null)
                OnCurrentPageChanged(vm.InternalNav.CurrentPage);
        }
    }

    private void OnCurrentPageChanged(object? page)
    {
        if (page == null)
        {
            InternalContent.Content = null;
            return;
        }

        // 从 DataContext 获取导航服务
        if (DataContext is not GamePageHostViewModel vm)
        {
            Log.Warning("GamePageHostView: DataContext is not GamePageHostViewModel");
            return;
        }

        var viewType = vm.InternalNav.GetRegisteredViewType(page.GetType());
        if (viewType == null)
        {
            Log.Warning("No view registered for {Type}", page.GetType().Name);
            InternalContent.Content = new TextBlock
            {
                Text = $"No view for {page.GetType().Name}",
                Foreground = Avalonia.Media.Brushes.Red,
                FontSize = 18,
            };
            return;
        }

        try
        {
            var view = (Avalonia.Controls.Control)Activator.CreateInstance(viewType)!;
            view.DataContext = page;
            InternalContent.Content = view;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create view for {Type}", page.GetType().Name);
        }
    }
}
