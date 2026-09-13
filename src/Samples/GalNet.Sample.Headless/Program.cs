using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Handlers;
using GalNet.Runtime.Runtime;
using GalNet.Sample.Headless;
using GalNet.Storage.FileSystem;

if (!HeadlessOptions.TryParse(args, out var options, out var error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine("Usage: GalNet.Sample.Headless <game-data-directory> [--profile <directory>] [--save-slot <n>] [--load-slot <n>]");
    return 1;
}

try
{
    var content = await new DirectoryGameContentProvider(options!.GameDirectory).LoadAsync();
    var settings = new SettingsContainer();
    settings.Set(new GameSettings());

    var saves = new FileSaveService(options.ProfileDirectory);
    var variables = await FileVariableService.CreateAsync(new FilePlayerVariableStore(options.ProfileDirectory));
    var progress = new FileGameProgressService(options.ProfileDirectory);

    var consoleServices = new ConsolePresentation(settings.Get<GameSettings>());
    IGameView view = new CompositeGameView(
        consoleServices,
        consoleServices,
        consoleServices,
        consoleServices,
        consoleServices,
        consoleServices,
        consoleServices,
        consoleServices,
        consoleServices);

    var runtime = new GameRuntime(null, content.Graph.RootNodeId, settings, variables);
    var engine = new GameEngine(
        content.Graph,
        runtime,
        view,
        EntryHandlerRegistry.CreateDefault(progress),
        progress);

    if (options.LoadSlot is { } loadSlot)
    {
        var snapshot = await saves.LoadAsync(loadSlot)
            ?? throw new InvalidOperationException($"Save slot {loadSlot} is empty or invalid.");
        engine.RestoreFrom(snapshot);
        Console.WriteLine($"[Save] loaded slot {loadSlot}");
    }

    Task latestCheckpointSave = Task.CompletedTask;
    if (options.SaveSlot is { } saveSlot)
    {
        if (saveSlot >= saves.MaxSlots) throw new ArgumentOutOfRangeException(nameof(options.SaveSlot), $"Slot must be below {saves.MaxSlots}.");
        engine.CheckpointCreated += snapshot => latestCheckpointSave = saves.SaveAsync(saveSlot, snapshot);
    }

    await engine.StepAsync();
    await latestCheckpointSave;

    if (options.SaveSlot is { } finalSaveSlot)
    {
        await saves.SaveAsync(finalSaveSlot, engine.CreateSaveData());
        Console.WriteLine($"[Save] wrote slot {finalSaveSlot}");
    }

    await variables.FlushPlayerVariablesAsync();
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
