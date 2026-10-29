using System.Collections;
using GalNet.Core.Primitives;

namespace GalNet.Runtime.Engine;

/// <summary>Ordered instance ownership, event subscriptions and skip batches, under the Engine execution gate.</summary>
internal sealed class ActivePrimitiveRegistry(Action<PrimitiveInstance> completed, Action<PrimitiveInstance> autoChanged) : IEnumerable<ActivePrimitive>
{
    private readonly List<ActivePrimitive> _items = [];
    private readonly Action<PrimitiveInstance> _completed = completed;
    private readonly Action<PrimitiveInstance> _autoChanged = autoChanged;
    private string? _groupId;
    private long _groupExecutionId;

    public ActivePrimitive Track(PrimitiveInstance instance, string groupId, string stableId, bool wasRead, bool isReading)
    {
        var active = new ActivePrimitive(_groupExecutionId, instance, groupId, stableId, wasRead, isReading);
        _items.Add(active);
        instance.Completed += _completed;
        instance.AutoBlockingChanged += _autoChanged;
        return active;
    }
    public void EndGroup() => _groupId = null;
    public ActivePrimitive? Find(Predicate<ActivePrimitive> match) => _items.Find(match);
    public IEnumerator<ActivePrimitive> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerable<ActivePrimitive> SkipCandidates(ActivePrimitive blocker)
    {
        if (blocker.Instance.BatchId is null)
        {
            if (!blocker.Instance.IsCompleted && blocker.Instance.IsSkippable)
                yield return blocker;
            yield break;
        }

        foreach (var item in _items)
            if (item.GroupExecutionId == blocker.GroupExecutionId &&
                string.Equals(item.Instance.BatchId, blocker.Instance.BatchId, StringComparison.Ordinal) &&
                !item.Instance.IsCompleted && item.Instance.IsSkippable)
                yield return item;
    }

    public ActivePrimitive? EarliestBlocker() => _items
        .Where(item => item.Instance.IsBlocking && !item.Instance.IsCompleted)
        .FirstOrDefault();

    public bool HasBlockingInstance() => _items.Any(item =>
        item.Instance.IsBlocking && !item.Instance.IsCompleted);

    public void RemoveCompleted(Action<ActivePrimitive> reconcile)
    {
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            var item = _items[index];
            reconcile(item);
            if (!item.Instance.IsCompleted)
                continue;
            // Completed asynchronous work may have failed before its logical completion.
            // Surface that failure through the same engine fault path as synchronous dispatch.
            if (item.Instance.PresentationCompletion.IsFaulted)
                item.Instance.PresentationCompletion.GetAwaiter().GetResult();
            item.Instance.Completed -= _completed;
            item.Instance.AutoBlockingChanged -= _autoChanged;
            _items.RemoveAt(index);
        }
    }

    public void Clear()
    {
        foreach (var item in _items)
        {
            item.Instance.Completed -= _completed;
            item.Instance.AutoBlockingChanged -= _autoChanged;
        }
        _items.Clear();
    }

    public bool BeginGroupExecution(string groupId)
    {
        if (string.Equals(_groupId, groupId, StringComparison.Ordinal))
            return false;
        _groupId = groupId;
        _groupExecutionId++;
        return true;
    }

}

internal sealed record ActivePrimitive(
        long GroupExecutionId,
        PrimitiveInstance Instance,
        string GroupId,
        string StableId,
        bool WasRead,
        bool IsReading)
{
    public bool ReadMarked { get; set; }
}

