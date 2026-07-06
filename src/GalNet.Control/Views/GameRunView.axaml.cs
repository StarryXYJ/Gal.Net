using System;
using Avalonia.Controls;
using GalNet.Control.ViewModels;

namespace GalNet.Control.Views;

/// <summary>
/// 游戏运行页 View —— 将自身 Content 设置为 ViewModel 的 GameView。
/// </summary>
public partial class GameRunView : UserControl
{
    public GameRunView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        Content = DataContext is GameRunViewModel vm ? vm.GameView : null;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        Content = null;
        base.OnDetachedFromVisualTree(e);
    }
}
