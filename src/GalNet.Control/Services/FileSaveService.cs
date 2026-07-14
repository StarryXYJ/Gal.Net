using System.Text.Json;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Runtime.SaveLoad;

namespace GalNet.Control.Services;

/// <summary>File-backed slot store. A profile directory is supplied by the launcher.</summary>
public sealed class FileSaveService : ISaveService
{
    private const int FormatVersion = 1;
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int MaxSlots { get; }

    public FileSaveService(string profileDirectory, int maxSlots = 60)
    {
        _root = Path.Combine(profileDirectory, "saves");
        MaxSlots = Math.Max(1, maxSlots);
    }

    public IReadOnlyList<SaveSlotInfo> ListSlots() => ListSlotsAsync().GetAwaiter().GetResult();
    public async Task<IReadOnlyList<SaveSlotInfo>> ListSlotsAsync(CancellationToken ct = default)
    {
        var result = new List<SaveSlotInfo>(MaxSlots);
        for (var i = 0; i < MaxSlots; i++) result.Add(await GetInfoAsync(i, false, ct));
        return result;
    }

    public Task SaveAsync(int slot, GameSnapshot snapshot) => SaveAsync(slot, new SaveRequest { Snapshot = snapshot });
    public Task SaveAsync(int slot, SaveRequest request, CancellationToken ct = default) => WriteAsync(slot, false, request, ct);

    public async Task<GameSnapshot?> LoadAsync(int slot)
    {
        if (slot < 0 || slot >= MaxSlots) return null;
        return await ReadAsync(GetPath(slot, false));
    }

    public async Task DeleteAsync(int slot)
    {
        if (slot < 0 || slot >= MaxSlots) return;
        await _gate.WaitAsync();
        try { DeleteFiles(slot, false); } finally { _gate.Release(); }
    }

    public Task QuickSaveAsync(GameSnapshot snapshot) => QuickSaveAsync(new SaveRequest { Snapshot = snapshot });
    public Task QuickSaveAsync(SaveRequest request, CancellationToken ct = default) => WriteAsync(-1, true, request, ct);
    public Task<GameSnapshot?> QuickLoadAsync() => ReadAsync(GetPath(-1, true));
    public async Task<bool> HasQuickSaveAsync(CancellationToken ct = default) => (await GetQuickSaveInfoAsync(ct)) is { IsCorrupt: false };
    public async Task<SaveSlotInfo?> GetQuickSaveInfoAsync(CancellationToken ct = default)
    {
        var path = GetPath(-1, true);
        return File.Exists(path) ? await GetInfoAsync(-1, true, ct) : null;
    }
    public async Task DeleteQuickSaveAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { DeleteFiles(-1, true); } finally { _gate.Release(); }
    }

    private async Task WriteAsync(int slot, bool quick, SaveRequest request, CancellationToken ct)
    {
        if (!quick && (slot < 0 || slot >= MaxSlots)) throw new ArgumentOutOfRangeException(nameof(slot));
        await _gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_root);
            var payload = new StoredSave { Version = FormatVersion, Timestamp = DateTimeOffset.UtcNow, Description = request.Description, Snapshot = request.Snapshot };
            var path = GetPath(slot, quick);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(payload), ct);
            File.Move(temp, path, true);
            if (request.PreviewImage is { Length: > 0 }) await File.WriteAllBytesAsync(GetPreviewPath(slot, quick), request.PreviewImage, ct);
        }
        finally { _gate.Release(); }
    }

    private async Task<SaveSlotInfo> GetInfoAsync(int slot, bool quick, CancellationToken ct)
    {
        var path = GetPath(slot, quick);
        if (!File.Exists(path)) return new SaveSlotInfo { SlotIndex = slot, IsQuickSave = quick };
        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var stored = JsonSerializer.Deserialize<StoredSave>(json);
            if (stored?.Version != FormatVersion || stored.Snapshot is null) throw new InvalidDataException();
            var preview = GetPreviewPath(slot, quick);
            return new SaveSlotInfo { SlotIndex = slot, IsQuickSave = quick, Timestamp = stored.Timestamp.LocalDateTime, Description = stored.Description, PreviewImage = File.Exists(preview) ? preview : null };
        }
        catch { return new SaveSlotInfo { SlotIndex = slot, IsQuickSave = quick, IsCorrupt = true }; }
    }

    private async Task<GameSnapshot?> ReadAsync(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var stored = JsonSerializer.Deserialize<StoredSave>(await File.ReadAllTextAsync(path));
            return stored?.Version == FormatVersion ? stored.Snapshot : null;
        }
        catch { return null; }
    }

    private string GetPath(int slot, bool quick) => Path.Combine(_root, quick ? "quick.json" : $"slot-{slot:D3}.json");
    private string GetPreviewPath(int slot, bool quick) => Path.Combine(_root, quick ? "quick.png" : $"slot-{slot:D3}.png");
    private void DeleteFiles(int slot, bool quick)
    {
        foreach (var path in new[] { GetPath(slot, quick), GetPreviewPath(slot, quick) }) if (File.Exists(path)) File.Delete(path);
    }

    private sealed class StoredSave
    {
        public int Version { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string? Description { get; set; }
        public GameSnapshot? Snapshot { get; set; }
    }
}
