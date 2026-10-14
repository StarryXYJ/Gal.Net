using GalNet.Core.Entry;
using GalNet.Core.Parameters;
using GalNet.Core.Primitives;

namespace GalNet.Presentation.Abstractions.Execution;

/// <summary>One instruction factory, independent of its parameter schema.</summary>
public sealed class PrimitiveImplementation
{
    private readonly Func<PrimitiveCreateContext, PrimitiveInstance> _factory;
    private Type? FieldDataType { get; init; }

    public PrimitiveImplementation(string type, Func<PrimitiveCreateContext, PrimitiveInstance> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(factory);
        Type = type;
        _factory = factory;
    }

    public string Type { get; }

    /// <summary>Synchronous behavior with automatic completion and batch propagation. CatchUp repeats dispatch unless overridden.</summary>
    public static PrimitiveImplementation Immediate(string type, Action<PrimitiveCreateContext> dispatch,
        Action<PrimitiveCreateContext>? catchUp = null)
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        return new(type, context => new ImmediatePrimitiveInstance(() => dispatch(context), batchId: context.BatchId,
            catchUp: catchUp is null ? null : () => catchUp(context)));
    }

    /// <summary>Decode the contract DTO once per invocation; keep synchronous execution and CatchUp policy explicit.</summary>
    public static PrimitiveImplementation Immediate<T>(string type, Action<PrimitiveCreateContext, T> dispatch,
        Action<PrimitiveCreateContext, T>? catchUp = null)
        where T : IFieldDto, IFieldSchema<T>, new()
    {
        ArgumentNullException.ThrowIfNull(dispatch);
        return For<T>(type, (context, data) => new ImmediatePrimitiveInstance(() => dispatch(context, data),
            batchId: context.BatchId, catchUp: catchUp is null ? null : () => catchUp(context, data)));
    }

    /// <summary>Decode normalized arguments using the contract's per-field codecs. Each invocation owns its DTO.</summary>
    public static PrimitiveImplementation For<T>(string type, Func<PrimitiveCreateContext, T, PrimitiveInstance> factory)
        where T : IFieldDto, IFieldSchema<T>, new()
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new(type, context =>
        {
            var data = new T();
            foreach (var field in context.Parameters.Fields)
                if (context.Arguments.TryGetProperty(field.Id, out var value))
                    data.WriteField(field.Id, ValueCodec.Deserialize(field.Value, value));
            return factory(context, data);
        })
        { FieldDataType = typeof(T) };
    }

    internal void ValidateDefinition(EntryDefinition definition)
    {
        if (FieldDataType is { } expected && definition.FieldDataType != expected)
            throw new InvalidOperationException($"Instruction '{Type}' requires DTO '{expected.FullName}', but its contract declares '{definition.FieldDataType?.FullName ?? "none"}'.");
    }

    public PrimitiveInstance CreateInstance(PrimitiveCreateContext context)
    {
        ValidateDefinition(context.Definition);
        return _factory(context) ?? throw new InvalidOperationException($"Instruction '{Type}' returned no instance.");
    }
}
