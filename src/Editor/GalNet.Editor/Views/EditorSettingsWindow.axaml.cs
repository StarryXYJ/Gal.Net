using Ursa.Controls;

namespace GalNet.Editor.Views;

public partial class EditorSettingsWindow : UrsaWindow
{
    public EditorSettingsWindow()
    {
        InitializeComponent();
        Closed += (_, _) => (DataContext as System.IDisposable)?.Dispose();
    }
}
