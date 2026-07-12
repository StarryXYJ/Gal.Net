using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalNet.Control.View;
using GalNet.Core.Runtime;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Loader;
using GalNet.Runtime.Runtime;
using Serilog;

namespace GalNet.Control.ViewModels;

public sealed class GameRunViewModel : IAsyncDisposable
{
    public DefaultGameView GameView { get; }

    private readonly ISettingsService _settingsService;
    private readonly IVariableService? _variableService;
    private readonly IGameDataProvider? _gameDataProvider;
    private readonly Action? _onGameEnded;
    private readonly GameFlowOptions? _options;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Task _runTask;
    private int _stopped;

    public GameRunViewModel(
        DefaultGameView gameView,
        ISettingsService settingsService,
        IVariableService? variableService = null,
        IGameDataProvider? gameDataProvider = null,
        GameFlowOptions? options = null,
        Action? onGameEnded = null)
    {
        GameView = gameView;
        _settingsService = settingsService;
        _variableService = variableService;
        _gameDataProvider = gameDataProvider;
        _options = options;
        _onGameEnded = onGameEnded;

        _runTask = RunAsync(_lifetimeCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 0)
            _lifetimeCts.Cancel();

        try { await _runTask; }
        catch (OperationCanceledException) { }
        _lifetimeCts.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var dataDirectory = ResolveGameDataDirectory();
        var settings = new SettingsContainer();
        settings.Set(_settingsService.GetSnapshot());

        var completedNormally = false;
        try
        {
            var graph = LoadGraph(dataDirectory);
            var runtime = new GameRuntime(
                GameView,
                null,
                graph.RootNodeId,
                settings,
                _variableService);
            var engine = new GameEngine(graph, runtime);
            _options?.RuntimeCreated?.Invoke(runtime);
            _options?.GameStarted?.Invoke();
            await engine.StepAsync(cancellationToken);
            completedNormally = !cancellationToken.IsCancellationRequested;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to run game from {Directory}", dataDirectory);
            _options?.GameFailed?.Invoke(ex);
        }
        finally
        {
            GameView.Cleanup();
            if (completedNormally)
            {
                _options?.GameEnded?.Invoke();
            }
            if (completedNormally && _onGameEnded is not null)
                Avalonia.Threading.Dispatcher.UIThread.Post(_onGameEnded);
        }
    }

    private string ResolveGameDataDirectory()
    {
        // Prefer explicit override
        if (!string.IsNullOrWhiteSpace(_options?.GameDataDirectory) && Directory.Exists(_options.GameDataDirectory))
            return _options.GameDataDirectory;

        // Use DI-provided data provider
        if (_gameDataProvider is not null && Directory.Exists(_gameDataProvider.DataDirectory))
            return _gameDataProvider.DataDirectory;

        // Fallback: sample data
        if (_options?.UseSampleDataIfMissing == true)
        {
            var sampleDir = Path.Combine(AppContext.BaseDirectory, "sample");
            EnsureSampleData(sampleDir);
            return sampleDir;
        }

        throw new DirectoryNotFoundException("No valid game data directory was provided.");
    }

    private static GalNet.Core.Graph.Graph LoadGraph(string dir)
    {
        var graphPath = Path.Combine(dir, "graph.json");
        var graph = GraphLoader.LoadFromFile(graphPath);

        foreach (var group in graph.Nodes.OfType<GalNet.Core.Graph.Group>())
        {
            var groupPath = Path.Combine(dir, $"{group.Id}.galgroup");
            if (File.Exists(groupPath))
                GalgroupLoader.LoadIntoGroup(group, groupPath);
        }

        return graph;
    }

    private static void EnsureSampleData(string sampleDir)
    {
        try
        {
            Directory.CreateDirectory(sampleDir);
            foreach (var f in Directory.EnumerateFiles(sampleDir, "*.galgroup"))
                File.Delete(f);
            if (File.Exists(Path.Combine(sampleDir, "graph.json")))
                File.Delete(Path.Combine(sampleDir, "graph.json"));

            File.WriteAllText(Path.Combine(sampleDir, "graph.json"), /*lang=json*/ """
            {
              "version": 1, "name": "DemoScene", "rootNodeId": "intro",
              "nodes": [
                { "id": "intro", "type": "Group", "name": "intro", "x": 100, "y": 100 },
                { "id": "showcase", "type": "Group", "name": "showcase", "x": 400, "y": 100 },
                { "id": "end", "type": "Group", "name": "end", "x": 700, "y": 100 }
              ],
              "edges": [
                { "fromNodeId": "intro", "fromOutlet": 0, "toNodeId": "showcase" },
                { "fromNodeId": "showcase", "fromOutlet": 0, "toNodeId": "end" }
              ]
            }
            """);

            File.WriteAllText(Path.Combine(sampleDir, "intro.galgroup"), """
            text : speaker:Alice; content:Hello GalNet
            """);
            File.WriteAllText(Path.Combine(sampleDir, "showcase.galgroup"), """
            text : speaker:Alice; content:Preview sample
            """);
            File.WriteAllText(Path.Combine(sampleDir, "end.galgroup"), """
            text : speaker:Alice; content:Bye
            """);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write default sample data.");
        }
    }
}
