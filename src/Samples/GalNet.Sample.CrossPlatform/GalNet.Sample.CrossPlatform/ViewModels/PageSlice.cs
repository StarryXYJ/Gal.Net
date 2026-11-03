using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GalNet.Sample.CrossPlatform.ViewModels;

/// <summary>A page of borrowed items; the source remains responsible for their lifetime.</summary>
public sealed class PageSlice<T> : ObservableObject, IDisposable
{
    private readonly ObservableCollection<T> _source;
    private int _currentPage = 1;
    private bool _deferred;
    private bool _disposed;

    public PageSlice(ObservableCollection<T> source, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        _source = source;
        PageSize = pageSize;
        _source.CollectionChanged += OnSourceChanged;
        Refresh();
    }

    public ObservableCollection<T> Items { get; } = [];
    public int PageSize { get; }
    public int TotalCount => _source.Count;
    public bool HasMultiplePages => TotalCount > PageSize;
    public event Action? Changed;
    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            var page = Math.Clamp(value, 1, Math.Max(1, (TotalCount + PageSize - 1) / PageSize));
            if (_currentPage == page) return;
            _currentPage = page;
            RefreshItems();
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    public void UpdateSource(Action update)
    {
        _deferred = true;
        try { update(); }
        finally { _deferred = false; Refresh(); }
    }

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_deferred) Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasMultiplePages));
        var page = Math.Clamp(_currentPage, 1, Math.Max(1, (TotalCount + PageSize - 1) / PageSize));
        var pageChanged = _currentPage != page;
        _currentPage = page;
        RefreshItems();
        if (pageChanged) OnPropertyChanged(nameof(CurrentPage));
        Changed?.Invoke();
    }

    private void RefreshItems()
    {
        var offset = (_currentPage - 1) * PageSize;
        var count = Math.Min(PageSize, Math.Max(0, _source.Count - offset));
        for (var index = 0; index < count; index++)
        {
            var item = _source[offset + index];
            if (index >= Items.Count) Items.Add(item);
            else if (!EqualityComparer<T>.Default.Equals(Items[index], item)) Items[index] = item;
        }
        while (Items.Count > count) Items.RemoveAt(Items.Count - 1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.CollectionChanged -= OnSourceChanged;
        Items.Clear();
        GC.SuppressFinalize(this);
    }
}
