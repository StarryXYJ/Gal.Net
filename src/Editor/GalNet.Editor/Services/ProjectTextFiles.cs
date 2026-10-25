using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Assets;
using GalNet.Editor.Abstraction.Documents;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

/// <summary>Project-owned buffers outlive their Dock views; bytes, paths and saving belong to the content session.</summary>
public sealed class ProjectTextFiles(IProjectService projects, IProjectContentSession content, EditorHistories histories, IEditorLocalizationService localization, IResourceTypeCatalog? resourceTypes = null) : IDisposable
{
    private readonly Dictionary<string, ProjectTextBuffer> _buffers = [];
    public ProjectTextBuffer Get(string id) => _buffers[id];
    public ProjectTextBuffer Open(string path)
    {
        var document = content.FileDocuments.Values.FirstOrDefault(file => file.Exists && ProjectFilePaths.Comparer.Equals(file.Path, path));
        if (document is not null && _buffers.TryGetValue(document.Id, out var buffer)) return buffer;
        var root = projects.Current!.RootPath;
        var full = new ProjectRawReader(root).Resolve(path);
        byte[]? bytes = null;
        Encoding? encoding = null;
        string text = "";
        var editable = true;
        try { ProjectFilePaths.Editable(path); }
        catch (Exception) { editable = false; }
        if (path.StartsWith("Graph/", StringComparison.OrdinalIgnoreCase)) editable = false;
        if (new[] { ".bin", ".dll", ".exe", ".pdb", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".mp3", ".ogg", ".wav", ".mp4", ".ttf", ".otf", ".pak", ".galpak", ".galgroup" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) editable = false;
        var original = document is { ManagedContent: true } ? document.Data
            : File.Exists(full) && new FileInfo(full).Length <= 2 * 1024 * 1024 ? File.ReadAllBytes(full) : null;
        if (original is { Length: <= 2 * 1024 * 1024 })
        {
            if (TryDecode(original, out encoding, out text) && editable) bytes = original;
        }
        if (encoding is null) editable = false;
        document ??= content.TrackFile(path, bytes);
        buffer = new(document, content, histories, localization, encoding, text, root, editable, ValidateVariants);
        _buffers.Add(document.Id, buffer);
        return buffer;
    }
    private void ValidateVariants(string path, string text)
    {
        if (resourceTypes is null || !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return;
        var isVariants = resourceTypes.TryGetByExtension(Path.GetExtension(path), out var type) && type.TypeId == "image-variants";
        var sidecar = new ProjectRawReader(projects.Current!.RootPath).Resolve(path + ".meta");
        if (!isVariants && File.Exists(sidecar)) isVariants = new AssetMetaCodec(resourceTypes).Deserialize(File.ReadAllText(sidecar), path[7..]).TypeId == "image-variants";
        if (!isVariants) return;
        var build = ImageVariantsAuthoring.Scan(projects.Current!.RootPath, path, text, resourceTypes).Build;
        if (build.Definition is null) throw new InvalidDataException(string.Join("\n", build.Diagnostics.Select(item => $"{item.Path} [{item.Code}]: {item.Message}")));
    }
    internal static bool TryDecode(byte[] bytes, out Encoding? encoding, out string text)
    {
        encoding = null; text = "";
        var skip = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, true, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, true, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, true, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, true, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { encoding = new UTF8Encoding(true, true); skip = 3; }
        else encoding = new UTF8Encoding(false, true);
        try
        {
            text = encoding.GetString(bytes, skip, bytes.Length - skip);
            if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'))) return false;
            return encoding.GetPreamble().Concat(encoding.GetBytes(text)).SequenceEqual(bytes);
        }
        catch (DecoderFallbackException) { text = ""; return false; }
    }
    public void Dispose() { foreach (var buffer in _buffers.Values) buffer.Dispose(); _buffers.Clear(); }
}

public sealed partial class ProjectTextBuffer : ObservableObject, IDisposable
{
    private readonly EditorFileDocument _document;
    private readonly IProjectContentSession _content;
    private readonly EditorHistories _histories;
    private readonly IEditorLocalizationService _localization;
    private Encoding? _encoding;
    private readonly string _root;
    private readonly bool _canEdit;
    private readonly IDisposable _draft;
    private readonly Action<string, string>? _validate;
    private byte[] _observed;
    private Exception? _failure;
    private bool _externalChange;
    private bool _disposed;
    public string Id => _document.Id;
    public string Path => _document.Path;
    public string Title => System.IO.Path.GetFileName(Path) + (IsDirty ? " *" : "");
    public bool IsReadOnly => _disposed || !_canEdit || _encoding is null || !_document.ManagedContent || !_document.Exists || _content.RepairRequired;
    public bool IsDirty => _document.Exists && _document.ManagedContent && (HasDraft || !_content.CaptureSaved().Files.TryGetValue(Id, out var saved) || !saved.Data.AsSpan().SequenceEqual(_document.Data));
    private bool HasDraft => !IsReadOnly && Text != Decode(_observed);
    public string Information => !_document.Exists ? _localization["Files.Missing"] : IsReadOnly ? _localization["Files.ReadOnly"] : _encoding!.WebName;
    public long Size { get { try { return new FileInfo(System.IO.Path.Combine(_root, Path)).Length; } catch (IOException) { return 0; } } }
    public bool CanOpenExternal => _document.Exists;
    [ObservableProperty] private string _text;
    [ObservableProperty] private string _error = "";
    public ProjectTextBuffer(EditorFileDocument document, IProjectContentSession content, EditorHistories histories, IEditorLocalizationService localization, Encoding? encoding, string text, string root, bool canEdit = true, Action<string, string>? validate = null)
    {
        _validate = validate;
        _document = document; _content = content; _histories = histories; _localization = localization; _encoding = encoding; _root = root;
        _canEdit = canEdit;
        _text = document.ManagedContent && encoding is not null ? Decode(document.Data) : text; _observed = document.Data.ToArray();
        _draft = content.RegisterFileDraft(document.Id, () => HasDraft, CommitAsync, ValidateStructuredDraft, Cancel);
        content.Changed += OnChanged; localization.PropertyChanged += OnLocale;
        content.ExternalReloaded += OnExternalReload;
    }
    private string Decode(byte[] bytes) => bytes.Length < _encoding!.GetPreamble().Length ? "" : _encoding.GetString(bytes, _encoding.GetPreamble().Length, bytes.Length - _encoding.GetPreamble().Length);
    private void OnExternalReload()
    {
        _failure = null; _externalChange = false; Error = "";
        var full = new ProjectRawReader(_root).Resolve(Path);
        if (!_document.ManagedContent) _document.Exists = File.Exists(full);
        var bytes = _document.ManagedContent ? _document.Data : _document.Exists && new FileInfo(full).Length <= 2 * 1024 * 1024 ? File.ReadAllBytes(full) : [];
        if (!ProjectTextFiles.TryDecode(bytes, out _encoding, out var text)) { _encoding = null; text = ""; }
        _observed = _document.Data.ToArray(); Text = text;
        OnChanged();
    }
    partial void OnTextChanged(string value) { if (!_disposed) _content.Refresh(); OnPropertyChanged(nameof(Title)); }
    private void OnLocale(object? sender, PropertyChangedEventArgs args)
    {
        if (_failure is not null) Error = EditorText.Error(_failure).Render(_localization);
        else if (_externalChange) Error = _localization["Files.ExternalChange"];
        OnChanged();
    }
    private void OnChanged()
    {
        if (_document.ManagedContent && _encoding is not null && !_observed.AsSpan().SequenceEqual(_document.Data))
        {
            if (HasDraft && Text != Decode(_document.Data))
            {
                _failure = ProjectFilePaths.Error("changed", Path);
                Error = EditorText.Error(_failure).Render(_localization);
            }
            else { _observed = _document.Data.ToArray(); Text = Decode(_document.Data); }
        }
        OnPropertyChanged(nameof(Path)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(IsReadOnly)); OnPropertyChanged(nameof(Information)); OnPropertyChanged(nameof(Size)); OnPropertyChanged(nameof(CanOpenExternal));
    }
    public async Task CommitAsync()
    {
        if (!HasDraft) return;
        try
        {
            _validate?.Invoke(Path, Text);
            if (!_document.Data.AsSpan().SequenceEqual(_observed)) throw ProjectFilePaths.Error("changed", Path);
            var next = _encoding!.GetPreamble().Concat(_encoding.GetBytes(Text)).ToArray();
            await _histories.Content.ExecuteAsync(new TextEdit(_document, _observed.ToArray(), next));
            _failure = null; Error = _externalChange ? _localization["Files.ExternalChange"] : "";
        }
        catch (Exception error) { _failure = error; Error = EditorText.Error(error).Render(_localization); throw; }
    }
    [RelayCommand] private async Task ApplyAsync() { try { await CommitAsync(); } catch (Exception) { } }
    [RelayCommand] private async Task SaveAsync() { try { await _content.SaveAsync(); _failure = null; Error = ""; } catch (Exception error) { _failure = error; Error = EditorText.Error(error).Render(_localization); } }
    private void ValidateStructuredDraft()
    {
        _validate?.Invoke(Path, Text);
        if (Path.StartsWith("I18n/", StringComparison.Ordinal) || Path == LocalizationJson.MetadataPath)
            LocalizationJson.Validate(Path, _encoding!.GetPreamble().Concat(_encoding.GetBytes(Text)).ToArray());
        if (!_document.Data.AsSpan().SequenceEqual(_observed)) throw ProjectFilePaths.Error("changed", Path);
    }
    [RelayCommand]
    private void Cancel()
    {
        if (!_document.ManagedContent || _encoding is null) return;
        _observed = _document.Data.ToArray(); _failure = null; Text = Decode(_document.Data);
        Error = _externalChange ? _localization["Files.ExternalChange"] : "";
    }
    [RelayCommand] private void OpenExternal() { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(_root, Path)) { UseShellExecute = true }); } catch (Exception error) { _failure = error; Error = EditorText.Error(error).Render(_localization); } }
    public void CheckExternalChange()
    {
        if (!_document.ManagedContent || !_document.Exists) return;
        var saved = _content.CaptureSaved().Files[Id];
        try
        {
            var full = new ProjectRawReader(_root).Resolve(Path);
            _externalChange = !File.Exists(full) || !File.ReadAllBytes(full).AsSpan().SequenceEqual(saved.Data);
            if (_failure is null) Error = _externalChange ? _localization["Files.ExternalChange"] : "";
        }
        catch (IOException) { Error = _localization["Files.ExternalChange"]; }
    }
    private sealed record TextEdit(EditorFileDocument Document, byte[] Before, byte[] After) : IUndoableEdit
    {
        public string Description => "Edit file";
        public EditorText DisplayText => new("History.TextFile", Document.Path);
        public long RetainedBytes => Before.LongLength + After.LongLength;
        private void Apply(byte[] expected, byte[] next)
        {
            if (!Document.Exists || !Document.Data.AsSpan().SequenceEqual(expected)) throw ProjectFilePaths.Error("changed", Document.Path);
            Document.Data = next.ToArray();
        }
        public void Redo() => Apply(Before, After);
        public void Undo() => Apply(After, Before);
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _content.Changed -= OnChanged; _content.ExternalReloaded -= OnExternalReload; _localization.PropertyChanged -= OnLocale; _draft.Dispose(); OnPropertyChanged(nameof(IsReadOnly)); }
}
