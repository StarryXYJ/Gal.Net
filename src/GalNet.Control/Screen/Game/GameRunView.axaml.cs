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
            if (!ReferenceEquals(_viewModel, vm))
                ReleaseGameView();
            _viewModel = vm;
            StartIfAttached();
        }
        else
        {
            ReleaseGameView();
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
        {
            if (!ReferenceEquals(GameViewHost.Content, _viewModel.GameView))
                GameViewHost.Content = _viewModel.GameView;
            _viewModel.Start();
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        ReleaseGameView();
        base.OnDetachedFromVisualTree(e);
    }

    private void ReleaseGameView()
    {
        if (_viewModel is not null && ReferenceEquals(GameViewHost.Content, _viewModel.GameView))
            GameViewHost.Content = null;
    }
}
