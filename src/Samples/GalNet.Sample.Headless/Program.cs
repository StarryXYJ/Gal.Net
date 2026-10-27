using GalNet.Contracts.Builtins.Assets;
using GalNet.Contracts.Builtins;
using GalNet.Assets;
using GalNet.Assets.Variants;
using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Gallery;
using GalNet.GameHost;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Persistence;
using GalNet.Runtime.Runtime;
using GalNet.Runtime.Variables;
using GalNet.Sample.Headless;
using System.Globalization;
using GalNet.Runtime.Engine;
using GalNet.Storage.FileSystem;

if (!HeadlessOptions.TryParse(args, out var options, out var error))
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine("Usage: GalNet.Sample.Headless <game-data-directory> [--profile <directory>] [--save-slot <n>] [--load-slot <n>]");
    return 1;
}

try
{
    var installation = await GameInstallation.OpenAsync(options!.GameDirectory);
    var resourceTypes = BuiltinResourceTypes.CreateCatalog();
    var galleryTypes = BuiltinGalleryTypes.CreateCatalog(resourceTypes);
    var content = await installation.CreateContentProvider(resourceTypes, galleryTypes, new()
    {
        Configurations = GalNet.Project.Contracts.ProjectSchemas.Configurations,
        SelectDefaultCulture = configuration => configuration.ReadDefaultOptions().TargetLocale.Code
    }).LoadAsync();
    using var assets = new AssetManager(installation.CreateAssetProviders(resourceTypes));
    assets.RegisterImageVariantsDecoder();
    var imageVariants = await assets.LoadImageVariantsCatalogAsync();
    foreach (var diagnostic in content.Localization.Diagnostics)
        Console.Error.WriteLine($"Skipped localization source '{diagnostic.RelativePath}': {diagnostic.Message}");
    var culture = CultureInfo.GetCultureInfo(content.Localization.DefaultCulture);
    var textResolver = content.Localization.CreateResolver(() => culture);

    using var player = await FilePlayerSession.OpenAsync(options.ProfileDirectory, content.Gallery,
        content.Configuration.ReadDefaultOptions().SaveSlotCount);
    var variables = player.Variables;
    var progress = player.Progress;

    var presentation = new ConsolePresentation();
    using var registration = new GameRegistration();
    registration.UseCatalog(GalNet.Project.Contracts.ProjectSchemas.CreateCatalog());
    await using var view = registration.AddBuiltinImplementations(registration.BuildCatalog(),
        presentation, presentation, presentation, presentation, content.Gallery, nvlPresenter: presentation,
        characters: content.Localization.Characters, imageVariants: imageVariants, audioConfiguration: new GalNet.Contracts.Builtins.Audio.AudioConfiguration(content.Configuration.ReadDefaultOptions().AudioTracks, content.Configuration.ReadDefaultOptions().AudioBuses)).BuildView();
    view.RequireInstructions(content.Graph.Nodes.OfType<GalNet.Core.Graph.Group>().SelectMany(group => group.Entries).Select(entry => entry.Type));

    var runtime = new GameRuntime(textResolver, content.Graph.RootNodeId, variables);
    await using var run = new GameRun(content.Graph, runtime, view, progress, presentation);
    var engine = run.Engine;

    GalNet.Core.Runtime.GameSnapshot? initialSnapshot = null;
    if (options.LoadSlot is { } loadSlot)
    {
        var snapshot = await player.LoadAsync(loadSlot, CancellationToken.None)
            ?? throw new InvalidOperationException($"Save slot {loadSlot} is empty or invalid.");
        initialSnapshot = snapshot;
        Console.WriteLine($"[Save] loaded slot {loadSlot}");
    }

    Task latestCheckpointSave = Task.CompletedTask;
    if (options.SaveSlot is { } saveSlot)
    {
        if (saveSlot >= content.Configuration.ReadDefaultOptions().SaveSlotCount) throw new InvalidDataException($"Save slot must be below {content.Configuration.ReadDefaultOptions().SaveSlotCount}.");
        engine.CheckpointCreated += snapshot => latestCheckpointSave = SaveCheckpointAsync(latestCheckpointSave, snapshot);
        async Task SaveCheckpointAsync(Task previous, GalNet.Core.Runtime.GameSnapshot snapshot)
        {
            await previous;
            await player.SaveAsync(saveSlot, snapshot, CancellationToken.None);
        }
    }

    var advanceFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
    Task latestAdvance = Task.CompletedTask;
    presentation.AdvanceRequested += () => latestAdvance = AdvanceAsync();

    async Task AdvanceAsync()
    {
        try { await engine.AdvanceAsync(); }
        catch (Exception exception) { advanceFailure.TrySetResult(exception); }
    }

    var terminal = new TaskCompletionSource<GameEngineStateChange>(TaskCreationOptions.RunContinuationsAsynchronously);
    engine.StateChanged += change =>
    {
        if (change.State is GameEngineState.Completed or GameEngineState.Faulted or GameEngineState.Stopped)
            terminal.TrySetResult(change);
    };
    try
    {
        await run.PrepareAsync(initialSnapshot);
        await run.StartPreparedAsync();
        var completed = await Task.WhenAny(terminal.Task, presentation.InputFailure, advanceFailure.Task);
        if (completed == presentation.InputFailure) throw await presentation.InputFailure;
        if (completed == advanceFailure.Task) throw await advanceFailure.Task;
        var state = await terminal.Task;
        if (state.Error is { } failure) throw failure;
        if (state.State == GameEngineState.Stopped) throw new OperationCanceledException();
    }
    finally { await Task.WhenAll(latestAdvance, latestCheckpointSave); }

    if (options.SaveSlot is { } finalSaveSlot)
    {
        await progress.FlushAsync();
        await player.SaveAsync(finalSaveSlot, engine.CreateSaveData(), CancellationToken.None);
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
