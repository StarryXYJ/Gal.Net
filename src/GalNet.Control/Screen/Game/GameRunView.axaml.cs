using System;
using Avalonia.Controls;
using GalNet.Control.ViewModels;

namespace GalNet.Control.Views;

/// <summary>
/// 游戏运行页 View —— 将自身 Content 设置为 ViewModel 的 GameView。
/// </summary>
public partial class GameRunView : UserControl
{
    private GameRunViewModel? _viewModel;
    public GameRunView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is GameRunViewModel vm)
        {
            _viewModel = vm;
            StartIfAttached();
        }
        else
        {
            _viewModel = null;
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartIfAttached();
    }

    private void StartIfAttached()
    {
        if (VisualRoot is not null && _viewModel is not null)
            _viewModel.Start();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
    }
}
