namespace GalNet.Core.Primitives;

/// <summary>A small instance for synchronous primitive behavior.</summary>
public sealed class ImmediatePrimitiveInstance : PrimitiveInstance
{
    private readonly Action? _dispatch;

    public ImmediatePrimitiveInstance(Action? dispatch = null, bool blocking = false, string? batchId = null)
        : base(batchId)
    {
        _dispatch = dispatch;
        IsBlocking = blocking;
    }

    public override bool IsBlocking { get; }
    public override bool IsSkippable => false;

    protected override void OnDispatch()
    {
        _dispatch?.Invoke();
        TryComplete();
    }
}
