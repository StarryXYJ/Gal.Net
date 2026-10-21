using System.Collections.ObjectModel;
using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Scene;
using GalNet.Editor.Abstraction.Documents;

namespace GalNet.Editor.Shared.Services;

public enum TargetPresence { Definite, Possible }
public enum SceneTargetKind { Layer, Particle, Effect, Unresolved }
public sealed record SceneLocator(string GroupId, string EntryId, string Slot, string? EventId = null);
public sealed record SceneTimelinePosition(int Frame, int? BeforeEventIndex = null, bool IncludeFuture = false);
public sealed record SceneTarget(SceneLocator Locator, string Handle, SceneTargetKind Kind, string Resource,
    TargetPresence Presence, IReadOnlyList<AnimatableProperty> Properties, IReadOnlyDictionary<string, float> Values, int? CreatedAtFrame = null, bool IsFuture = false, bool CapabilitiesComplete = true);
public sealed record SceneTargetResult(IReadOnlyList<SceneTarget> Targets, bool Complete, bool Reachable);

/// <summary>Pure, finite abstract interpretation. Never instantiates a game or executes an entry.</summary>
public sealed partial class SceneTargetAnalysis
{
    private sealed class State
    {
        public Dictionary<string, SceneTarget> Targets { get; } = new(StringComparer.Ordinal);
        public bool Complete { get; set; } = true;
        public State Clone() { var copy = new State { Complete = Complete }; foreach (var pair in Targets) copy.Targets.Add(pair.Key, pair.Value); return copy; }
        public void Uncertain() { Complete = false; foreach (var (id, target) in Targets.ToArray()) Targets[id] = target with { Presence = TargetPresence.Possible, Values = EmptyValues }; }
    }
    private static readonly IReadOnlyDictionary<string, float> EmptyValues = new ReadOnlyDictionary<string, float>(new Dictionary<string, float>());
    private readonly IReadOnlyDictionary<string, List<EditorEntryData>> _groups;
    private readonly IReadOnlyDictionary<string, ShaderEffectDescriptor?>? _programs;
    private readonly IEffectCatalog? _effects;
    private readonly Dictionary<string, State> _inputs = new(StringComparer.Ordinal);
    private bool _budgetExceeded;
    public SceneTargetAnalysis(EditorGraphDocument graph, IReadOnlyDictionary<string, List<EditorEntryData>> groups, int budget = 10000,
        IReadOnlyDictionary<string, ShaderEffectDescriptor?>? programs = null, IEffectCatalog? effects = null)
    {
        // Caller owns the immutable snapshot for this generation.
        _groups = groups;
        _programs = programs; _effects = effects;
        var nodes = graph.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        if (!nodes.ContainsKey(graph.RootNodeId)) return;
        var outputs = new Dictionary<string, State>(StringComparer.Ordinal);
        var queue = new Queue<string>(); queue.Enqueue(graph.RootNodeId);
        var successors = graph.Edges.GroupBy(edge => edge.FromNodeId).ToDictionary(group => group.Key, group => group.Select(edge => edge.ToNodeId).Distinct().Where(nodes.ContainsKey).ToArray());
        var predecessors = graph.Edges.GroupBy(edge => edge.ToNodeId).ToDictionary(group => group.Key, group => group.Select(edge => edge.FromNodeId).Distinct().ToArray());
        while (queue.Count > 0)
        {
            if (--budget < 0) { _budgetExceeded = true; break; }
            var id = queue.Dequeue();
            var incoming = predecessors.TryGetValue(id, out var previous) ? previous.Where(outputs.ContainsKey).Select(key => outputs[key]).ToList() : [];
            if (id == graph.RootNodeId) incoming.Add(new State());
            if (incoming.Count == 0) continue;
            var input = Merge(incoming); _inputs[id] = input;
            var output = input.Clone();
            var node = nodes[id];
            if (node.Type == "Group")
            {
                if (groups.TryGetValue(id, out var entries)) foreach (var entry in entries) output = Apply(output, id, entry);
                else output.Uncertain();
            }
            else if (node.Type is not ("Entry" or "Branch" or "Exit")) output.Uncertain();
            if (outputs.TryGetValue(id, out var old) && Equal(old, output)) continue;
            outputs[id] = output;
            if (successors.TryGetValue(id, out var next)) foreach (var target in next) queue.Enqueue(target);
        }
    }
    public SceneTargetResult Before(string groupId, string? entryId)
        => StateBefore(groupId, entryId) is { } state ? Result(state) : new([], false, false);
    private State? StateBefore(string groupId, string? entryId)
    {
        if (!_inputs.TryGetValue(groupId, out var input) || !_groups.TryGetValue(groupId, out var entries)) return null;
        var state = input.Clone();
        if (_budgetExceeded)
        {
            // Do not publish an unfinished fixed point as a definite fact.
            state.Uncertain();
            foreach (var (id, group) in _groups) foreach (var entry in group)
            {
                var candidate = Apply(new State(), id, entry);
                foreach (var (handle, target) in candidate.Targets) state.Targets.TryAdd(handle, target with { Presence = TargetPresence.Possible, Values = EmptyValues });
            }
        }
        var found = entryId is null;
        foreach (var entry in entries)
        {
            if (entry.StableId == entryId) { found = true; break; }
            state = Apply(state, groupId, entry);
        }
        if (!found) return null;
        if (_budgetExceeded) state.Uncertain();
        return state;
    }
    private static State Merge(IReadOnlyList<State> states)
    {
        var result = new State { Complete = states.All(state => state.Complete) };
        foreach (var id in states.SelectMany(state => state.Targets.Keys).Distinct(StringComparer.Ordinal))
        {
            var candidates = states.Where(state => state.Targets.ContainsKey(id)).Select(state => state.Targets[id]).ToArray();
            var first = candidates.OrderBy(target => target.Locator.GroupId, StringComparer.Ordinal).ThenBy(target => target.Locator.EntryId, StringComparer.Ordinal).First();
            var sameKind = candidates.All(target => target.Kind == first.Kind);
            var definite = sameKind && candidates.Length == states.Count && candidates.All(target => target.Presence == TargetPresence.Definite);
            var properties = first.Properties.Where(property => candidates.All(target => target.Kind == first.Kind && target.Properties.Contains(property))).ToArray();
            var values = definite ? first.Values.Where(pair => candidates.All(target => target.Values.TryGetValue(pair.Key, out var value) && value == pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value) : [];
            result.Targets[id] = first with
            {
                Kind = sameKind ? first.Kind : SceneTargetKind.Unresolved,
                Presence = definite ? TargetPresence.Definite : TargetPresence.Possible,
                Properties = Array.AsReadOnly(properties),
                Values = new ReadOnlyDictionary<string, float>(values),
                Resource = candidates.All(target => target.Resource == first.Resource) ? first.Resource : "",
                CreatedAtFrame = candidates.All(target => target.Locator == first.Locator && target.CreatedAtFrame == first.CreatedAtFrame) ? first.CreatedAtFrame : null,
                IsFuture = candidates.All(target => target.IsFuture),
                CapabilitiesComplete = candidates.All(target => target.CapabilitiesComplete)
            };
        }
        return result;
    }
    private static bool Equal(State left, State right) => left.Complete == right.Complete && left.Targets.Count == right.Targets.Count && left.Targets.All(pair =>
        right.Targets.TryGetValue(pair.Key, out var other) && pair.Value.Locator == other.Locator && pair.Value.Kind == other.Kind && pair.Value.Resource == other.Resource
        && pair.Value.CreatedAtFrame == other.CreatedAtFrame && pair.Value.IsFuture == other.IsFuture
        && pair.Value.CapabilitiesComplete == other.CapabilitiesComplete
        && pair.Value.Presence == other.Presence && pair.Value.Properties.SequenceEqual(other.Properties)
        && pair.Value.Values.Count == other.Values.Count && pair.Value.Values.All(value => other.Values.TryGetValue(value.Key, out var number) && number == value.Value));
    private State Apply(State before, string group, EditorEntryData entry)
    {
        if (entry.Condition.Trim().Equals("false", StringComparison.OrdinalIgnoreCase)) return before.Clone();
        var after = before.Clone();
        try { Transfer(after, group, entry); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or ArgumentException or InvalidDataException or OverflowException) { after.Uncertain(); }
        return string.IsNullOrWhiteSpace(entry.Condition) || entry.Condition.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ? after : Merge([before, after]);
    }
    private void Transfer(State state, string group, EditorEntryData entry)
    {
        string Text(string key, string fallback = "") => entry.Parameters.TryGetValue(key, out var value) ? value.GetString() ?? fallback : fallback;
        float Number(string key, float fallback) => entry.Parameters.TryGetValue(key, out var value) ? value.GetSingle() : fallback;
        JsonElement Json(string key) => entry.Parameters.TryGetValue(key, out var value) ? value : JsonSerializer.SerializeToElement(new { });
        void Create(string handle, SceneTargetKind kind, string resource, IEnumerable<AnimatableProperty> properties, Dictionary<string, float> values)
        {
            if (string.IsNullOrWhiteSpace(handle) || values.Values.Any(value => !float.IsFinite(value))) { state.Uncertain(); return; }
            state.Targets[handle] = new(new(group, entry.StableId, kind == SceneTargetKind.Layer ? "handleId" : "instanceId"), handle, kind, resource, TargetPresence.Definite,
                Array.AsReadOnly(properties.ToArray()), new ReadOnlyDictionary<string, float>(values));
        }
        void Transform(string handle, bool create)
        {
            var transform = Json("transform").Deserialize<LayerTransform>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new JsonException();
            Dictionary<string, float> values = new() { ["transform.x"] = transform.X, ["transform.y"] = transform.Y, ["transform.rotationDegrees"] = transform.RotationDegrees, ["transform.scaleX"] = transform.ScaleX, ["transform.scaleY"] = transform.ScaleY };
            if (values.Values.Any(value => !float.IsFinite(value))) throw new InvalidDataException("Invalid transform.");
            if (create)
            {
                values["opacity"] = Number("opacity", 1);
                var properties = Layer.AnimationProperties.Where(property => property.Name != "flipbook.index").ToList();
                if (entry.Type == BuiltinEntryIds.ShowLayer && entry.Parameters.TryGetValue("flipbook", out var raw) && raw.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    var flipbook = raw.Deserialize<FlipbookDefinition>(TimelineJson);
                    if (flipbook is { IsValid: true })
                    {
                        var property = Layer.AnimationProperties.Single(property => property.Name == "flipbook.index");
                        properties.Add(property);
                        // Runtime stores the raw float index; only rendering floors/clamps it to a cell.
                        if (property.Accepts(flipbook.Index)) values[property.Name] = flipbook.Index;
                        else state.Complete = false;
                    }
                    else if (flipbook is not null) state.Complete = false;
                }
                Create(handle, SceneTargetKind.Layer, Text(entry.Type == BuiltinEntryIds.PlayVideo ? "asset" : entry.Type == BuiltinEntryIds.ShowColorLayer ? "color" : "assetId"), properties, values);
            }
            else if (state.Targets.TryGetValue(handle, out var target) && target.Kind == SceneTargetKind.Layer)
                state.Targets[handle] = target with { Values = new ReadOnlyDictionary<string, float>(target.Values.Where(pair => !values.ContainsKey(pair.Key)).Concat(values).ToDictionary(pair => pair.Key, pair => pair.Value)) };
        }
        switch (entry.Type)
        {
            case BuiltinEntryIds.ShowLayer:
            case BuiltinEntryIds.ShowColorLayer: Transform(Text("handleId"), true); break;
            case BuiltinEntryIds.PlayVideo:
                Transform(Text("handleId", "video"), true);
                if (state.Targets.TryGetValue(Text("handleId", "video"), out var video)) state.Targets[video.Handle] = video with { Presence = TargetPresence.Possible, Values = EmptyValues };
                break;
            case BuiltinEntryIds.HideLayer:
            case BuiltinEntryIds.StopVideo:
                if (state.Targets.TryGetValue(Text("handleId", entry.Type == BuiltinEntryIds.StopVideo ? "video" : ""), out var layer) && layer.Kind == SceneTargetKind.Layer) state.Targets.Remove(layer.Handle);
                break;
            case BuiltinEntryIds.MoveLayer: Transform(Text("handleId"), false); break;
            case BuiltinEntryIds.ReplaceLayer:
                if (state.Targets.TryGetValue(Text("handleId"), out var replaced) && replaced.Kind == SceneTargetKind.Layer) state.Targets[replaced.Handle] = replaced with { Resource = Text("assetId") };
                break;
            case BuiltinEntryIds.PlayParticleEmitter:
                var definition = ParticleEmitterDefinition.FromJson(Json("parameters").GetRawText());
                Create(Text("instanceId"), SceneTargetKind.Particle, definition.ParticleTexture, ParticleEmitterInstance.AnimationProperties,
                    new() { ["emissionRate"] = definition.EmissionRate, ["initialVelocityX"] = definition.InitialModule.VelocityX.Center, ["initialVelocityY"] = definition.InitialModule.VelocityY.Center, ["noise"] = definition.MotionModule.Noise, ["particleScale"] = definition.InitialModule.Size.Center, ["particleLifetime"] = definition.InitialModule.Lifetime.Center });
                break;
            case BuiltinEntryIds.ApplyEffect:
                var effect = EffectCapabilities(entry);
                Create(Text("instanceId"), SceneTargetKind.Effect, string.IsNullOrWhiteSpace(Text("program")) ? Text("id") : Text("program"), effect.Properties, effect.Values);
                if (state.Targets.TryGetValue(Text("instanceId"), out var createdEffect)) state.Targets[createdEffect.Handle] = createdEffect with { CapabilitiesComplete = effect.Complete };
                break;
            case BuiltinEntryIds.StopParticleEmitter:
            case BuiltinEntryIds.StopEffect:
                var kind = entry.Type == BuiltinEntryIds.StopEffect ? SceneTargetKind.Effect : SceneTargetKind.Particle;
                if (state.Targets.TryGetValue(Text("instanceId"), out var stopped) && stopped.Kind == kind) state.Targets.Remove(stopped.Handle);
                break;
            case BuiltinEntryIds.PlayTimeline: TransferTimeline(state, group, entry); break;
            case BuiltinEntryIds.CrossFadeTransition:
            case BuiltinEntryIds.SlideTransition:
            case BuiltinEntryIds.BlindsTransition:
            case BuiltinEntryIds.BlackFadeTransition:
            case BuiltinEntryIds.WhiteFadeTransition:
            case BuiltinEntryIds.ColorFadeTransition: TransferTransition(state, group, entry); break;
            case BuiltinEntryIds.Animate:
                if (state.Targets.TryGetValue(Text("handleId"), out var animated)) state.Targets[animated.Handle] = animated with { Values = EmptyValues };
                break;
            // Proven scene-neutral built-ins. Unknown/composite/Timeline semantics must never be neutral by default.
            case BuiltinEntryIds.Text:
            case BuiltinEntryIds.ShowDialogue:
            case BuiltinEntryIds.HideDialogue:
            case BuiltinEntryIds.ShowNvl:
            case BuiltinEntryIds.HideNvl:
            case BuiltinEntryIds.ClearNvl:
            case BuiltinEntryIds.AppendNvl:
            case BuiltinEntryIds.TextVoice:
            case BuiltinEntryIds.AppendVoiceNvl:
            case BuiltinEntryIds.Wait:
            case BuiltinEntryIds.SetVariable:
            case BuiltinEntryIds.UnlockGallery:
            case BuiltinEntryIds.PlayAudio:
            case BuiltinEntryIds.StopAudio:
            case BuiltinEntryIds.PauseAudio:
            case BuiltinEntryIds.ResumeAudio:
            case BuiltinEntryIds.NextAudio:
            case BuiltinEntryIds.ClearAudio:
            case BuiltinEntryIds.EnqueueAudio:
            case BuiltinEntryIds.ApplyAudioSEffect:
            case BuiltinEntryIds.StopAudioSEffect:
            case BuiltinEntryIds.BurstParticles:
            case BuiltinEntryIds.StopAnimation: break;
            default: state.Uncertain(); break;
        }
    }
}
