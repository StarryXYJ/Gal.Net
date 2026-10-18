using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public sealed partial class TimelineTreeNode(string key, Func<string> label, TimelineValueRow? value = null) : ObservableObject
{
    internal string Key { get; } = key;
    public string Label => label();
    public TimelineValueRow? Value { get; } = value;
    public bool IsLeaf => Value is not null;
    public bool IsBranch => !IsLeaf;
    public int Depth { get; internal set; }
    public double Indent => Depth * 16;
    public ObservableCollection<TimelineTreeNode> Children { get; } = [];
    [ObservableProperty] private bool _isExpanded = true;
    public string ExpandGlyph => IsExpanded ? "▾" : "▸";
    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(ExpandGlyph));
    internal IEnumerable<TimelineTreeNode> Descendants() => new[] { this }.Concat(Children.SelectMany(node => node.Descendants()));
    internal void RefreshLabel() => OnPropertyChanged(nameof(Label));
}

public sealed partial class TimelineEditSession
{
    private readonly Dictionary<string, bool> _expansion = new(StringComparer.Ordinal);
    public ObservableCollection<TimelineTreeNode> Tree { get; } = [];
    private void NotifyState() { OnPropertyChanged(nameof(HasNoRows)); OnPropertyChanged(nameof(AnalysisIncomplete)); }
    private void SaveExpansion()
    {
        foreach (var node in Tree.SelectMany(node => node.Descendants())) _expansion[node.Key] = node.IsExpanded;
        if (_expansion.Count > 2000) _expansion.Clear();
    }
    private string ObjectName(TimelineValueRow row, int number)
    {
        if (row.Target is not { } target || target.Kind == SceneTargetKind.Unresolved) return _localization.Format("Timeline.Unresolved", number);
        var group = _documents.CurrentDocument.Nodes.FirstOrDefault(node => node.Id == target.Locator.GroupId);
        var entry = _documents.Groups.TryGetValue(target.Locator.GroupId, out var document) ? document.Entries.FirstOrDefault(item => item.StableId == target.Locator.EntryId) : null;
        var source = (group?.Name ?? "") + " / " + entry?.Id;
        return _localization["Timeline.Kind." + target.Kind] + " · " + source + " · " + _localization[target.Presence == TargetPresence.Definite && _document?.HasEvents != true ? "Timeline.Definite" : "Timeline.Possible"];
    }
    private void BuildTree()
    {
        var number = 0;
        foreach (var rows in Rows.GroupBy(row => row.Handle))
        {
            var ordinal = ++number; var first = rows.First();
            var root = new TimelineTreeNode(rows.Key, () => ObjectName(first, ordinal)); Tree.Add(root);
            foreach (var row in rows)
            {
                var segments = row.Property.Split('.'); var parent = root; var path = root.Key;
                for (var index = 0; index < segments.Length - 1; index++)
                {
                    var segment = segments[index]; path += "/" + segment;
                    var node = parent.Children.FirstOrDefault(child => child.Key == path);
                    if (node is null) { node = new(path, () => PropertyName(segment)); parent.Children.Add(node); }
                    parent = node;
                }
                parent.Children.Add(new(path + "/" + segments[^1], () => PropertyName(row.Property), row));
            }
        }
        foreach (var node in Tree.SelectMany(node => node.Descendants()))
        { if (_expansion.TryGetValue(node.Key, out var expanded)) node.IsExpanded = expanded; node.PropertyChanged += OnTreeExpansion; }
    }
    private string PropertyName(string property)
    {
        var key = "Timeline.Property." + property; var text = _localization[key]; return text == "[" + key + "]" ? property : text;
    }
    internal bool Remove(TimelineValueRow row, int generation, int index)
    {
        if (_disposed || generation != _generation || !Rows.Contains(row) || _document is null) return false;
        return Submit(_document.Remove(index));
    }
}
