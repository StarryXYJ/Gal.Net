using Avalonia.Controls;
using GalNet.Editor.ViewModels;
using Ursa.Controls;

namespace GalNet.Editor.Views;

public partial class ProjectSettingsWindow : UrsaWindow
{
    private bool _approved;
    public ProjectSettingsWindow()
    {
        InitializeComponent();
        Closing += async (_, args) =>
        {
            if (_approved || DataContext is not ProjectSettingsPanelViewModel viewModel) return;
            args.Cancel = true;
            try { await viewModel.CommitDraftAsync(); _approved = true; Close(); }
            catch (System.IO.InvalidDataException) { }
        };
        Closed += (_, _) => (DataContext as System.IDisposable)?.Dispose();
    }
}
