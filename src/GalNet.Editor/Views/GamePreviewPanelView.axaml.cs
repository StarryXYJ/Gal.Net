using System;
using Avalonia;
using Avalonia.Controls;
using Dock.Model.Core;
using GalNet.Control.ViewModels;
using GalNet.Control.Views;
using GalNet.Editor.ViewModels;
using Serilog;

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
        var dc = DataContext;
        Log.Information("[PreviewView] OnDataContextChanged DC={DCType}",
            dc?.GetType().Name ?? "null");

        // Dock passes Document as DataContext — override with the real ViewModel
        if (dc is IDockable dockable && dockable.Context is GamePreviewPanelViewModel vm)
        {
            Log.Information("[PreviewView] Overriding DataContext: Document -> GamePreviewPanelViewModel");
            DataContext = vm;
            return;
        }

        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = dc as GamePreviewPanelViewModel;
        Log.Information("[PreviewView] VM setup: {Type}, PageHostVm={PH}",
            _vm?.GetType().Name ?? "null",
            _vm?.PageHostVm?.GetType().Name ?? "null");

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            SyncPageHost();
        }

        base.OnDataContextChanged(e);
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

    private void SyncPageHost()
    {
        if (_vm?.PageHostVm is null) return;

        // Avoid re-creating if already showing the same host
        if (GameViewHost.Content is GamePageHostView existing
            && ReferenceEquals(existing.DataContext, _vm.PageHostVm))
            return;

        Log.Information("[PreviewView] SyncPageHost: creating GamePageHostView");
        var hostView = new GamePageHostView
        {
            DataContext = _vm.PageHostVm,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
        };
        GameViewHost.Content = hostView;
    }
}
