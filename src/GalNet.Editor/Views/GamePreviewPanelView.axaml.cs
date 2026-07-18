using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Control.Screen.Host;
using GalNet.Editor.ViewModels;
using Serilog;
using Ursa.Controls;

namespace GalNet.Editor.Views;

public partial class GamePreviewPanelView : UserControl
{
    private int _attachCount;
    private bool _layoutLoggedForThisAttach;
    private GamePreviewPanelViewModel? _vm;

    public GamePreviewPanelView()
    {
        Log.Information("[PreviewView] .ctor thread={ThreadId}", Environment.CurrentManagedThreadId);
        InitializeComponent();
        Log.Information("[PreviewView] InitializeComponent done");

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        _attachCount++;
        _layoutLoggedForThisAttach = false;

        Log.Information("[PreviewView] AttachedToVisualTree #{Count}, Root={Root}, Bounds={Bounds}",
            _attachCount, e.RootVisual?.GetType().Name ?? "null", Bounds);

        LayoutUpdated += OnLayoutUpdated;
        SyncPageHost();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        Log.Information("[PreviewView] DetachedFromVisualTree #{Count}, Root={Root}",
            _attachCount, e.RootVisual?.GetType().Name ?? "null");
        LayoutUpdated -= OnLayoutUpdated;
        if (_vm is not null)
            _ = _vm.DisposeAsync();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (_layoutLoggedForThisAttach) return;
        _layoutLoggedForThisAttach = true;

        Log.Information("[PreviewView] First LayoutUpdated (attach #{Count}): Bounds={Bounds}, Desired={Desired}",
            _attachCount, Bounds, DesiredSize);

        Log.Information("[PreviewView] GameViewHost: Bounds={B}, Content={C}",
            GameViewHost.Bounds, GameViewHost.Content?.GetType().Name ?? "null");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.NameConflictRequested -= OnNameConflictRequested;
        }

        _vm = DataContext as GamePreviewPanelViewModel;
        Log.Information("[PreviewView] VM setup: {Type}, PageHostVm={PH}",
            _vm?.GetType().Name ?? "null",
            _vm?.PageHostVm?.GetType().Name ?? "null");

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
        if (e.PropertyName == nameof(GamePreviewPanelViewModel.PageHostVm))
        {
            Log.Information("[PreviewView] PageHostVm prop changed: {V}",
                _vm?.PageHostVm?.GetType().Name ?? "null");
            SyncPageHost();
        }
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
        if (_vm?.PageHostVm is null) return;

        if (GameViewHost.GameContent is GamePageHostView existing
            && ReferenceEquals(existing.DataContext, _vm.PageHostVm))
            return;

        Log.Information("[PreviewView] SyncPageHost: creating GamePageHostView");
        var hostView = new GamePageHostView
        {
            DataContext = _vm.PageHostVm,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        GameViewHost.GameContent = hostView;
    }
}
