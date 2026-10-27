using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Contracts.Builtins;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Extensibility;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;

namespace GalNet.Editor.Inspector.ViewModels;

public sealed class EntryInspectorViewModel : ObservableObject, IInspectorControlViewModel, IInspectorLockAware, IUndoRedoTarget
{
    public GroupEditorPanelViewModel Page { get; }
    private bool _locked;
    private string? _entryId;
    private readonly AuthorNavigationService? _navigation;
    private readonly ProjectSceneTargets? _sceneTargets;
    private readonly IAssetManager? _assets;
    private CancellationTokenSource? _previewCancellation;
    private EntryEditorItemViewModel? _previewOwner;
    private string? _previewKey;
    private bool _disposed;
    public string? RequestedField { get; private set; }
    public EntryParameterFormBinding? Form { get; private set; }
    public EntryParameterFormBinding? ShaderRawForm { get; private set; }
    public ShaderParameterFormBinding? ShaderForm { get; private set; }
    public bool IsShaderMode { get; private set; }
    public bool ShaderUnavailable => IsShaderMode && ShaderForm is null;
    public Bitmap? ShaderPreview { get; private set; }
    public string? ShaderPreviewError { get; private set; }
    public bool IsShaderPreviewLoading { get; private set; }
    public Task PreviewLoading { get; private set; } = Task.CompletedTask;
    private EntryEditorItemViewModel? _formOwner;
    private string? _formType;
    private string? _shaderProgram;
    private ShaderEffectDescriptor? _shaderDescriptor;
    public EntryEditorItemViewModel? Entry => Page.Workspace.Nodes.FirstOrDefault(node => node.Id == Page.GroupId)?.Entries.FirstOrDefault(entry => entry.StableId == _entryId);
    public EditorRegistry FieldEditors => Page.FieldEditors;
    public bool IsAvailable => Entry is not null;
    public IUndoRedoHistory? UndoRedoHistory => Page.UndoRedoHistory;
    public string TargetDescription => Entry is { } entry ? $"{Page.Workspace.Nodes.FirstOrDefault(node => node.Id == Page.GroupId)?.Name} / {entry.Id} · {entry.Type}" : "";
    public bool HasMultipleSelection => Page.SelectedEntries.Count > 1;
    public int SelectionCount => Page.SelectedEntries.Count;
    public EntryInspectorViewModel(GroupEditorPanelViewModel page, AuthorNavigationService? navigation = null,
        ProjectSceneTargets? sceneTargets = null, IAssetManager? assets = null)
    {
        Page = page;
        _navigation = navigation;
        _sceneTargets = sceneTargets;
        _assets = assets;
        if (_navigation is not null) _navigation.Requested += Navigate;
        if (_sceneTargets is not null) _sceneTargets.Changed += Refresh;
        Page.PropertyChanged += PageChanged;
        Page.SelectedEntries.CollectionChanged += SelectionChanged;
        ((INotifyCollectionChanged)Page.Workspace.Nodes).CollectionChanged += NodesChanged;
        Page.Workspace.UndoRedoHistory!.Changed += Refresh;
        Sync();
    }
    private void PageChanged(object? sender, PropertyChangedEventArgs args)
    { if (!_locked && args.PropertyName == nameof(Page.SelectedEntry)) Sync(); }
    private void NodesChanged(object? sender, NotifyCollectionChangedEventArgs args) => Refresh();
    private void SelectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    { OnPropertyChanged(nameof(HasMultipleSelection)); OnPropertyChanged(nameof(SelectionCount)); }
    private void EntryChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(EntryEditorItemViewModel.Parameters) or nameof(EntryEditorItemViewModel.Type)) Refresh(); }
    private void Sync() { _entryId = Page.SelectedEntry?.StableId; Refresh(); }
    private void Navigate(AuthorNavigationTarget target)
    {
        if (target.NodeId != Page.GroupId || target.EntryId != _entryId) return;
        RequestedField = target.FieldId; OnPropertyChanged(nameof(RequestedField));
    }
    private void Refresh()
    {
        if (_disposed) return;
        var entry = Entry;
        var ownerChanged = !ReferenceEquals(entry, _formOwner);
        var program = entry?.Type == BuiltinEntryIds.ApplyEffect && entry.Source.Parameters.TryGetValue("program", out var selected)
            && selected.ValueKind == System.Text.Json.JsonValueKind.String ? selected.GetString() : null;
        var shaderMode = !string.IsNullOrWhiteSpace(program);
        if (ownerChanged || entry?.Type != _formType || shaderMode != IsShaderMode)
        {
            if (_formOwner is not null) _formOwner.PropertyChanged -= EntryChanged;
            Form?.Dispose(); Form = null; ShaderRawForm?.Dispose(); ShaderRawForm = null;
            _formOwner = entry; _formType = entry?.Type; IsShaderMode = shaderMode;
            if (entry is not null) entry.PropertyChanged += EntryChanged;
            if (entry?.DetailForm is { } form)
            {
                Form = new EntryParameterFormBinding(entry, shaderMode ? form.Fields.Where(field => field.Id is not ("id" or "parameters")).ToArray() : form.Fields, entry.ParameterDtoType);
                if (shaderMode && form.Fields.FirstOrDefault(field => field.Id == "parameters") is { } root)
                    ShaderRawForm = new EntryParameterFormBinding(entry, [root], entry.ParameterDtoType);
            }
            OnPropertyChanged(nameof(Form));
            OnPropertyChanged(nameof(ShaderRawForm));
            OnPropertyChanged(nameof(IsShaderMode));
        }
        var descriptor = shaderMode && _sceneTargets is not null ? _sceneTargets.ProgramDescriptor(program!) : null;
        if (ownerChanged || _shaderProgram != program || !ReferenceEquals(_shaderDescriptor, descriptor))
        {
            ShaderForm?.Dispose(); ShaderForm = null;
            _shaderProgram = program; _shaderDescriptor = descriptor;
            if (entry is not null && descriptor is not null && entry.DetailForm?.Fields.FirstOrDefault(field => field.Id == "parameters") is { } root)
                ShaderForm = new ShaderParameterFormBinding(entry, root, descriptor);
            OnPropertyChanged(nameof(ShaderForm));
            OnPropertyChanged(nameof(ShaderUnavailable));
        }
        Form?.Refresh();
        ShaderRawForm?.Refresh();
        ShaderForm?.Refresh();
        RefreshPreview(entry, program);
        OnPropertyChanged(nameof(Entry)); OnPropertyChanged(nameof(IsAvailable)); OnPropertyChanged(nameof(TargetDescription));
    }
    private void RefreshPreview(EntryEditorItemViewModel? entry, string? program)
    {
        var parameters = entry?.Source.Parameters.GetValueOrDefault("parameters") ?? default;
        var json = parameters.ValueKind == JsonValueKind.Undefined ? "{}" : parameters.GetRawText();
        var key = !string.IsNullOrWhiteSpace(program) && _assets is not null
            ? program + "\n" + json + "\n" + (_sceneTargets?.ResourceEpoch ?? 0) : null;
        if (ReferenceEquals(entry, _previewOwner) && key == _previewKey) return;
        _previewOwner = entry; _previewKey = key;
        _previewCancellation?.Cancel(); _previewCancellation?.Dispose(); _previewCancellation = null;
        var old = ShaderPreview; ShaderPreview = null; old?.Dispose();
        ShaderPreviewError = null; IsShaderPreviewLoading = key is not null;
        OnPropertyChanged(nameof(ShaderPreview)); OnPropertyChanged(nameof(ShaderPreviewError)); OnPropertyChanged(nameof(IsShaderPreviewLoading));
        if (key is null) { PreviewLoading = Task.CompletedTask; return; }
        var cancellation = new CancellationTokenSource(); _previewCancellation = cancellation;
        PreviewLoading = RenderPreviewAsync(program!, json, cancellation);
    }
    private async Task RenderPreviewAsync(string program, string parameters, CancellationTokenSource cancellation)
    {
        try
        {
            var png = await Task.Run(() => ShaderEffectPreviewRenderer.RenderAsync(_assets!, program, parameters, cancellation.Token), cancellation.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || !ReferenceEquals(_previewCancellation, cancellation)) return;
                using var stream = new MemoryStream(png, writable: false);
                ShaderPreview = new Bitmap(stream);
                IsShaderPreviewLoading = false;
                OnPropertyChanged(nameof(ShaderPreview)); OnPropertyChanged(nameof(IsShaderPreviewLoading));
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || !ReferenceEquals(_previewCancellation, cancellation)) return;
                ShaderPreviewError = error.Message;
                IsShaderPreviewLoading = false;
                OnPropertyChanged(nameof(ShaderPreviewError)); OnPropertyChanged(nameof(IsShaderPreviewLoading));
            });
        }
    }
    public void SetLocked(bool isLocked) { _locked = isLocked; if (!_locked) Sync(); }
    public void Dispose()
    {
        _disposed = true;
        _previewCancellation?.Cancel(); _previewCancellation?.Dispose(); _previewCancellation = null;
        ShaderPreview?.Dispose(); ShaderPreview = null;
        Form?.Dispose();
        ShaderRawForm?.Dispose(); ShaderForm?.Dispose();
        if (_formOwner is not null) _formOwner.PropertyChanged -= EntryChanged;
        if (_navigation is not null) _navigation.Requested -= Navigate;
        if (_sceneTargets is not null) _sceneTargets.Changed -= Refresh;
        Page.PropertyChanged -= PageChanged;
        Page.SelectedEntries.CollectionChanged -= SelectionChanged;
        ((INotifyCollectionChanged)Page.Workspace.Nodes).CollectionChanged -= NodesChanged;
        Page.Workspace.UndoRedoHistory!.Changed -= Refresh;
    }
}
