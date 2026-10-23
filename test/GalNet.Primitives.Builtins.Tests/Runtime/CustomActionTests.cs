using System.Text.Json;
using GalNet.Contracts.Builtins;
using GalNet.Core.Compilation;
using GalNet.Core.Entry;
using GalNet.Core.Graph;
using GalNet.Core.Primitives;
using GalNet.Core.Serialization;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GalNet.Runtime.Runtime;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace GeneralTest.Runtime;

[NonParallelizable]
public sealed class CustomActionTests
{
    private static readonly string[] ParameterNames = ["action", "argument"];
    private static PrimitiveEntry Invoke(string action = "project.action", string argument = "", string? batchId = "batch") =>
        new(BuiltinEntryIds.InvokeCustom, JsonSerializer.SerializeToElement(new { action, argument }), batchId)
        { Id = 7, StableId = "call#1" };

    private static CompositeGameView View(BuiltinCustomActionModule actions) => new GameRegistration()
        .RegisterSchema(BuiltinSchemas.CreateCustomActionsModule()).AddBuiltinCustomActions(actions).BuildView();

    [TestCase(PrimitiveInvocationMode.Normal, "execute")]
    [TestCase(PrimitiveInvocationMode.Quick, "quick")]
    [TestCase(PrimitiveInvocationMode.CatchUp, "catchup")]
    public void Immediate_selects_exactly_one_delegate_and_preserves_the_opaque_payload(PrimitiveInvocationMode mode, string expected)
    {
        const string payload = " { not JSON }\n自定义语法: a=1; \"quoted\" ";
        var calls = new List<string>();
        var actions = new BuiltinCustomActionModule().RegisterImmediate("project.action",
            value => calls.Add("execute:" + value), value => calls.Add("quick:" + value), value => calls.Add("catchup:" + value));
        using var view = View(actions);
        var instance = view.Dispatch(Invoke(argument: payload), new GameRuntime(null), default, mode);
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(new[] { expected + ":" + payload }));
            Assert.That(instance.IsCompleted, Is.True);
            Assert.That(instance.IsBlocking, Is.False);
            Assert.That(instance.IsSkippable, Is.False);
            Assert.That(instance.BatchId, Is.EqualTo("batch"));
        });
    }

    [TestCase(PrimitiveInvocationMode.Normal, 1)]
    [TestCase(PrimitiveInvocationMode.Quick, 1)]
    [TestCase(PrimitiveInvocationMode.CatchUp, 0)]
    public void Immediate_defaults_execute_in_quick_and_do_nothing_in_catchup(PrimitiveInvocationMode mode, int expected)
    {
        var calls = 0;
        using var view = View(new BuiltinCustomActionModule().RegisterImmediate("project.action", argument =>
        { Assert.That(argument, Is.Empty); calls++; }));
        view.Dispatch(Invoke(), new GameRuntime(null), default, mode);
        Assert.That(calls, Is.EqualTo(expected));
    }

    [Test]
    public void Contextful_handlers_receive_independent_calls_and_can_use_runtime_services()
    {
        var contexts = new List<PrimitiveCreateContext>();
        using var cancellation = new CancellationTokenSource();
        using var view = View(new BuiltinCustomActionModule().RegisterImmediate("project.action", (context, argument) =>
        {
            contexts.Add(context);
            context.Runtime.SetVariable("score", int.Parse(argument, System.Globalization.CultureInfo.InvariantCulture));
        }));
        var runtime = new GameRuntime(null);
        var first = view.Dispatch(Invoke(argument: "2"), runtime, cancellation.Token);
        var second = view.Dispatch(Invoke(argument: "7"), runtime, cancellation.Token);
        Assert.Multiple(() =>
        {
            Assert.That(runtime.GetVariable("score")!.AsInt(), Is.EqualTo(7));
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(contexts[0], Is.Not.SameAs(contexts[1]));
            Assert.That(contexts.All(context => context.ScopeCancellation == cancellation.Token), Is.True);
        });
    }

    [Test]
    public void Unknown_actions_are_ordinal_log_location_without_payload_and_complete_in_every_mode()
    {
        var sink = new Sink();
        var previous = Log.Logger;
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        Log.Logger = logger;
        try
        {
            using var view = new GameRegistration().AddBuiltins(customActions: new BuiltinCustomActionModule()
                .RegisterImmediate("project.Action", _ => Assert.Fail("The key must match exactly."))).BuildView();
            view.RequireExecutionCoverage();
            foreach (var mode in Enum.GetValues<PrimitiveInvocationMode>())
            {
                var instance = view.Dispatch(Invoke(argument: "private-payload"), new GameRuntime(null, "group"), default, mode);
                Assert.That(instance.IsCompleted, Is.True);
            }
            Assert.That(sink.Events, Has.Count.EqualTo(3));
            foreach (var item in sink.Events)
            {
                Assert.That(item.Level, Is.EqualTo(LogEventLevel.Warning));
                var message = item.RenderMessage(System.Globalization.CultureInfo.InvariantCulture);
                Assert.That(message, Does.Contain("project.action").And.Contain("group").And.Contain("call#1"));
                Assert.That(message, Does.Not.Contain("private-payload"));
            }
        }
        finally { Log.Logger = previous; }
    }

    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public void Strict_missing_actions_fail_in_every_mode(PrimitiveInvocationMode mode)
    {
        using var view = View(new BuiltinCustomActionModule(strictMissingActions: true));
        Assert.That(() => view.Dispatch(Invoke(), new GameRuntime(null), default, mode),
            Throws.InvalidOperationException.With.Message.Contains("project.action"));
    }

    [Test]
    public async Task Synchronous_handler_failures_fault_the_engine()
    {
        using var view = View(new BuiltinCustomActionModule().RegisterImmediate("project.action", _ => throw new IOException("sync failed")));
        var group = new Group { Id = "group", Entries = [Invoke(batchId: null)] };
        await using var engine = new GameEngine(new Graph { RootNodeId = group.Id, Nodes = [group] }, view);
        Assert.ThrowsAsync<IOException>(() => engine.AdvanceAsync());
        Assert.That(engine.State, Is.EqualTo(GameEngineState.Faulted));
    }

    [Test]
    public void Selected_builtin_registration_keeps_a_partial_catalog_partial()
    {
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateCustomActionsModule()]);
        var called = false;
        using var view = new GameRegistration().UseCatalog(catalog).AddBuiltinImplementations(catalog,
            customActions: new BuiltinCustomActionModule().RegisterImmediate("project.action", _ => called = true)).BuildView();
        view.RequireExecutionCoverage();
        view.Dispatch(Invoke(), new GameRuntime(null), default);
        Assert.That(called, Is.True);
        Assert.That(view.CanExecute(BuiltinEntryIds.SetVariable), Is.False);
    }

    [TestCase(PrimitiveInvocationMode.Normal, false, "execute")]
    [TestCase(PrimitiveInvocationMode.Quick, false, "execute")]
    [TestCase(PrimitiveInvocationMode.CatchUp, false, "")]
    [TestCase(PrimitiveInvocationMode.Quick, true, "quick")]
    [TestCase(PrimitiveInvocationMode.CatchUp, true, "catchup")]
    public async Task Async_delegates_use_the_same_mode_defaults(PrimitiveInvocationMode mode, bool overrides, string expected)
    {
        var called = "";
        var actions = new BuiltinCustomActionModule().RegisterAsync("project.action", (_, _) =>
        { called = "execute"; return Task.CompletedTask; },
            quick: overrides ? (_, _) => { called = "quick"; return Task.CompletedTask; }
        : null,
            catchUp: overrides ? (_, _) => { called = "catchup"; return Task.CompletedTask; }
        : null);
        using var view = View(actions);
        var instance = view.Dispatch(Invoke(), new GameRuntime(null), default, mode);
        await instance.PresentationCompletion;
        Assert.That(called, Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Async_operation_tracks_completion_and_forwards_scope_cancellation(bool blocking)
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actions = new BuiltinCustomActionModule().RegisterAsync("project.action", async (context, argument, token) =>
        {
            Assert.That(context.ScopeCancellation, Is.EqualTo(cancellation.Token));
            Assert.That(token.CanBeCanceled, Is.True);
            Assert.That(argument, Is.EqualTo("resource-id"));
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }, blocking);
        using var view = View(actions);
        var instance = view.Dispatch(Invoke(argument: "resource-id"), new GameRuntime(null), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Multiple(() =>
        {
            Assert.That(instance.IsBlocking, Is.EqualTo(blocking));
            Assert.That(instance.IsCompleted, Is.False);
            Assert.That(instance.PresentationCompletion.IsCompleted, Is.False);
            Assert.That(instance.BatchId, Is.EqualTo("batch"));
        });
        cancellation.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(() => instance.PresentationCompletion);
    }

    [Test]
    public async Task Async_view_disposal_cancels_and_waits_for_handler_cleanup()
    {
        var cleaning = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actions = new BuiltinCustomActionModule().RegisterAsync("project.action", async (_, token) =>
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { cleaning.SetResult(); await release.Task; }
        });
        var view = View(actions);
        var instance = view.Dispatch(Invoke(), new GameRuntime(null), default);
        var disposing = view.DisposeAsync().AsTask();
        try
        {
            await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(disposing.IsCompleted, Is.False);
            Assert.That(instance.PresentationCompletion.IsCompleted, Is.False);
        }
        finally { release.TrySetResult(); await disposing; }
        Assert.ThrowsAsync<TaskCanceledException>(() => instance.PresentationCompletion);
        await actions.DisposeAsync();
    }

    [Test]
    public async Task Engine_waits_for_blocking_actions_then_continues_the_story()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var after = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var view = View(new BuiltinCustomActionModule()
            .RegisterAsync("wait", (_, token) => release.Task.WaitAsync(token), blocking: true)
            .RegisterImmediate("after", _ => after.TrySetResult()));
        var group = new Group { Id = "group", Entries = [Invoke("wait", batchId: null), Invoke("after", batchId: null)] };
        await using var engine = new GameEngine(new Graph { RootNodeId = group.Id, Nodes = [group] }, view);
        await engine.AdvanceAsync();
        Assert.That(after.Task.IsCompleted, Is.False);
        release.SetResult();
        await after.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Engine_reports_async_action_failures_instead_of_advancing(bool immediate)
    {
        var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var after = false;
        using var view = View(new BuiltinCustomActionModule()
            .RegisterAsync("fail", (_, _) => immediate ? Task.FromException(new IOException("action failed")) : failure.Task, blocking: true)
            .RegisterImmediate("after", _ => after = true));
        var group = new Group { Id = "group", Entries = [Invoke("fail", batchId: null), Invoke("after", batchId: null)] };
        await using var engine = new GameEngine(new Graph { RootNodeId = group.Id, Nodes = [group] }, view);
        var fault = new TaskCompletionSource<GameEngineStateChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.StateChanged += change => { if (change.State == GameEngineState.Faulted) fault.TrySetResult(change); };
        if (immediate) Assert.ThrowsAsync<IOException>(() => engine.AdvanceAsync());
        else { await engine.AdvanceAsync(); failure.SetException(new IOException("action failed")); }
        Assert.That((await fault.Task.WaitAsync(TimeSpan.FromSeconds(3))).Error!.Message, Is.EqualTo("action failed"));
        Assert.That(after, Is.False);
    }

    [Test]
    public void Factory_instances_keep_their_own_mode_skip_and_completion_semantics()
    {
        var instances = new List<Skippable>();
        using var view = View(new BuiltinCustomActionModule().RegisterFactory("project.action", (context, argument) =>
        {
            Assert.That(argument, Is.EqualTo("arg"));
            var instance = new Skippable(context.BatchId);
            instances.Add(instance);
            return instance;
        }));
        var instance = view.Dispatch(Invoke(argument: "arg"), new GameRuntime(null), default, PrimitiveInvocationMode.CatchUp);
        Assert.That(instance, Is.SameAs(instances.Single()));
        Assert.That(instance.IsBlocking, Is.True);
        Assert.That(instance.IsCompleted, Is.False);
        instance.Skip();
        Assert.That(instance.IsCompleted, Is.True);
    }

    [Test]
    public void Registration_rejects_duplicates_blank_ids_and_changes_after_composition_or_disposal()
    {
        var actions = new BuiltinCustomActionModule().RegisterImmediate("project.action", _ => { });
        Assert.That(() => actions.RegisterImmediate("project.action", _ => { }), Throws.InvalidOperationException);
        Assert.That(() => actions.RegisterImmediate(" ", _ => { }), Throws.ArgumentException);
        using var view = View(actions);
        Assert.That(() => actions.RegisterImmediate("late", _ => { }), Throws.InvalidOperationException);
        var disposed = new BuiltinCustomActionModule();
        disposed.Dispose();
        Assert.That(() => disposed.RegisterImmediate("late", _ => { }), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void Compiler_accepts_unknown_action_ids_and_preserves_payload_without_project_specific_contracts()
    {
        var catalog = new TargetProfileEntryCatalog([BuiltinSchemas.CreateCustomActionsModule()]);
        const string argument = "not JSON;\nfree syntax";
        var source = new GroupDocument
        {
            Entries = [new() { Id = "call", Type = BuiltinEntryIds.InvokeCustom,
            Parameters = new() { ["action"] = JsonSerializer.SerializeToElement("unregistered"), ["argument"] = JsonSerializer.SerializeToElement(argument) } }]
        };
        var result = GalgroupCompiler.Compile(source, catalog).Document.Entries.Single();
        Assert.That(result.Arguments.GetProperty("argument").GetString(), Is.EqualTo(argument));
        Assert.That(catalog.Get(BuiltinEntryIds.InvokeCustom).DynamicParameters!.Fields.Select(field => field.Id),
            Is.EqualTo(ParameterNames));
        var fields = catalog.Get(BuiltinEntryIds.InvokeCustom).DynamicParameters!.Fields;
        Assert.That(fields.All(field => field.IsRequired && !field.Value.AllowNull && field.DefaultValue is null), Is.True);
    }

    [TestCase("{}")]
    [TestCase("{\"action\":\"id\"}")]
    [TestCase("{\"action\":\"\",\"argument\":\"\"}")]
    [TestCase("{\"action\":\" \",\"argument\":\"\"}")]
    [TestCase("{\"action\":null,\"argument\":\"\"}")]
    [TestCase("{\"action\":\"id\",\"argument\":null}")]
    [TestCase("{\"action\":\"id\",\"argument\":{}}")]
    [TestCase("{\"action\":\"id\",\"argument\":\"\",\"extra\":true}")]
    public void Contract_rejects_invalid_arguments_before_handler_creation(string json)
    {
        using var view = View(new BuiltinCustomActionModule().RegisterFactory("id", (_, _) =>
        { Assert.Fail("Invalid arguments must not reach the factory."); return null!; }));
        using var document = JsonDocument.Parse(json);
        Assert.That(() => view.Dispatch(new PrimitiveEntry(BuiltinEntryIds.InvokeCustom, document.RootElement), new GameRuntime(null), default),
            Throws.TypeOf<InvalidDataException>());
    }

    private sealed class Skippable(string? batchId) : PrimitiveInstance(batchId)
    {
        public override bool IsBlocking => true;
        public override bool IsSkippable => true;
        protected override void OnDispatch() { }
        protected override void OnSkip() => TryComplete();
    }

    private sealed class Sink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
