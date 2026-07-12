using System.Collections.Generic;
using System.ComponentModel;
using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Variable;
using GalNet.Editor.Services.Interfaces;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace GalNet.Editor.ViewModels;

public sealed partial class NodeInspectorPanelViewModel : ObservableObject, IDisposable
{
    private readonly IAssetCatalogService _assets;
    private bool _loadingAssetSettings;
    private LibVLC? _previewVlc;
    private MediaPlayer? _audioPreview;
    private bool _updatingAudioPosition;
    public EditorWorkspaceViewModel Workspace { get; }

    public bool IsNodeInspectorVisible => Workspace.InspectorMode == InspectorMode.Node;
    public bool IsPreviewVariablesVisible => Workspace.InspectorMode == InspectorMode.PreviewVariables;
    public bool IsAssetInspectorVisible => Workspace.InspectorMode == InspectorMode.Asset;
    public bool IsSelectedAssetImage => Workspace.SelectedAsset?.IsImage == true;
    public bool IsSelectedAssetAudio => Workspace.SelectedAsset?.IsAudio == true;
    public bool IsSelectedAssetVideo => Workspace.SelectedAsset?.IsVideo == true;
    public bool HasSelectedNode => Workspace.SelectedNode is not null;
    public bool IsLinearGroupSelected => Workspace.SelectedNode?.NodeKind == GraphNodeKind.LinearGroup;
    public bool IsChoiceBranchSelected => Workspace.SelectedNode?.NodeKind == GraphNodeKind.ChoiceBranch;
    public bool IsConditionBranchSelected => Workspace.SelectedNode?.NodeKind == GraphNodeKind.ConditionBranch;
    public IReadOnlyList<ConditionVariableSuggestion> ConditionSuggestions => Workspace.GetConditionVariableSuggestions();
    public IReadOnlyList<ProjectVariableDefinition> ValidationVariables => Workspace.AllProjectVariableDefinitions;
    public IReadOnlyList<string> FilterOptions { get; } = ["point", "bilinear"];
    public IReadOnlyList<string> CompressionOptions { get; } = ["none", "deflate", "gzip", "brotli"];

    [ObservableProperty] private string _assetFilter = "bilinear";
    [ObservableProperty] private string _assetCompression = "none";
    [ObservableProperty] private bool _isAudioPreviewPlaying;
    [ObservableProperty] private double _audioPreviewPosition;
    [ObservableProperty] private double _audioPreviewDuration = 1;
    [ObservableProperty] private string _audioPreviewError = "";

    public NodeInspectorPanelViewModel(EditorWorkspaceViewModel workspace, IAssetCatalogService assets)
    {
        Workspace = workspace;
        _assets = assets;
        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
        Workspace.VariableDefinitionsChanged += OnVariableDefinitionsChanged;
    }

    partial void OnAssetFilterChanged(string value) => SaveAssetSettings();
    partial void OnAssetCompressionChanged(string value) => SaveAssetSettings();

    private void LoadAssetSettings()
    {
        _loadingAssetSettings = true;
        AssetFilter = Workspace.SelectedAsset?.Filter ?? "bilinear";
        AssetCompression = Workspace.SelectedAsset?.Compress ?? "none";
        _loadingAssetSettings = false;
    }
    private void SaveAssetSettings()
    {
        if (_loadingAssetSettings || Workspace.SelectedAsset is not { IsDirectory: false } asset) return;
        _ = _assets.UpdateMetaAsync(asset, AssetFilter, AssetCompression);
    }

    [RelayCommand]
    private void ToggleAudioPreview()
    {
        if (Workspace.SelectedAsset is not { IsAudio: true } asset) return;
        try
        {
            if (_audioPreview is null)
            {
                LibVLCSharp.Shared.Core.Initialize();
                _previewVlc = new LibVLC();
                _audioPreview = new MediaPlayer(_previewVlc);
                _audioPreview.TimeChanged += (_, e) => Dispatcher.UIThread.Post(() =>
                {
                    _updatingAudioPosition = true; AudioPreviewPosition = e.Time; _updatingAudioPosition = false;
                });
                _audioPreview.LengthChanged += (_, e) => Dispatcher.UIThread.Post(() => AudioPreviewDuration = Math.Max(1, e.Length));
                _audioPreview.EndReached += (_, _) => Dispatcher.UIThread.Post(() => IsAudioPreviewPlaying = false);
            }
            if (IsAudioPreviewPlaying) { _audioPreview.Pause(); IsAudioPreviewPlaying = false; return; }
            _audioPreview.Media = new Media(_previewVlc!, asset.FullPath);
            _audioPreview.Play(); IsAudioPreviewPlaying = true; AudioPreviewError = "";
        }
        catch (Exception ex) { AudioPreviewError = ex.Message; IsAudioPreviewPlaying = false; }
    }

    partial void OnAudioPreviewPositionChanged(double value)
    {
        if (!_updatingAudioPosition && _audioPreview is not null && IsAudioPreviewPlaying) _audioPreview.Time = (long)value;
    }

    public void Dispose()
    {
        _audioPreview?.Stop(); _audioPreview?.Dispose(); _previewVlc?.Dispose();
    }

    [RelayCommand]
    private void OpenGroupEditor()
    {
        if (Workspace.SelectedNode is not null)
            Workspace.OpenGroupEditor(Workspace.SelectedNode);
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorWorkspaceViewModel.InspectorMode)
            or nameof(EditorWorkspaceViewModel.SelectedNode)
            or nameof(EditorWorkspaceViewModel.SelectedEdge)
            or nameof(EditorWorkspaceViewModel.HasMultipleNodeSelection)
            or nameof(EditorWorkspaceViewModel.ActivePreview)
            or nameof(EditorWorkspaceViewModel.SelectedAsset))
        {
            OnPropertyChanged(nameof(IsNodeInspectorVisible));
            OnPropertyChanged(nameof(IsPreviewVariablesVisible));
            OnPropertyChanged(nameof(IsAssetInspectorVisible));
            OnPropertyChanged(nameof(IsSelectedAssetImage));
            OnPropertyChanged(nameof(IsSelectedAssetAudio));
            OnPropertyChanged(nameof(IsSelectedAssetVideo));
            if (e.PropertyName == nameof(EditorWorkspaceViewModel.SelectedAsset)) LoadAssetSettings();
            OnPropertyChanged(nameof(HasSelectedNode));
            OnPropertyChanged(nameof(IsLinearGroupSelected));
            OnPropertyChanged(nameof(IsChoiceBranchSelected));
            OnPropertyChanged(nameof(IsConditionBranchSelected));
            OnPropertyChanged(nameof(ConditionSuggestions));
            OnPropertyChanged(nameof(ValidationVariables));
        }
    }

    private void OnVariableDefinitionsChanged()
    {
        OnPropertyChanged(nameof(ConditionSuggestions));
        OnPropertyChanged(nameof(ValidationVariables));
    }
}
