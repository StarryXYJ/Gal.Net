using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia;
using System.Linq;
using System;
using GalNet.Editor.Models;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;
public partial class AssetPanelView : UserControl
{
    private static readonly DataFormat<string> InternalAssetFormat = DataFormat.CreateStringApplicationFormat("galnet.asset-relative-path");
    private (AssetEntry Entry, PointerPressedEventArgs Args, Point Position)? _pendingInternalDrag;

    public AssetPanelView() => InitializeComponent();
    private void OnEntryDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: AssetEntry entry } && DataContext is AssetPanelViewModel vm)
            vm.OpenCommand.Execute(entry);
    }
    private void OnPathLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && DataContext is AssetPanelViewModel vm) vm.NavigatePathCommand.Execute(box.Text);
    }
    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox box && DataContext is AssetPanelViewModel vm) { vm.NavigatePathCommand.Execute(box.Text); e.Handled = true; }
    }
    private void OnEntryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(sender as Visual).Properties.IsLeftButtonPressed || sender is not Avalonia.Controls.Control { DataContext: AssetEntry entry }) return;
        _pendingInternalDrag = (entry, e, e.GetPosition(this));
    }
    private async void OnEntryPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingInternalDrag is not { } pending || sender is not Avalonia.Controls.Control) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - pending.Position.X) < 5 && Math.Abs(position.Y - pending.Position.Y) < 5) return;
        _pendingInternalDrag = null;
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(InternalAssetFormat, pending.Entry.RelativePath));
        await DragDrop.DoDragDropAsync(pending.Args, data, DragDropEffects.Move);
    }
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(InternalAssetFormat))
        {
            e.DragEffects = DragDropEffects.None;
            (DataContext as AssetPanelViewModel)?.SetExternalDropActive(false);
            return;
        }
        var files = e.DataTransfer.TryGetFiles();
        e.DragEffects = files?.Any() == true ? DragDropEffects.Copy : DragDropEffects.None;
        if (DataContext is AssetPanelViewModel vm) vm.SetExternalDropActive(e.DragEffects == DragDropEffects.Copy);
        e.Handled = true;
    }
    private void OnDragLeave(object? sender, RoutedEventArgs e) => (DataContext as AssetPanelViewModel)?.SetExternalDropActive(false);
    private void OnBackDragOver(object? sender, DragEventArgs e)
    {
        if (DataContext is not AssetPanelViewModel vm || !e.DataTransfer.Formats.Contains(InternalAssetFormat)) return;
        var sourcePath = e.DataTransfer.TryGetValue(InternalAssetFormat);
        var allowed = sourcePath is not null && vm.CanMoveToParent(sourcePath);
        e.DragEffects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        vm.SetParentDropTarget(allowed);
        e.Handled = true;
    }
    private void OnBackDragLeave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AssetPanelViewModel vm) vm.SetParentDropTarget(false);
    }
    private async void OnBackDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is AssetPanelViewModel vm && e.DataTransfer.Formats.Contains(InternalAssetFormat))
        {
            var sourcePath = e.DataTransfer.TryGetValue(InternalAssetFormat);
            if (sourcePath is not null) await vm.MoveInternalToParentAsync(sourcePath);
            e.Handled = true;
        }
        if (DataContext is AssetPanelViewModel viewModel) viewModel.SetParentDropTarget(false);
    }
    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        var paths = files?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        if (paths.Length > 0 && DataContext is AssetPanelViewModel vm) vm.ImportExternalCommand.Execute(paths);
        if (DataContext is AssetPanelViewModel viewModel) viewModel.SetExternalDropActive(false);
        e.Handled = true;
    }
    private void OnEntryDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: AssetEntry target } || DataContext is not AssetPanelViewModel vm || !e.DataTransfer.Formats.Contains(InternalAssetFormat)) return;
        var sourcePath = e.DataTransfer.TryGetValue(InternalAssetFormat);
        var allowed = sourcePath is not null && vm.CanMoveTo(target, sourcePath);
        e.DragEffects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        vm.SetDropTarget(allowed ? target : null);
        e.Handled = true;
    }
    private void OnEntryDragLeave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AssetPanelViewModel vm) vm.SetDropTarget(null);
    }
    private async void OnEntryDrop(object? sender, DragEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: AssetEntry target } && DataContext is AssetPanelViewModel vm && e.DataTransfer.Formats.Contains(InternalAssetFormat))
        {
            var sourcePath = e.DataTransfer.TryGetValue(InternalAssetFormat);
            if (sourcePath is not null) await vm.MoveInternalAsync(sourcePath, target);
            e.Handled = true;
        }
        if (DataContext is AssetPanelViewModel viewModel) viewModel.SetDropTarget(null);
    }
    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is Avalonia.Controls.Control { DataContext: AssetEntry entry } && DataContext is AssetPanelViewModel vm) vm.CommitRenameCommand.Execute(entry);
    }
    private void OnRenameTextBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox { DataContext: AssetEntry entry } box || !entry.IsRenaming) return;
        var extensionIndex = entry.IsDirectory ? -1 : entry.Name.LastIndexOf('.');
        var length = extensionIndex > 0 ? extensionIndex : entry.Name.Length;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectionStart = 0;
            box.SelectionEnd = length;
        });
    }
    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Avalonia.Controls.Control { DataContext: AssetEntry entry } || DataContext is not AssetPanelViewModel vm) return;
        if (e.Key == Key.Enter) { vm.CommitRenameCommand.Execute(entry); e.Handled = true; }
        else if (e.Key == Key.Escape) { vm.CancelRenameCommand.Execute(null); e.Handled = true; }
    }
}
