using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.ViewModels;
using LibVLCSharp.Shared;

namespace GalNet.Editor.Inspector.ViewModels;
public sealed partial class AssetInspectorControlViewModel : ObservableObject, IInspectorControlViewModel
{
    private readonly IAssetCatalogService _assets; private readonly IEditorLocalizationService _localization; private bool _loading; private LibVLC? _vlc; private MediaPlayer? _player; private bool _updating;
    public EditorWorkspaceViewModel Workspace { get; }
    public bool IsAvailable => true;
    public bool HasSelectedAsset => Workspace.SelectedAsset is not null;
    public bool IsSelectedAssetImage => Workspace.SelectedAsset?.IsImage == true;
    public bool IsSelectedAssetAudio => Workspace.SelectedAsset?.IsAudio == true;
    public bool IsSelectedAssetVideo => Workspace.SelectedAsset?.IsVideo == true;
    public IReadOnlyList<string> FilterOptions { get; } = ["point", "bilinear"];
    public IReadOnlyList<string> CompressionOptions { get; } = ["none", "deflate", "gzip", "brotli"];
    public string AudioPreviewActionText => _localization[IsAudioPreviewPlaying ? "Inspector.Asset.Pause" : "Inspector.Asset.Play"];
    [ObservableProperty] private string _assetFilter = "bilinear"; [ObservableProperty] private string _assetCompression = "none";
    [ObservableProperty] private bool _isAudioPreviewPlaying; [ObservableProperty] private double _audioPreviewPosition; [ObservableProperty] private double _audioPreviewDuration = 1; [ObservableProperty] private string _audioPreviewError = "";
    public AssetInspectorControlViewModel(EditorWorkspaceViewModel workspace, IAssetCatalogService assets, IEditorLocalizationService localization) { Workspace = workspace; _assets = assets; _localization = localization; Workspace.PropertyChanged += OnWorkspacePropertyChanged; _localization.PropertyChanged += OnLocalizationPropertyChanged; Load(); }
    partial void OnAssetFilterChanged(string value) => Save(); partial void OnAssetCompressionChanged(string value) => Save();
    private void Load() { _loading = true; AssetFilter = Workspace.SelectedAsset?.Filter ?? "bilinear"; AssetCompression = Workspace.SelectedAsset?.Compress ?? "none"; _loading = false; }
    private void Save() { if (!_loading && Workspace.SelectedAsset is { IsDirectory: false } asset) _ = _assets.UpdateMetaAsync(asset, AssetFilter, AssetCompression); }
    [RelayCommand] private void ToggleAudioPreview() { if (Workspace.SelectedAsset is not { IsAudio: true } asset) return; try { if (_player is null) { LibVLCSharp.Shared.Core.Initialize(); _vlc = new LibVLC(); _player = new MediaPlayer(_vlc); _player.TimeChanged += (_, e) => Dispatcher.UIThread.Post(() => { _updating = true; AudioPreviewPosition = e.Time; _updating = false; }); _player.LengthChanged += (_, e) => Dispatcher.UIThread.Post(() => AudioPreviewDuration = Math.Max(1, e.Length)); _player.EndReached += (_, _) => Dispatcher.UIThread.Post(() => IsAudioPreviewPlaying = false); } if (IsAudioPreviewPlaying) { _player.Pause(); IsAudioPreviewPlaying = false; return; } _player.Media = new Media(_vlc!, asset.FullPath); _player.Play(); IsAudioPreviewPlaying = true; AudioPreviewError = ""; } catch (Exception ex) { AudioPreviewError = ex.Message; IsAudioPreviewPlaying = false; } }
    partial void OnAudioPreviewPositionChanged(double value) { if (!_updating && _player is not null && IsAudioPreviewPlaying) _player.Time = (long)value; }
    partial void OnIsAudioPreviewPlayingChanged(bool value) => OnPropertyChanged(nameof(AudioPreviewActionText));
    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(EditorWorkspaceViewModel.SelectedAsset)) { Load(); OnPropertyChanged(nameof(HasSelectedAsset)); OnPropertyChanged(nameof(IsSelectedAssetImage)); OnPropertyChanged(nameof(IsSelectedAssetAudio)); OnPropertyChanged(nameof(IsSelectedAssetVideo)); } }
    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(IEditorLocalizationService.CurrentCulture) or "Item[]") OnPropertyChanged(nameof(AudioPreviewActionText)); }
    public void Dispose() { Workspace.PropertyChanged -= OnWorkspacePropertyChanged; _localization.PropertyChanged -= OnLocalizationPropertyChanged; _player?.Stop(); _player?.Dispose(); _vlc?.Dispose(); }
}
