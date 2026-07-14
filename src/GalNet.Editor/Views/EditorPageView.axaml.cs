using Avalonia.Controls;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class EditorPageView : UserControl
{
    public EditorPageView()
    {
        InitializeComponent();
        DockHost.PointerReleased += (_, _) =>
        {
            if (DataContext is EditorPageViewModel vm)
                vm.PersistLayout();
        };
    }
}
