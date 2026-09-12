using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GalNet.Core.Scene;
using SkiaSharp;

namespace GalNet.Rendering.Scene;

/// <summary>A pixel pass. Stage selection is deliberately outside this contract.</summary>
public interface ITextureEffect
{
    void Render(SKCanvas target, SKBitmap source, SceneEffectInstance instance);

    /// <summary>GPU path. Effects that only support the CPU/snapshot path may keep the default.</summary>
    void Render(SKCanvas target, SKImage source, SceneEffectInstance instance) =>
        throw new NotSupportedException($"{GetType().Name} does not implement GPU texture rendering.");
}

/// <summary>Marks a texture effect that can stay entirely on the active Skia GPU context.</summary>
public interface IGpuTextureEffect : ITextureEffect;

/// <summary>Factory metadata plus a texture-to-texture pass implementation.</summary>
public interface ITextureEffectFactory
{
    EffectDefinition Definition { get; }
    ITextureEffect Create();
}

/// <summary>Live renderer-owned state for a texture effect instance.</summary>
public sealed class SceneEffectInstance : INotifyPropertyChanged
{
    private readonly Dictionary<string, float> _animatedValues = new(StringComparer.Ordinal);
    private readonly Lazy<IReadOnlyDictionary<string, JsonElement>> _staticValues;
    public SceneEffectInstance(string instanceId, ITextureEffectFactory factory, string targetHandleId, int order, string parameters, long insertionOrder = 0)
    {
        InstanceId = instanceId;
        Factory = factory;
        Effect = factory.Create();
        TargetHandleId = targetHandleId;
        Order = order;
        Parameters = parameters;
        InsertionOrder = insertionOrder;
        _staticValues = new Lazy<IReadOnlyDictionary<string, JsonElement>>(ParseStaticValues, LazyThreadSafetyMode.ExecutionAndPublication);
    }
    public string InstanceId { get; }
    public ITextureEffectFactory Factory { get; }
    public ITextureEffect Effect { get; }
    public EffectDefinition Definition => Factory.Definition;
    public string TargetHandleId { get; }
    public int Order { get; }
    public long InsertionOrder { get; }
    public string Parameters { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void SetAnimatedValue(string name, float value)
    {
        if (_animatedValues.TryGetValue(name, out var old) && Math.Abs(old - value) < float.Epsilon) return;
        _animatedValues[name] = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public float GetFloat(string name, float fallback, float minimum = float.NegativeInfinity, float maximum = float.PositiveInfinity)
    {
        if (_animatedValues.TryGetValue(name, out var animated)) return Math.Clamp(animated, minimum, maximum);
        if (_staticValues.Value.TryGetValue(name, out var value) && value.TryGetSingle(out var parsed))
            return Math.Clamp(parsed, minimum, maximum);
        return Math.Clamp(fallback, minimum, maximum);
    }
    public string GetString(string name, string fallback)
    {
        return _staticValues.Value.TryGetValue(name, out var value) ? value.GetString() ?? fallback : fallback;
    }

    public float GetSelectValue(string name, string fallback, IReadOnlyDictionary<string, float> options) =>
        options.TryGetValue(GetString(name, fallback), out var value) ? value : options[fallback];

    private IReadOnlyDictionary<string, JsonElement> ParseStaticValues()
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(Parameters) ? "{}" : Parameters);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return EmptyParameters;
            return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal);
        }
        catch (JsonException) { return EmptyParameters; }
    }

    private static IReadOnlyDictionary<string, JsonElement> EmptyParameters { get; } = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
}

/// <summary>Future scene objects (for example GPU particle batches) render before ScenePost passes.</summary>
public interface ISceneRenderable
{
    double Z { get; }
    void Render(SceneRenderContext context);
}

public sealed class SceneRenderContext(SKCanvas canvas, SKSize size)
{
    public SKCanvas Canvas { get; } = canvas;
    public SKSize Size { get; } = size;
}
