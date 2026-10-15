using System.Text.Json;
using System.Text.Json.Serialization;
using GalNet.Core.Scene;

namespace GalNet.Core.Runtime;

public sealed record StateSection(int Version, JsonElement Payload);

/// <summary>Explicit, platform-neutral state contracts. A catalog is immutable after composition.</summary>
public sealed class StateCatalog
{
    public static StateCatalog Empty { get; } = new([]);
    internal IReadOnlyList<StateContract> Contracts { get; }
    public IReadOnlyCollection<string> Ids => Contracts.Select(item => item.Id).ToArray();
    internal StateCatalog(IEnumerable<StateContract> contracts) => Contracts = contracts.ToArray();
}

public sealed class StateCatalogBuilder
{
    private readonly List<StateContract> _contracts = [];
    public void Register<T>(string id, Func<T, string> key, Action<T>? validate = null, int version = 1) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (_contracts.Any(item => item.Id == id || item.Type == typeof(T)))
            throw new InvalidOperationException($"Duplicate state contract '{id}' / '{typeof(T).Name}'.");
        _contracts.Add(new StateContract<T>(id, version, key, validate));
    }
    public StateCatalog Build() => new(_contracts);
}

internal abstract class StateContract(string id, int version, Type type)
{
    public string Id { get; } = id;
    public int Version { get; } = version;
    public Type Type { get; } = type;
    public abstract IStateTable Create(GameState owner, JsonElement? payload);
}

internal sealed class StateContract<T>(string id, int version, Func<T, string> key, Action<T>? validate)
    : StateContract(id, version, typeof(T)) where T : class
{
    public override IStateTable Create(GameState owner, JsonElement? payload)
    {
        var table = new StateTable<T>(owner, this, key, validate);
        if (payload is { } json)
        {
            if (json.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"State '{Id}' must be an array.");
            T[] values;
            try { values = json.Deserialize<T[]>(GameState.JsonOptions) ?? throw new InvalidDataException($"Invalid state '{Id}'."); }
            catch (JsonException error) { throw new InvalidDataException($"Invalid state '{Id}': {error.Message}", error); }
            foreach (var value in values)
                table.Add(value);
        }
        return table;
    }
}

internal interface IStateTable
{
    StateContract Contract { get; }
    JsonElement Encode();
}

/// <summary>A typed, ID-indexed table owned by one run. Mutations and capture share the owner's gate.</summary>
public sealed class StateTable<T> : IStateTable where T : class
{
    private readonly GameState _owner;
    private readonly Func<T, string> _key;
    private readonly Action<T>? _validate;
    private readonly Dictionary<string, T> _values = new(StringComparer.Ordinal);
    private readonly StateContract _contract;
    StateContract IStateTable.Contract => _contract;
    internal StateTable(GameState owner, StateContract contract, Func<T, string> key, Action<T>? validate)
    { _owner = owner; _contract = contract; _key = key; _validate = validate; }
    public IReadOnlyList<T> Values { get { lock (_owner.SyncRoot) return _values.Values.ToArray(); } }
    public bool TryGet(string id, out T value) { lock (_owner.SyncRoot) return _values.TryGetValue(id, out value!); }
    public T Get(string id) => TryGet(id, out var value) ? value : throw new KeyNotFoundException(id);
    public void Add(T value) => Write(value, replace: false);
    public void Set(T value) => Write(value, replace: true);
    private void Write(T value, bool replace)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_owner.SyncRoot)
        {
            var id = _key(value);
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException($"State '{_contract.Id}' has an empty ID.");
            _validate?.Invoke(value);
            if (!replace && _values.ContainsKey(id)) throw new InvalidDataException($"Duplicate state item '{_contract.Id}/{id}'.");
            _owner.CheckSceneIdentity(value, _values.GetValueOrDefault(id));
            _values[id] = value;
        }
    }
    public bool Remove(string id)
    {
        lock (_owner.SyncRoot)
        {
            if (!_values.Remove(id, out var value)) return false;
            _owner.RemoveSceneIdentity(value);
            return true;
        }
    }
    public void Clear()
    {
        lock (_owner.SyncRoot)
        {
            foreach (var value in _values.Values) _owner.RemoveSceneIdentity(value);
            _values.Clear();
        }
    }
    JsonElement IStateTable.Encode() => JsonSerializer.SerializeToElement(_values.Values.ToArray(), GameState.JsonOptions);
}

/// <summary>Canonical persistent facts for one run. Old run references never resolve through a new state root.</summary>
public sealed class GameState
{
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        AllowDuplicateProperties = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IgnoreReadOnlyProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public object SyncRoot { get; } = new();
    private readonly Dictionary<Type, IStateTable> _tables = [];
    private readonly Dictionary<string, ISceneInstance> _scene = new(StringComparer.Ordinal);
    private StateCatalog? _lastCatalog;
    public GameState(StateCatalog? catalog = null) => Configure(catalog ?? StateCatalog.Empty);
    public void Configure(StateCatalog catalog)
    {
        lock (SyncRoot)
        {
            if (ReferenceEquals(catalog, _lastCatalog)) return;
            foreach (var contract in catalog.Contracts)
            {
                if (_tables.TryGetValue(contract.Type, out var existing))
                {
                    if (existing.Contract.Id != contract.Id || existing.Contract.Version != contract.Version)
                        throw new InvalidOperationException($"Conflicting state contract '{contract.Id}'.");
                    continue;
                }
                if (_tables.Values.Any(table => table.Contract.Id == contract.Id)) throw new InvalidOperationException($"Duplicate state ID '{contract.Id}'.");
                _tables.Add(contract.Type, contract.Create(this, null));
            }
            _lastCatalog = catalog;
        }
    }
    public StateCatalog Catalog { get { lock (SyncRoot) return new(_tables.Values.Select(table => table.Contract)); } }
    public StateTable<T> Table<T>() where T : class
    {
        lock (SyncRoot) return _tables.TryGetValue(typeof(T), out var table) ? (StateTable<T>)table
            : throw new InvalidOperationException($"State type '{typeof(T).Name}' is not registered.");
    }
    public void Update(Action action) { lock (SyncRoot) action(); }
    public T Read<T>(Func<T> read) { lock (SyncRoot) return read(); }
    public Dictionary<string, StateSection> Capture()
    {
        lock (SyncRoot) return _tables.Values.ToDictionary(table => table.Contract.Id,
            table => new StateSection(table.Contract.Version, table.Encode()), StringComparer.Ordinal);
    }
    public static GameState Restore(StateCatalog catalog, IReadOnlyDictionary<string, StateSection> sections)
    {
        if (sections.Count != catalog.Contracts.Count) throw new InvalidDataException("Save state sections do not match the registered catalog.");
        var state = new GameState();
        foreach (var contract in catalog.Contracts)
        {
            if (!sections.TryGetValue(contract.Id, out var section) || section is null || section.Version != contract.Version)
                throw new InvalidDataException($"Missing or unsupported state section '{contract.Id}'.");
            var table = contract.Create(state, section.Payload);
            state._tables.Add(contract.Type, table);
        }
        return state;
    }
    internal void CheckSceneIdentity(object value, object? previous)
    {
        if (value is not ISceneInstance scene) return;
        if (_scene.TryGetValue(scene.Id, out var existing) && !ReferenceEquals(existing, previous))
            throw new InvalidOperationException($"Scene handle '{scene.Id}' is already in use.");
        _scene[scene.Id] = scene;
    }
    internal void RemoveSceneIdentity(object value)
    {
        if (value is ISceneInstance scene && _scene.TryGetValue(scene.Id, out var existing) && ReferenceEquals(existing, value))
            _scene.Remove(scene.Id);
    }
    public bool TryGetScene<T>(string id, out T value) where T : class, ISceneInstance
    {
        lock (SyncRoot)
        {
            value = _scene.GetValueOrDefault(id) as T ?? null!;
            return value is not null;
        }
    }
    public IReadOnlyList<T> SceneObjects<T>() where T : class, ISceneInstance
    {
        lock (SyncRoot) return _scene.Values.OfType<T>().ToArray();
    }
}
