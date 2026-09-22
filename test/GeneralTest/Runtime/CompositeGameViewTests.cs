using System.Text.Json;
using GalNet.Core.Primitives;
using GalNet.Core.View;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public class CompositeGameViewTests
{
    [Test]
    public void DynamicGameViewOnlyInheritsDisposal()
    {
        Assert.That(typeof(IGameView).GetInterfaces(), Is.EquivalentTo(new[] { typeof(IDisposable) }));
    }

    [Test]
    public void ScopeFreezesDescriptorLookupAndRoutesByPrefix()
    {
        var module = new TestModule("layer", new PrimitiveDescriptor("layer.show", DynamicParameterTable.Empty));
        using var view = new CompositeGameView([module]);
        var invocation = new PrimitiveInvocation(
            "layer.show",
            new PrimitiveContext { Runtime = new GameRuntime(null), Origin = PrimitiveInvocationOrigin.GroupEntry },
            JsonSerializer.SerializeToElement(new { visible = true }));

        Assert.That(view.TryGetDescriptor("layer.show", out var descriptor), Is.True);
        Assert.That(descriptor, Is.SameAs(module.Descriptors.Single()));

        var dispatch = view.Dispatch(invocation, new PrimitiveExecutionControl(), CancellationToken.None);

        Assert.That(dispatch.Status, Is.EqualTo(PrimitiveDispatchStatus.Accepted));
        Assert.That(module.Command, Is.EqualTo("show"));
        Assert.That(module.Arguments.GetProperty("visible").GetBoolean(), Is.True);
    }

    [Test]
    public void UnknownOrInvalidPrimitiveSafelySkips()
    {
        using var view = new CompositeGameView([]);
        var context = new PrimitiveContext { Runtime = new GameRuntime(null), Origin = PrimitiveInvocationOrigin.GroupEntry };

        foreach (var typeId in new[] { "missing.run", "missing", ".run", "run." })
        {
            var dispatch = view.Dispatch(
                new PrimitiveInvocation(typeId, context, JsonSerializer.SerializeToElement(new { })),
                new PrimitiveExecutionControl(),
                CancellationToken.None);
            Assert.That(dispatch.Status, Is.EqualTo(PrimitiveDispatchStatus.Skipped), typeId);
        }
    }

    [Test]
    public void ScopeRejectsDuplicatePrefixesAndMismatchedDescriptors()
    {
        var first = new TestModule("layer", new PrimitiveDescriptor("layer.show", DynamicParameterTable.Empty));
        var duplicate = new TestModule("layer", new PrimitiveDescriptor("layer.hide", DynamicParameterTable.Empty));
        var mismatched = new TestModule("layer", new PrimitiveDescriptor("effect.apply", DynamicParameterTable.Empty), register: false);

        Assert.That(() => new CompositeGameView([first, duplicate]), Throws.InvalidOperationException);
        Assert.That(() => new CompositeGameView([mismatched]), Throws.InvalidOperationException);
    }

    private sealed class TestModule : PrimitiveModuleBase
    {
        public TestModule(string prefix, PrimitiveDescriptor descriptor, bool register = true) : base(prefix)
        {
            if (register) Register(descriptor, Execute);
            else RegisterUnchecked(descriptor);
        }

        public string? Command { get; private set; }
        public JsonElement Arguments { get; private set; }

        private PrimitiveDispatch Execute(PrimitiveContext context, JsonElement arguments, PrimitiveExecutionControl control, CancellationToken cancellationToken)
        {
            Command = "show";
            Arguments = arguments.Clone();
            return new PrimitiveDispatch(PrimitiveDispatchStatus.Accepted, new PrimitiveExecutionPolicy(false, false, null), Task.FromResult(PrimitiveResult.Empty));
        }

        private void RegisterUnchecked(PrimitiveDescriptor descriptor)
        {
            // Force the Composite validator to inspect an externally supplied invalid descriptor.
            var field = typeof(PrimitiveModuleBase).GetField("_commands", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var commands = (Dictionary<string, (PrimitiveDescriptor Descriptor, PrimitiveCommand Command)>)field.GetValue(this)!;
            commands.Add("show", (descriptor, Execute));
        }
    }
}
