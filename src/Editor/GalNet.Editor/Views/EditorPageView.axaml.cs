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
        // Let text editors consume local undo/IME input before project shortcuts.
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
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
