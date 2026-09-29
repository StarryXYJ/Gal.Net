using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class EditorPageView : UserControl
{
    public EditorPageView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DockHost.PointerReleased += (_, _) =>
        {
            if (DataContext is EditorPageViewModel vm)
                vm.PersistLayout();
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || DataContext is not EditorPageViewModel vm)
            return;
        args.Handled = vm.TryExecuteShortcut(args);
    }
}
