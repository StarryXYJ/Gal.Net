using System.Text.Json;
using GalNet.Core.Runtime;
using GalNet.Core.Serialization;
using GalNet.Runtime.Persistence;

namespace GalNet.Storage.FileSystem;

/// <summary>File-backed slot and quick-save store.</summary>
public sealed class FileSaveService : ISaveService, ISaveSlotNamingService, ISaveSlotInfoSource, IDisposable
{
    private const int FormatVersion = GameSnapshot.CurrentFormatVersion;
    private static readonly JsonSerializerOptions JsonOptions = JsonMetadata.CreateOptions(resolver: StorageJsonContext.Default);
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int MaxSlots { get; }

    public FileSaveService(string profileDirectory, int maxSlots = 60)
    {
        _root = Path.Combine(profileDirectory, "saves");
        MaxSlots = Math.Max(1, maxSlots);
    }

    public async Task<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(CancellationToken ct = default)
    {
        var result = new List<SaveSlotInfo>(MaxSlots);
        for (var i = 0; i < MaxSlots; i++) result.Add(await GetInfoAsync(i, false, ct));
        return result;
    }

    public Task SaveAsync(int slot, SaveRequest request, CancellationToken ct = default) => WriteAsync(slot, false, request, ct);

    public Task<SaveSlotInfo> GetSlotInfoAsync(int slot, CancellationToken ct = default)
    {
        if (slot < 0 || slot >= MaxSlots) throw new ArgumentOutOfRangeException(nameof(slot));
        ct.ThrowIfCancellationRequested();
        return GetInfoAsync(slot, false, ct);
    }

    public async Task RenameAsync(int slot, string name, CancellationToken ct = default)
    {
        if (slot < 0 || slot >= MaxSlots) throw new ArgumentOutOfRangeException(nameof(slot));
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Trim();
        if (normalized.Length > 120 || normalized.Any(char.IsControl))
            throw new ArgumentException("Save names must contain at most 120 characters and no control characters.", nameof(name));
        await _gate.WaitAsync(ct);
        try
        {
            var path = GetPath(slot, false);
            var stored = JsonCodec.Deserialize<StoredSave>(await File.ReadAllTextAsync(path, ct), JsonOptions);
            if (stored?.Version != FormatVersion || stored.Snapshot is null)
                throw new InvalidDataException("Only valid saves can be renamed.");
            stored.Description = normalized.Length == 0 ? null : normalized;
            await FileReplacement.WriteTextAsync(path, JsonCodec.Serialize(stored, JsonOptions), ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<GameSnapshot?> LoadAsync(int slot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (slot < 0 || slot >= MaxSlots) return null;
        return await ReadAsync(GetPath(slot, false), ct);
    }

    public async Task DeleteAsync(int slot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (slot < 0 || slot >= MaxSlots) return;
        await _gate.WaitAsync(ct);
        try { DeleteFiles(slot, false); } finally { _gate.Release(); }
    }

    public Task QuickSaveAsync(SaveRequest request, CancellationToken ct = default) => WriteAsync(-1, true, request, ct);
    public Task<GameSnapshot?> QuickLoadAsync(CancellationToken ct = default) => ReadAsync(GetPath(-1, true), ct);
    public async Task<bool> HasQuickSaveAsync(CancellationToken ct = default) => (await GetQuickSaveInfoAsync(ct)) is { IsCorrupt: false };

    public async Task<SaveSlotInfo?> GetQuickSaveInfoAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = GetPath(-1, true);
        return File.Exists(path) ? await GetInfoAsync(-1, true, ct) : null;
    }

    public async Task DeleteQuickSaveAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { DeleteFiles(-1, true); } finally { _gate.Release(); }
    }

    /// <summary>Removes every normal slot and the quick-save for a fresh player profile.</summary>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            for (var slot = 0; slot < MaxSlots; slot++)
                DeleteFiles(slot, false);
            DeleteFiles(-1, true);
        }
        finally { _gate.Release(); }
    }

    private async Task WriteAsync(int slot, bool quick, SaveRequest request, CancellationToken ct)
    {
        if (!quick && (slot < 0 || slot >= MaxSlots)) throw new ArgumentOutOfRangeException(nameof(slot));
        await _gate.WaitAsync(ct);
        string? stagedPreview = null;
        var committed = false;
        try
        {
            var path = GetPath(slot, quick);
            var previousPreview = await ReadPreviewPathAsync(path, slot, quick, ct);
            var previewName = "";
            if (request.PreviewImage is { Length: > 0 })
            {
                Directory.CreateDirectory(_root);
                previewName = $"{(quick ? "quick" : $"slot-{slot:D3}")}-{Guid.NewGuid():N}.png";
                stagedPreview = Path.Combine(_root, previewName);
                await File.WriteAllBytesAsync(stagedPreview, request.PreviewImage, ct).ConfigureAwait(false);
            }
            var payload = new StoredSave { Version = FormatVersion, Timestamp = DateTimeOffset.UtcNow, Description = request.Description, Snapshot = request.Snapshot, PreviewFile = previewName };
            var json = await Task.Run(() => JsonCodec.Serialize(payload, JsonOptions), ct).ConfigureAwait(false);
            await FileReplacement.WriteTextAsync(path, json, ct).ConfigureAwait(false);
            committed = true;
            TryDelete(previousPreview);
        }
        finally { if (!committed) TryDelete(stagedPreview); _gate.Release(); }
    }

    private async Task<SaveSlotInfo> GetInfoAsync(int slot, bool quick, CancellationToken ct)
    {
        var path = GetPath(slot, quick);
        if (!File.Exists(path)) return new SaveSlotInfo { SlotIndex = slot, IsQuickSave = quick };
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            var stored = JsonCodec.Deserialize<StoredSave>(json, JsonOptions);
            if (stored?.Version != FormatVersion || stored.Snapshot is null) throw new InvalidDataException();
            var preview = PreviewPath(stored, slot, quick);
            return new SaveSlotInfo { SlotIndex = slot, IsQuickSave = quick, Timestamp = stored.Timestamp.LocalDateTime, Description = stored.Description, PreviewImage = preview is not null && File.Exists(preview) ? preview : null };
        }
        catch (OperationCanceledException) { throw; }
        catch { return new SaveSlotInfo { SlotIndex = slot, IsQuickSave = quick, IsCorrupt = true }; }
    }

    private static async Task<GameSnapshot?> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var stored = JsonCodec.Deserialize<StoredSave>(await File.ReadAllTextAsync(path, ct), JsonOptions);
            return stored?.Version == FormatVersion ? stored.Snapshot : null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private string GetPath(int slot, bool quick) => Path.Combine(_root, quick ? "quick.json" : $"slot-{slot:D3}.json");
    private string GetPreviewPath(int slot, bool quick) => Path.Combine(_root, quick ? "quick.png" : $"slot-{slot:D3}.png");

    private string? PreviewPath(StoredSave stored, int slot, bool quick)
    {
        if (stored.PreviewFile is null) return GetPreviewPath(slot, quick); // Existing saves.
        var prefix = quick ? "quick-" : $"slot-{slot:D3}-";
        var name = stored.PreviewFile;
        return name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".png", StringComparison.Ordinal) &&
            name.Length == prefix.Length + 32 + 4 && Guid.TryParseExact(name.Substring(prefix.Length, 32), "N", out _)
            ? Path.Combine(_root, name) : null;
    }

    private async Task<string?> ReadPreviewPathAsync(string path, int slot, bool quick, CancellationToken ct)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var stored = JsonCodec.Deserialize<StoredSave>(await File.ReadAllTextAsync(path, ct), JsonOptions);
            return stored is null ? null : PreviewPath(stored, slot, quick);
        }
        catch (JsonException) { return null; }
    }

    private static void TryDelete(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void DeleteFiles(int slot, bool quick)
    {
        foreach (var path in new[] { GetPath(slot, quick), GetPreviewPath(slot, quick) })
            if (File.Exists(path)) File.Delete(path);
        if (Directory.Exists(_root))
            foreach (var path in Directory.EnumerateFiles(_root, quick ? "quick-*.png" : $"slot-{slot:D3}-*.png"))
                File.Delete(path);
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    internal sealed class StoredSave
    {
        public int Version { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? Description { get; set; }
        public GameSnapshot? Snapshot { get; set; }
        public string? PreviewFile { get; set; }
    }
}
