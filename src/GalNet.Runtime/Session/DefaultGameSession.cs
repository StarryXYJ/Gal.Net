using GalNet.Core.Graph;
using GalNet.Core.Services;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Loader;

namespace GalNet.Runtime.Session;

/// <summary>
/// 默认游戏会话实现。
/// 从指定目录加载 graph.json + *.galgroup，构造 GameEngine 并运行到结束。
/// 开发期可传入 sampleDir（含样本文件），上线后替换为资源服务即可。
/// </summary>
public sealed class DefaultGameSession : IGameSession
{
    private readonly IGameView _view;
    private readonly SettingsContainer _settings;
    private readonly string _gameDataDir;

    public DefaultGameSession(IGameView view, SettingsContainer settings, string gameDataDir)
    {
        _view = view;
        _settings = settings;
        _gameDataDir = gameDataDir;
    }

    public async Task RunAsync(Action? onEnded = null, CancellationToken ct = default)
    {
        await Task.Run(async () =>
        {
            try
            {
                var graph = LoadGraph(_gameDataDir);
                var engine = new GameEngine(graph, _view, null, _settings);

                try
                {
                    await engine.StepAsync(ct);
                    Console.WriteLine("Info: Game ended normally.");
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("Info: Game session cancelled.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: Engine error during game session. {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: DefaultGameSession failed to load game data. {ex.Message}");
            }

            if (onEnded != null)
                onEnded();
        }, ct);
    }

    // ── 私有加载工具 ──

    private static Graph LoadGraph(string dir)
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
}
