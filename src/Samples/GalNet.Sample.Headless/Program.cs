using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Runtime;
using GalNet.Primitives.Builtins;
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

    var presentation = new ConsolePresentation(settings.Get<GameSettings>());
    using IGameView view = new CompositeGameView(BuiltinEntryModules.CreateRecommended(presentation, presentation, presentation, presentation));

    var runtime = new GameRuntime(null, content.Graph.RootNodeId, settings, variables);
    using var engine = new GameEngine(content.Graph, runtime, view, progress, presentation);

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
        if (saveSlot >= saves.MaxSlots) throw new InvalidDataException($"Save slot must be below {saves.MaxSlots}.");
        engine.CheckpointCreated += snapshot => latestCheckpointSave = saves.SaveAsync(saveSlot, snapshot);
    }

    var advanceFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
    Task latestAdvance = Task.CompletedTask;
    presentation.AdvanceRequested += () => latestAdvance = AdvanceAsync();

    async Task AdvanceAsync()
    {
        try { await engine.AdvanceAsync(); }
        catch (Exception exception) { advanceFailure.TrySetResult(exception); }
    }

    await engine.AdvanceAsync();
    while (engine.IsRunning)
    {
        var completed = await Task.WhenAny(Task.Delay(25), advanceFailure.Task);
        if (completed == advanceFailure.Task)
            throw await advanceFailure.Task;
    }
    await latestAdvance;
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
