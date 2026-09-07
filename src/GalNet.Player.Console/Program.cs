using GalNet.Core.Graph;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Player.ConsoleHost;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Loader;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: GalNet.Player.Console <game-data-directory>");
    return 1;
}

var gameDataDirectory = Path.GetFullPath(args[0]);
var graphPath = Path.Combine(gameDataDirectory, "graph.json");
if (!File.Exists(graphPath))
{
    Console.Error.WriteLine($"graph.json was not found: {graphPath}");
    return 1;
}

try
{
    var graph = GraphLoader.LoadFromFile(graphPath);
    foreach (var group in graph.Nodes.OfType<Group>())
    {
        var groupPath = Path.Combine(gameDataDirectory, $"{group.Id}.galgroup");
        if (File.Exists(groupPath))
            GalgroupLoader.LoadIntoGroup(group, groupPath);
    }

    var settings = new SettingsContainer();
    settings.Set(new GameSettings());

    // The host owns composition. The engine receives only this completed presentation facade.
    var consoleServices = new ConsolePresentation(settings.Get<GameSettings>()!);
    IGameView view = new CompositeGameView(
        consoleServices,
        consoleServices,
        consoleServices,
        consoleServices,
        consoleServices,
        consoleServices,
        consoleServices,
        consoleServices);

    var engine = new GameEngine(graph, view, settings: settings);
    await engine.StepAsync();
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Game session cancelled.");
    return 2;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Game session failed: {exception.Message}");
    return 1;
}
