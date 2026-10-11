using GalNet.Core.Primitives;

namespace GalNet.Presentation.Abstractions.Execution;

/// <summary>One instruction factory, independent of its parameter schema.</summary>
public sealed class PrimitiveImplementation
{
    private readonly Func<PrimitiveCreateContext, PrimitiveInstance> _factory;

    public PrimitiveImplementation(string type, Func<PrimitiveCreateContext, PrimitiveInstance> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);
        Type = type;
        _factory = factory;
    }

    public string Type { get; }
    public PrimitiveInstance CreateInstance(PrimitiveCreateContext context) =>
        _factory(context) ?? throw new InvalidOperationException($"Instruction '{Type}' returned no instance.");
}
