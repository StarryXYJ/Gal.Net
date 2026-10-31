using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Services;

namespace GalNet.Editor.Inspector.ViewModels;

public sealed partial class ResourceMetadataEditorViewModel : ObservableObject, IDisposable
{
    private readonly ResourceMetadataAuthoringService _service;
    private ResourceMetadataDraft _expected;
    private readonly IDisposable _draft;
    private bool _disposed;
    private Task? _submission;
    public bool IsGroup => _expected.Group;
    public System.Collections.ObjectModel.ObservableCollection<ResourceCoverOption> CoverOptions { get; } = [];
    [ObservableProperty] private ResourceCoverOption? _selectedCover;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _unlockId = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _busy;
    public bool IsDirty => Name != _expected.Name || UnlockId != Format(_expected.UnlockId) || (IsGroup && SelectedCover?.Id != _expected.PreviewResourceId);
    private static string Format(int? id) => id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public ResourceMetadataEditorViewModel(ResourceMetadataAuthoringService service, string owner, bool group)
    {
        _service = service; _expected = service.Capture(owner, group); RefreshCovers(); Reset();
        _draft = service.Content.RegisterDraft(() => IsDirty, CommitAsync, Reset);
        service.Content.Changed += OnChanged;
    }
    private void RefreshCovers()
    {
        if (!IsGroup) return;
        CoverOptions.Clear(); foreach (var cover in _service.Covers()) CoverOptions.Add(cover);
        if (_expected.PreviewResourceId is { } id && !CoverOptions.Any(option => option.Id == id)) CoverOptions.Add(new(id, _service.Text.Format("Resource.MissingCover", id)));
    }
    private void Reset() { Name = _expected.Name; UnlockId = Format(_expected.UnlockId); SelectedCover = CoverOptions.FirstOrDefault(option => option.Id == _expected.PreviewResourceId); Error = ""; }
    private void OnChanged()
    {
        if (_disposed || Busy || IsDirty) return;
        try { _expected = _service.Capture(_expected.Owner, _expected.Group); RefreshCovers(); Reset(); }
        catch (Exception error) { Error = error.Message; }
    }
    public Task CommitAsync() => _submission is { IsCompleted: false } ? _submission : _submission = CommitCoreAsync();
    private async Task CommitCoreAsync()
    {
        if (_disposed || !IsDirty) return;
        Busy = true; Error = "";
        try
        {
            int? id = null;
            if (!string.IsNullOrWhiteSpace(UnlockId))
            {
                if (!int.TryParse(UnlockId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed <= 0) throw new InvalidDataException(_service.Text["Resource.InvalidGalleryId"]);
                id = parsed;
            }
            await _service.SetAsync(_expected, Name, id, IsGroup ? SelectedCover?.Id ?? "" : null);
            _expected = _service.Capture(_expected.Owner, _expected.Group); RefreshCovers(); Reset();
        }
        catch (Exception error) { Error = error.Message; throw; }
        finally { Busy = false; }
    }
    [RelayCommand] private async Task TranslateAsync()
    {
        try { await CommitAsync(); _service.OpenTranslations(); }
        catch (Exception error) { Error = error.Message; }
    }
    public void Dispose() { _disposed = true; _service.Content.Changed -= OnChanged; _draft.Dispose(); }
}
