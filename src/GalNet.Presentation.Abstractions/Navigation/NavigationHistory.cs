namespace GalNet.Presentation.Abstractions.Navigation;

/// <summary>
/// Framework-independent history stack. Derived services keep their own navigation
/// contract, object construction and change-notification semantics.
/// </summary>
public abstract class NavigationHistory<TPage> where TPage : class
{
    private readonly Stack<TPage> _history = [];

    protected TPage? Current { get; private set; }
    public bool CanGoBack => _history.Count > 0;

    protected void Push(TPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (Current is not null) _history.Push(Current);
        SetCurrent(page);
    }

    protected void Replace(TPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _history.Clear();
        SetCurrent(page);
    }

    protected bool TryGoBack()
    {
        if (!_history.TryPop(out var page)) return false;
        SetCurrent(page);
        return true;
    }

    protected void ClearCurrent()
    {
        _history.Clear();
        SetCurrent(null);
    }

    protected abstract void OnCurrentChanged(TPage? page);

    private void SetCurrent(TPage? page)
    {
        Current = page;
        OnCurrentChanged(page);
    }
}
