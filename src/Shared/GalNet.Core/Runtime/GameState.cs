using GalNet.Core.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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
    public void Register<T>(string id, Func<T, string> key, Action<T>? validate = null, int version = 1, string? keySpace = null,
        JsonTypeInfo<T[]>? jsonTypeInfo = null) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        if (_contracts.Any(item => item.Id == id || item.Type == typeof(T)))
            throw new InvalidOperationException($"Duplicate state contract '{id}' / '{typeof(T).Name}'.");
        _contracts.Add(new StateContract<T>(id, version, key, validate, keySpace, jsonTypeInfo));
    }
    public StateCatalog Build() => new(_contracts);
}

internal abstract class StateContract(string id, int version, Type type, string? keySpace)
{
    public string Id { get; } = id;
    public int Version { get; } = version;
    public Type Type { get; } = type;
    public string? KeySpace { get; } = keySpace;
    public abstract IStateTable Create(GameState owner, JsonElement? payload);
}

internal sealed class StateContract<T>(string id, int version, Func<T, string> key, Action<T>? validate, string? keySpace,
    JsonTypeInfo<T[]>? jsonTypeInfo)
    : StateContract(id, version, typeof(T), keySpace) where T : class
{
    internal JsonTypeInfo<T[]> JsonTypeInfo => jsonTypeInfo ?? JsonMetadata.GetTypeInfo<T[]>(GameState.JsonOptions);
    public override IStateTable Create(GameState owner, JsonElement? payload)
    {
        var table = new StateTable<T>(owner, this, key, validate);
        if (payload is { } json)
        {
            if (json.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"State '{Id}' must be an array.");
            T[] values;
            try { values = JsonSerializer.Deserialize(json, JsonTypeInfo) ?? throw new InvalidDataException($"Invalid state '{Id}'."); }
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
    private readonly StateContract<T> _contract;
    StateContract IStateTable.Contract => _contract;
    internal StateTable(GameState owner, StateContract<T> contract, Func<T, string> key, Action<T>? validate)
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
            _owner.CheckIdentity(_contract.KeySpace, id, value, _values.GetValueOrDefault(id));
            _values[id] = value;
        }
    }
    public bool Remove(string id)
    {
        lock (_owner.SyncRoot)
        {
            if (!_values.Remove(id, out var value)) return false;
            _owner.RemoveIdentity(_contract.KeySpace, id, value);
            return true;
        }
    }
    public void Clear()
    {
        lock (_owner.SyncRoot)
        {
            foreach (var (id, value) in _values) _owner.RemoveIdentity(_contract.KeySpace, id, value);
            _values.Clear();
        }
    }
    JsonElement IStateTable.Encode() => JsonSerializer.SerializeToElement(_values.Values.ToArray(), _contract.JsonTypeInfo);
}

/// <summary>Canonical persistent facts for one run. Old run references never resolve through a new state root.</summary>
public sealed class GameState
{
    public static JsonSerializerOptions JsonOptions { get; } = JsonMetadata.CreateOptions(new()
    {
        AllowDuplicateProperties = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IgnoreReadOnlyProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    }, stringEnums: true);
    internal object SyncRoot { get; } = new();
    private readonly Dictionary<Type, IStateTable> _tables = [];
    private readonly Dictionary<(string Space, string Id), object> _identities = new();
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
                    if (existing.Contract.Id != contract.Id || existing.Contract.Version != contract.Version || existing.Contract.KeySpace != contract.KeySpace)
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
    public T Update<T>(Func<T> update) { lock (SyncRoot) return update(); }
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
    internal void CheckIdentity(string? space, string id, object value, object? previous)
    {
        if (space is null) return;
        if (_identities.TryGetValue((space, id), out var existing) && !ReferenceEquals(existing, previous))
            throw new InvalidOperationException($"State handle '{id}' is already in use in '{space}'.");
        _identities[(space, id)] = value;
    }
    internal void RemoveIdentity(string? space, string id, object value)
    {
        if (space is not null && _identities.TryGetValue((space, id), out var existing) && ReferenceEquals(existing, value))
            _identities.Remove((space, id));
    }
    public bool TryGetIdentity<T>(string space, string id, out T value) where T : class
    {
        lock (SyncRoot)
        {
            value = _identities.GetValueOrDefault((space, id)) as T ?? null!;
            return value is not null;
        }
    }
    public IReadOnlyList<T> IdentityObjects<T>(string space) where T : class
    {
        lock (SyncRoot) return _identities.Where(pair => pair.Key.Space == space).Select(pair => pair.Value).OfType<T>().ToArray();
    }
}
