using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GalNet.Editor.ViewModels;
using Ursa.Controls;

namespace GalNet.Editor.Views;

public partial class GamePreviewPanelView : UserControl
{
    private GamePreviewPanelViewModel? _vm;

    public GamePreviewPanelView()
    {
        InitializeComponent();
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_vm is not null)
            _ = _vm.DisposeAsync();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.NameConflictRequested -= OnNameConflictRequested;
        }

        _vm = DataContext as GamePreviewPanelViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.NameConflictRequested += OnNameConflictRequested;
            SyncPageHost();
        }

        base.OnDataContextChanged(e);
    }

    private async void OnNameConflictRequested(string name)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        await MessageBox.ShowAsync(
            owner,
            $"Variable name \"{name}\" already exists.",
            "Duplicate Name",
            button: MessageBoxButton.OK);
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GamePreviewPanelViewModel.PreviewShell))
            SyncPageHost();
    }

    private async void OnResetPlayerClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
            return;

        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            return;

        var result = await MessageBox.ShowAsync(
            owner,
            "Reset all player variables to their default values?",
            "Reset Player",
            button: MessageBoxButton.OKCancel);

        if (result == MessageBoxResult.OK)
            await _vm.ResetPlayerAsync();
    }

    private void SyncPageHost()
    {
        if (_vm?.PreviewShell is null)
        {
            GameViewHost.GameContent = null;
            return;
        }

        if (ReferenceEquals(GameViewHost.GameContent, _vm.PreviewShell))
            return;

        GameViewHost.GameContent = _vm.PreviewShell;
    }
}
