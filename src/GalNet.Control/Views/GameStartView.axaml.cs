using System;
using Avalonia.Controls;
using GalNet.Control.ViewModels;

namespace GalNet.Control.Views;

/// <summary>
/// 游戏开始页 View —— 包装 TitleScreenView，绑定 ViewModel 按钮事件。
/// </summary>
public partial class GameStartView : UserControl
{
    private Action<int>? _buttonHandler;

    public GameStartView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // 清理旧订阅
        if (_buttonHandler != null)
        {
            TitleScreen.ButtonClicked -= _buttonHandler;
            _buttonHandler = null;
        }

        if (DataContext is GameStartViewModel vm)
        {
            TitleScreen.SetTitle(vm.Title);
            TitleScreen.SetButtons(vm.Buttons);

            _buttonHandler = index => vm.OnButtonClicked(index);
            TitleScreen.ButtonClicked += _buttonHandler;
        }
    }
}
