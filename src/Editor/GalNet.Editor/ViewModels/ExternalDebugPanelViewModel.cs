using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Serialization;
using GalNet.Core.Variable;
using GalNet.Debugging;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Shared.Services;
using GalNet.Runtime.Engine;

namespace GalNet.Editor.ViewModels;

/// <summary>Project-owned debug state and drafts. Closing the Dock does not dispose the active connection.</summary>
public partial class ExternalDebugPanelViewModel : ObservableObject
{
    private readonly ExternalGameDebugClient _client;
    private readonly IProjectService _projects;
    private readonly IEditorDocumentService _documents;
    private readonly IEntryCatalog _catalog;
    private readonly EditorWorkspaceViewModel _workspace;
    private readonly IFileDialogService _files;
    private readonly Dictionary<(string Group, string Primitive), string> _sources = [];
    private readonly Dictionary<(string Group, string Source), int> _positions = [];
    private readonly Dictionary<(string Group, string Source), GameDebugSpan> _spans = [];
    private readonly HashSet<(string Group, string Source)> _breakpoints = [];
    private string? _breakpointRun;
    private string? _runIdentity;
    private string? _authorSignature;
    private bool _closed;
    private readonly IDisposable? _closing;
    public IEditorLocalizationService L { get; }
    public ObservableCollection<DebugVariableRow> PlayerVariables { get; } = [];
    public ObservableCollection<DebugVariableRow> SaveVariables { get; } = [];
    public ObservableCollection<DebugChoiceRow> Choices { get; } = [];
    [ObservableProperty] private string _executable = "";
    [ObservableProperty] private string _arguments = "";
    [ObservableProperty] private string _workingDirectory = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private bool _contentChanged;
    public bool IsConnected => _client.IsConnected;
    public bool HasRun => _client.Host?.Run is not null;
    public bool CanStart => IsConnected && _client.Host?.CanStart == true && !HasRun && !Busy;
    public bool CanControl => HasRun && !Busy;
    public bool CanLocate => CanControl && !ContentChanged && _authorSignature is not null;
    public GameDebugState? RuntimeState => _client.State;

    public ExternalDebugPanelViewModel(ExternalGameDebugClient client, IProjectService projects, IEditorDocumentService documents,
        IEntryCatalog catalog, EditorWorkspaceViewModel workspace, IFileDialogService files, IEditorLocalizationService localization)
    {
        _client = client; _projects = projects; _documents = documents; _catalog = catalog; _workspace = workspace; _files = files; L = localization;
        if (projects.Current is { } project)
        {
            var settings = ExternalGameDebugClient.LoadSettings(project.RootPath);
            if (settings is not null) { Executable = settings.Executable; Arguments = string.Join(Environment.NewLine, settings.Arguments); WorkingDirectory = settings.WorkingDirectory ?? ""; }
            _closing = project.RegisterClosingCallback(CloseProjectAsync);
        }
        _client.Changed += OnClientChanged;
        _documents.DocumentChanged += OnAuthorChanged;
        Refresh();
    }
    partial void OnBusyChanged(bool value) => NotifyControls();
    partial void OnContentChangedChanged(bool value) => NotifyControls();
    private void NotifyControls()
    {
        OnPropertyChanged(nameof(IsConnected)); OnPropertyChanged(nameof(HasRun)); OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanControl)); OnPropertyChanged(nameof(CanLocate));
    }
    private void OnClientChanged() => Dispatcher.UIThread.Post(() => { if (!_closed) Refresh(); });
    private void OnAuthorChanged()
    {
        if (_authorSignature is not null) ContentChanged = Signature() != _authorSignature;
        UpdateHighlights();
    }
    private void Refresh()
    {
        Status = _client.Status;
        if (_client.Error is { } error) Error = error;
        var run = _client.Host?.Run;
        var state = _client.State;
        if (state?.Error is { } runtimeError) Error = runtimeError;
        var identity = run is null ? null : run.RunId + ":" + run.Generation;
        if (_runIdentity != identity)
        {
            foreach (var row in PlayerVariables.Concat(SaveVariables)) row.Invalidate();
            PlayerVariables.Clear(); SaveVariables.Clear(); Choices.Clear(); _runIdentity = identity;
        }
        if (state is not null && run is not null)
        {
            if (_breakpointRun != run.RunId)
            {
                if (_breakpoints.Count == 0) _breakpointRun = run.RunId;
                else if (!Busy) _ = SynchronizeBreakpointsAsync(run.RunId);
            }
            UpdateVariables(PlayerVariables, state.Variables.Where(variable => variable.Scope == VariableScope.Player), run);
            UpdateVariables(SaveVariables, state.Variables.Where(variable => variable.Scope == VariableScope.Save), run);
            if (state.Choice is { } choice)
            {
                if (Choices.FirstOrDefault()?.ChoiceId != choice.Id)
                {
                    Choices.Clear();
                    for (var index = 0; index < choice.Options.Length; index++)
                        Choices.Add(new(choice.Id, choice.Options[index], index, this, run.RunId, run.Generation));
                }
            }
            else Choices.Clear();
        }
        else if (run is null)
        {
            foreach (var row in PlayerVariables.Concat(SaveVariables)) row.Invalidate();
            PlayerVariables.Clear(); SaveVariables.Clear(); Choices.Clear();
        }
        UpdateHighlights(); NotifyControls(); OnPropertyChanged(nameof(RuntimeState));
    }
    private void UpdateVariables(ObservableCollection<DebugVariableRow> rows, IEnumerable<GameDebugVariable> values, DebugRunInfo run)
    {
        var snapshot = values.ToArray();
        foreach (var row in rows.Where(row => !snapshot.Any(variable => variable.Uid == row.Uid)).ToArray()) { row.Invalidate(); rows.Remove(row); }
        foreach (var variable in snapshot.OrderBy(variable => variable.Name, StringComparer.Ordinal))
        {
            var row = rows.FirstOrDefault(row => row.Uid == variable.Uid);
            if (row is null) rows.Add(new(this, run.RunId, run.Generation, variable));
            else row.Refresh(variable);
        }
    }
    private void UpdateHighlights()
    {
        var state = !ContentChanged && _authorSignature is not null ? _client.State : null;
        foreach (var node in _workspace.Nodes)
        {
            node.IsDebugCurrent = state?.NodeId == node.Id;
            var active = state?.ActiveEntries?.Where(item => item.GroupId == node.Id)
                .Select(item => _sources.GetValueOrDefault((node.Id, item.EntryId))).Where(id => id is not null).ToHashSet() ?? [];
            if (state?.NodeId == node.Id && active.Count == 0 && state.Groups.FirstOrDefault(group => group.Id == node.Id) is { } group && state.EntryIndex < group.Entries.Length)
                active.Add(_sources.GetValueOrDefault((node.Id, group.Entries[state.EntryIndex])));
            foreach (var entry in node.Entries)
            {
                entry.IsDebugCurrent = active.Contains(entry.StableId);
                entry.IsDebugBreakpoint = _breakpoints.Contains((node.Id, entry.StableId));
            }
        }
    }
    [RelayCommand] private async Task BrowseAsync()
    {
        var selected = await _files.OpenFilePickerAsync(L["Debug.Executable"], false);
        if (selected.FirstOrDefault() is { } path) Executable = path;
    }
    [RelayCommand] private void SaveConfiguration()
    {
        try
        {
            var project = _projects.Current ?? throw new InvalidOperationException("No project.");
            ExternalGameDebugClient.SaveSettings(project.RootPath, new(Executable.Trim(), Arguments.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
                string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory.Trim()));
            Error = "";
        }
        catch (Exception error) { Error = error.Message; }
    }
    [RelayCommand] private Task LaunchAsync() => RunAsync(async () =>
    {
        if (!File.Exists(Executable))
        {
            await BrowseAsync();
            if (!File.Exists(Executable)) return;
        }
        SaveConfiguration();
        var root = await PrepareAsync();
        _client.Launch(new(Executable, Arguments.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
            string.IsNullOrWhiteSpace(WorkingDirectory) ? null : WorkingDirectory), root, Path.Combine(_projects.Current!.RootPath, ".galnet", "debug-profile"));
    });
    [RelayCommand] private Task WaitForApplicationAsync() => RunAsync(async () =>
    {
        await PrepareAsync();
        var invitation = _client.Invitation!;
        Status = $"{Status} — --debug-port {invitation.Port} --debug-session {invitation.SessionId} --debug-project {invitation.Identity.ProjectId} --debug-token {invitation.Token}";
    });
    private async Task<string> PrepareAsync()
    {
        await _client.DisconnectAsync();
        await _projects.SaveAsync();
        var project = _projects.Current ?? throw new InvalidOperationException("No project.");
        if (ExternalGameDebugClient.LoadSettings(project.RootPath) is { } launch)
        {
            Executable = launch.Executable; Arguments = string.Join(Environment.NewLine, launch.Arguments); WorkingDirectory = launch.WorkingDirectory ?? "";
        }
        var output = Path.Combine(project.RootPath, "Output", "Debug");
        await new ProjectContentBuilder(_catalog).BuildAsync(project.RootPath, output);
        _sources.Clear(); _positions.Clear(); _spans.Clear(); _breakpointRun = null;
        var repository = new EditorDocumentRepository(_catalog);
        foreach (var group in _documents.Groups.Values)
        {
            var result = GalgroupCompiler.Compile(new GroupDocument { Kind = GroupDocumentKind.Raw, Entries = group.Entries.Select(repository.SerializeEntry).ToList() }, _catalog);
            var offset = 0;
            foreach (var entry in group.Entries)
            {
                _positions[(group.Id, entry.StableId)] = offset;
                foreach (var primitive in result.SourceMap[entry.StableId]) { _sources[(group.Id, primitive)] = entry.StableId; offset++; }
                _spans[(group.Id, entry.StableId)] = new(group.Id, _positions[(group.Id, entry.StableId)], offset);
            }
        }
        _authorSignature = Signature(); ContentChanged = false;
        _client.Listen(new(project.Id, DebugContentIdentity.Compute(output)));
        return output;
    }
    private string Signature() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        Graph = _documents.CurrentDocument,
        Groups = _documents.Groups.Select(pair => new { pair.Key, Entries = pair.Value.Entries.Select(entry => new { entry.StableId, entry.Type, entry.Condition, entry.Parameters }) }).ToArray()
    }))));
    [RelayCommand] private Task StartGameAsync() => CommandAsync("start");
    [RelayCommand] private Task StopGameAsync() => CommandAsync("stop");
    [RelayCommand] private Task PauseAsync() => CommandAsync("pause");
    [RelayCommand] private Task ResumeAsync() => CommandAsync("resume");
    [RelayCommand] private Task StepAsync() => RunAsync(async () =>
    {
        if (ContentChanged) throw new InvalidOperationException(L["Debug.ContentChanged"]);
        var state = _client.State ?? throw new InvalidOperationException("No active game.");
        var span = _spans.Values.FirstOrDefault(span => span.GroupId == state.NodeId && span.Start <= state.EntryIndex && state.EntryIndex < span.End)
            ?? throw new InvalidOperationException("There is no next author entry in this Group. Continue to leave it.");
        await _client.SendAsync("step", span);
    });
    [RelayCommand] private Task GroupEndAsync() => CommandAsync("groupEnd");
    [RelayCommand] private Task DisconnectAsync() => RunAsync(_client.DisconnectAsync);
    [RelayCommand] private async Task CancelAsync() { try { await _client.SendAsync("cancel"); } catch (Exception error) { Error = error.Message; } }
    internal Task CommandAsync(string method, object? arguments = null, string? run = null, long? generation = null) =>
        RunAsync(async () => { await _client.SendAsync(method, arguments, run, generation); });
    private async Task RunAsync(Func<Task> action)
    {
        if (Busy || _closed) return;
        Busy = true; Error = "";
        try { await action(); }
        catch (Exception error) { Error = error.Message; }
        finally { Busy = false; }
    }
    public Task ToggleBreakpointAsync(string group, string source) => RunAsync(async () =>
    {
        if (ContentChanged || !_spans.ContainsKey((group, source))) throw new InvalidOperationException(L["Debug.ContentChanged"]);
        var key = (group, source);
        if (!_breakpoints.Remove(key)) _breakpoints.Add(key);
        try { await SendBreakpointsAsync(); }
        catch { if (!_breakpoints.Remove(key)) _breakpoints.Add(key); throw; }
        UpdateHighlights();
    });
    private Task SendBreakpointsAsync() => _client.SendAsync("breakpoints", ContentChanged ? Array.Empty<GameDebugSpan>() :
        _breakpoints.Where(key => _spans.ContainsKey(key)).Select(key => _spans[key]).ToArray());
    private Task SynchronizeBreakpointsAsync(string run) => RunAsync(async () => { await SendBreakpointsAsync(); _breakpointRun = run; });
    public Task SeekAsync(string group, string? sourceEntry = null, bool initialScene = false) => RunAsync(async () =>
    {
        if (ContentChanged || _authorSignature is null) throw new InvalidOperationException(L["Debug.ContentChanged"]);
        var state = _client.State ?? throw new InvalidOperationException("No active game.");
        var index = sourceEntry is null ? 0 : _positions.GetValueOrDefault((group, sourceEntry), -1);
        if (index < 0) throw new InvalidOperationException("The author entry is not mapped to this game content.");
        var span = sourceEntry is null ? _spans.Values.FirstOrDefault(item => item.GroupId == group && item.Start == 0 && item.End > 0) : _spans.GetValueOrDefault((group, sourceEntry));
        if (initialScene)
        {
            await _client.SendAsync("seek", new GameDebugSeek(group, index, [], span?.End, InitialScene: true));
            return;
        }
        var edges = _documents.CurrentDocument.Edges.Select(edge => new GameDebugRouteEdge(edge.FromNodeId, edge.FromOutlet, edge.ToNodeId)).ToArray();
        var plan = DebugRoutePlanner.Plan(state.NodeId, group, edges, _workspace.SelectedNodes.Select(node => node.Id).ToArray());
        if (plan.Routes.Length == 0 && state.CachedGroups.Contains(group)) plan = new([[]], false, false);
        if (plan.Routes.Length == 0) plan = DebugRoutePlanner.Plan(_documents.CurrentDocument.RootNodeId, group, edges);
        if (plan.Routes.Length == 0) throw new InvalidOperationException(plan.Truncated ? L["Debug.RouteLimit"] : L["Debug.NoRoute"]);
        var route = plan.Routes.Length == 1 && !plan.Truncated ? plan.Routes[0] : await SelectRouteAsync(plan);
        if (route is not null) await _client.SendAsync("seek", new GameDebugSeek(group, index, route, span?.End));
    });
    private async Task<GameDebugRouteEdge[]?> SelectRouteAsync(DebugRoutePlan plan)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return null;
        var dialog = new Window { Title = L["Debug.SelectRoute"], Width = 620, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var list = new ListBox { ItemsSource = plan.Routes.Select(route => string.Join(" → ", route.Select(edge => $"{_workspace.Nodes.FirstOrDefault(node => node.Id == edge.From)?.Name ?? edge.From} [{edge.Outlet}]")
            .Append(_workspace.Nodes.FirstOrDefault(node => node.Id == route.LastOrDefault()?.To)?.Name ?? route.LastOrDefault()?.To ?? ""))).ToArray(), SelectedIndex = 0 };
        var confirm = new Button { Content = L["Debug.Jump"], HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        confirm.Click += (_, _) => dialog.Close(list.SelectedIndex);
        var layout = new DockPanel { Margin = new Thickness(12), LastChildFill = true };
        if (plan.Truncated)
        {
            var warning = new TextBlock { Text = L["Debug.RouteLimit"], TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            DockPanel.SetDock(warning, Avalonia.Controls.Dock.Top); layout.Children.Add(warning);
        }
        DockPanel.SetDock(confirm, Avalonia.Controls.Dock.Bottom); layout.Children.Add(confirm); layout.Children.Add(list); dialog.Content = layout;
        var selected = await dialog.ShowDialog<int?>(owner);
        return selected is { } index && (uint)index < plan.Routes.Length ? plan.Routes[index] : null;
    }
    private async Task CloseProjectAsync()
    {
        _closed = true; _client.Changed -= OnClientChanged; _documents.DocumentChanged -= OnAuthorChanged;
        foreach (var row in PlayerVariables.Concat(SaveVariables)) row.Invalidate();
        await _client.DisconnectAsync(); _closing?.Dispose();
    }
}

public partial class DebugVariableRow : ObservableObject
{
    private readonly ExternalDebugPanelViewModel _owner;
    private readonly string _run;
    private readonly long _generation;
    private GameDebugVariable _expected;
    private GameDebugVariable _current;
    private bool _valid = true;
    public string Uid => _current.Uid;
    public string Name => _current.Name;
    public string Type => _current.Type.ToString();
    public string CurrentValue => _current.Value;
    public bool Conflict => _current != _expected;
    [ObservableProperty] private string _draft;
    public DebugVariableRow(ExternalDebugPanelViewModel owner, string run, long generation, GameDebugVariable value)
    { _owner = owner; _run = run; _generation = generation; _current = _expected = value; _draft = value.Value; }
    public void Refresh(GameDebugVariable value)
    {
        var clean = Draft == _expected.Value;
        _current = value;
        if (clean) { _expected = value; Draft = value.Value; }
        OnPropertyChanged(nameof(CurrentValue)); OnPropertyChanged(nameof(Conflict));
    }
    public void Invalidate() => _valid = false;
    [RelayCommand] private async Task ApplyAsync()
    {
        if (!_valid || _owner.Busy) return;
        await _owner.CommandAsync("setVariable", new DebugVariableEdit(_expected, Draft), _run, _generation);
        if (_owner.Error.Length == 0 && _valid) _expected = _expected with { Value = Draft };
    }
    [RelayCommand] private void Reset() { _expected = _current; Draft = _current.Value; OnPropertyChanged(nameof(Conflict)); }
}
public sealed record DebugChoiceRow(string ChoiceId, string Text, int Index, ExternalDebugPanelViewModel Owner, string Run, long Generation)
{
    public IAsyncRelayCommand ChooseCommand => new AsyncRelayCommand(() => Owner.CommandAsync("choose", new DebugChoiceSelection(ChoiceId, Index), Run, Generation));
}
