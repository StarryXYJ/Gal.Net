using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using GalNet.Editor.Models;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class AssetPanelView : UserControl
{
    private static readonly DataFormat<string> InternalFiles = DataFormat.CreateStringApplicationFormat("galnet.project-files");
    private (PointerPressedEventArgs Args, Point Position, string[] Paths)? _pending;
    private sealed record Payload(string Root, string[] Paths);
    public AssetPanelView() => InitializeComponent();
    private void OnEntryDoubleTapped(object? sender, TappedEventArgs args) { if (sender is Control { DataContext: ProjectFileItem item } && DataContext is AssetPanelViewModel vm) vm.OpenCommand.Execute(item); }
    private void OnAddressPressed(object? sender, PointerPressedEventArgs args)
    {
        if (DataContext is not AssetPanelViewModel vm || vm.IsEditingPath || !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (args.Source is Visual source && source.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
        vm.PathText = vm.CurrentDirectory; vm.IsEditingPath = true;
        var box = this.FindControl<TextBox>("PathInput")!; box.Focus(); box.SelectAll(); args.Handled = true;
    }
    private void OnPathKeyDown(object? sender, KeyEventArgs args)
    {
        if (DataContext is not AssetPanelViewModel vm) return;
        if (args.Key == Key.Enter) { vm.NavigatePathCommand.Execute((sender as TextBox)?.Text); vm.IsEditingPath = false; args.Handled = true; }
        else if (args.Key == Key.Escape) { vm.PathText = vm.CurrentDirectory; vm.IsEditingPath = false; args.Handled = true; }
    }
    private void OnPathLostFocus(object? sender, RoutedEventArgs args) { if (DataContext is AssetPanelViewModel vm) { vm.PathText = vm.CurrentDirectory; vm.IsEditingPath = false; } }
    private void OnEntryPressed(object? sender, PointerPressedEventArgs args)
    {
        if (sender is not Control { DataContext: ProjectFileItem item } control || DataContext is not AssetPanelViewModel vm) return;
        var properties = args.GetCurrentPoint(control).Properties;
        if (properties.IsRightButtonPressed) vm.Select(item);
        if (!properties.IsLeftButtonPressed || !item.CanEdit) return;
        var paths = vm.SelectedEntries.Contains(item) ? vm.SelectedEntries.Select(entry => entry.Path).ToArray() : [item.Path];
        _pending = (args, args.GetPosition(this), paths);
    }
    private async void OnEntryMoved(object? sender, PointerEventArgs args)
    {
        if (_pending is not { } pending || DataContext is not AssetPanelViewModel vm) return;
        if (!args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _pending = null; return; }
        var position = args.GetPosition(this);
        if (Math.Abs(position.X - pending.Position.X) < 6 && Math.Abs(position.Y - pending.Position.Y) < 6) return;
        _pending = null;
        var transfer = new DataTransfer(); transfer.Add(DataTransferItem.Create(InternalFiles, JsonSerializer.Serialize(new Payload(vm.ProjectRoot, pending.Paths))));
        await DragDrop.DoDragDropAsync(pending.Args, transfer, DragDropEffects.Copy | DragDropEffects.Move);
    }
    private string[]? ReadInternal(DragEventArgs args)
    {
        if (DataContext is not AssetPanelViewModel vm || !args.DataTransfer.Formats.Contains(InternalFiles)) return null;
        try { var payload = JsonSerializer.Deserialize<Payload>(args.DataTransfer.TryGetValue(InternalFiles)!); return payload?.Root == vm.ProjectRoot ? payload.Paths : []; } catch (JsonException) { return []; }
    }
    private static string? Target(Control? control) => control?.DataContext switch { ProjectDirectoryNode node => node.Path, FileBreadcrumb crumb => crumb.Path, ProjectFileItem { IsDirectory: true } item => item.Path, _ => null };
    private void SetEffects(DragEventArgs args, string target)
    {
        if (DataContext is not AssetPanelViewModel vm) return;
        var copy = args.KeyModifiers.HasFlag(KeyModifiers.Control);
        var paths = ReadInternal(args);
        args.DragEffects = paths is not null ? paths.Length > 0 && vm.CanDrop(paths, target, copy) ? copy ? DragDropEffects.Copy : DragDropEffects.Move : DragDropEffects.None
            : vm.CanImportInto(target) && args.DataTransfer.TryGetFiles()?.Any() == true ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }
    private void OnGridDragOver(object? sender, DragEventArgs args) { if (DataContext is AssetPanelViewModel vm) SetEffects(args, vm.CurrentDirectory); }
    private void OnTargetDragOver(object? sender, DragEventArgs args) { if (Target(sender as Control) is { } target) { SetEffects(args, target); (sender as Control)?.Classes.Set("drop-target", args.DragEffects != DragDropEffects.None); } }
    private void OnTargetDragLeave(object? sender, RoutedEventArgs args) => (sender as Control)?.Classes.Set("drop-target", false);
    private async Task DropAsync(DragEventArgs args, string target)
    {
        if (DataContext is not AssetPanelViewModel vm) return;
        SetEffects(args, target); if (args.DragEffects == DragDropEffects.None) return;
        if (ReadInternal(args) is { Length: > 0 } paths) await vm.DropAsync(paths, target, args.KeyModifiers.HasFlag(KeyModifiers.Control));
        else { var external = args.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray() ?? []; if (external.Length > 0) await vm.ImportExternalAsync(external, target); }
    }
    private async void OnGridDrop(object? sender, DragEventArgs args) { if (DataContext is AssetPanelViewModel vm) await DropAsync(args, vm.CurrentDirectory); }
    private async void OnTargetDrop(object? sender, DragEventArgs args) { if (Target(sender as Control) is { } target) await DropAsync(args, target); (sender as Control)?.Classes.Set("drop-target", false); }
    private void FocusRename(TextBox box)
    {
        if (!box.IsVisible || box.DataContext is not ProjectFileItem { IsRenaming: true } item) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!box.IsVisible) return; box.Focus(); box.SelectionStart = 0; var extension = item.IsDirectory ? "" : Path.GetExtension(item.Name); box.SelectionEnd = item.Name.Length - extension.Length; });
    }
    private void OnRenameAttached(object? sender, VisualTreeAttachmentEventArgs args) { if (sender is TextBox box) FocusRename(box); }
    private void OnRenamePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args) { if (args.Property == IsVisibleProperty && sender is TextBox box) FocusRename(box); }
    private async void OnRenameKeyDown(object? sender, KeyEventArgs args)
    {
        if (DataContext is not AssetPanelViewModel vm) return;
        if (args.Key == Key.Enter) { args.Handled = true; await vm.CommitRenameAsync(); }
        else if (args.Key == Key.Escape) { args.Handled = true; vm.CancelRenameCommand.Execute(null); }
    }
    private async void OnRenameLostFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is Control { DataContext: ProjectFileItem { IsRenaming: true } item } && DataContext is AssetPanelViewModel vm && ReferenceEquals(item, vm.RenamingEntry)) await vm.CommitRenameAsync();
    }
    private async void OnBrowserKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.Source is TextBox || DataContext is not AssetPanelViewModel vm) return;
        var control = args.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (args.KeyModifiers.HasFlag(KeyModifiers.Alt) && args.Key == Key.Left) vm.BackCommand.Execute(null);
        else if (args.KeyModifiers.HasFlag(KeyModifiers.Alt) && args.Key == Key.Right) vm.ForwardCommand.Execute(null);
        else if (control && args.Key == Key.F) this.FindControl<TextBox>("SearchInput")!.Focus();
        else if (control && args.Key == Key.A) this.FindControl<ListBox>("FileGrid")!.SelectAll();
        else if (control && args.Key == Key.C) vm.CopySelectedCommand.Execute(null);
        else if (control && args.Key == Key.X) vm.CutSelectedCommand.Execute(null);
        else if (control && args.Key == Key.V) await vm.PasteCommand.ExecuteAsync(null);
        else if (control && args.Key == Key.D) await vm.DuplicateCommand.ExecuteAsync(null);
        else if (args.Key == Key.F2 && vm.CanRename) vm.BeginRenameCommand.Execute(vm.SelectedEntry);
        else if (args.Key == Key.Delete) await vm.DeleteSelectedCommand.ExecuteAsync(null);
        else if (args.Key == Key.Back) vm.UpCommand.Execute(null);
        else if (args.Key == Key.Enter) vm.OpenCommand.Execute(vm.SelectedEntry);
        else return;
        args.Handled = true;
    }
}
