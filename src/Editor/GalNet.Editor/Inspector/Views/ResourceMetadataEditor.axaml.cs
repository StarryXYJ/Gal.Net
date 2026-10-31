using Avalonia.Interactivity;
using GalNet.Editor.Inspector.ViewModels;
namespace GalNet.Editor.Inspector.Views;
public partial class ResourceMetadataEditor : global::Avalonia.Controls.UserControl
{
    public ResourceMetadataEditor() => InitializeComponent();
    private async void OnCommit(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ResourceMetadataEditorViewModel model) return;
        try { await model.CommitAsync(); } catch (Exception) { /* The draft retains the error and blocks Save All until corrected. */ }
    }
}
