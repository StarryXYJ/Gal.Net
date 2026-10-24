using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace GalNet.Editor.Views;

public partial class CharactersPanelView : UserControl
{
    public CharactersPanelView() => InitializeComponent();
    private void OnCharacterAdded(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(() => { CharacterName.Focus(); CharacterName.SelectAll(); });
}
