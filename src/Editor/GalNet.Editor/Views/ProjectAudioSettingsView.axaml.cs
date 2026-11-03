using Avalonia.Controls;

namespace GalNet.Editor.Views;

public partial class ProjectAudioSettingsView : UserControl
{
    public ProjectAudioSettingsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Classes.Set("narrow", Bounds.Width < 600);
    }
}
