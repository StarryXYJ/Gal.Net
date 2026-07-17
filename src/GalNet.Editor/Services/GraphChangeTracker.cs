using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Editor.Models.Graph;

namespace GalNet.Editor.Services;

/// <summary>Tracks persisted graph presentation changes without coupling them to the workspace view model.</summary>
public sealed class GraphChangeTracker : IDisposable
{
    private readonly Action _markDirty;
    private readonly Func<bool> _isLoading;
    private readonly Action<GraphNode, string>? _nodeChanged;
    private readonly Action<GraphNode, object, string>? _itemChanged;
    private readonly List<Action> _unsubscribe = [];

    public GraphChangeTracker(
        Action markDirty,
        Func<bool> isLoading,
        Action<GraphNode, string>? nodeChanged = null,
        Action<GraphNode, object, string>? itemChanged = null)
    {
        _markDirty = markDirty;
        _isLoading = isLoading;
        _nodeChanged = nodeChanged;
        _itemChanged = itemChanged;
    }

    public void Track(GraphNode node)
    {
        PropertyChangedEventHandler nodeHandler = (_, args) =>
        {
            if (!_isLoading() && args.PropertyName == nameof(GraphNode.Name))
            {
                if (_nodeChanged is not null) _nodeChanged(node, args.PropertyName);
                else _markDirty();
            }
        };
        node.PropertyChanged += nodeHandler;
        _unsubscribe.Add(() => node.PropertyChanged -= nodeHandler);

        TrackCollection(node, node.Entries, nameof(EntryEditorItemViewModel.Type), nameof(EntryEditorItemViewModel.Condition), nameof(EntryEditorItemViewModel.Parameters));
        TrackCollection(node, node.Options, nameof(BranchOptionEditorItemViewModel.Text), nameof(BranchOptionEditorItemViewModel.Condition));
        TrackCollection(node, node.Conditions, nameof(BranchConditionEditorItemViewModel.Expression));
    }

    public void Clear()
    {
        foreach (var unsubscribe in _unsubscribe)
            unsubscribe();
        _unsubscribe.Clear();
    }

    private void TrackCollection<TItem>(GraphNode node, ObservableCollection<TItem> collection, params string[] persistedProperties)
        where TItem : ObservableObject
    {
        PropertyChangedEventHandler itemHandler = (sender, args) =>
        {
            if (!_isLoading() && args.PropertyName is { } propertyName && persistedProperties.Contains(propertyName, StringComparer.Ordinal))
            {
                if (_itemChanged is not null && sender is not null) _itemChanged(node, sender, propertyName);
                else _markDirty();
            }
        };
        NotifyCollectionChangedEventHandler collectionHandler = (_, args) =>
        {
            SubscribeItems(args.NewItems, itemHandler, true);
            SubscribeItems(args.OldItems, itemHandler, false);
            if (!_isLoading() && args.Action != NotifyCollectionChangedAction.Reset)
                _markDirty();
        };
        collection.CollectionChanged += collectionHandler;
        foreach (var item in collection)
            item.PropertyChanged += itemHandler;
        _unsubscribe.Add(() =>
        {
            collection.CollectionChanged -= collectionHandler;
            foreach (var item in collection)
                item.PropertyChanged -= itemHandler;
        });
    }

    private static void SubscribeItems(System.Collections.IList? items, PropertyChangedEventHandler handler, bool subscribe)
    {
        if (items is null)
            return;
        foreach (var item in items.OfType<INotifyPropertyChanged>())
        {
            if (subscribe) item.PropertyChanged += handler;
            else item.PropertyChanged -= handler;
        }
    }

    public void Dispose() => Clear();
}
