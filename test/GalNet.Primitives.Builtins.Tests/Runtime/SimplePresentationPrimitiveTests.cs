using GalNet.Core.Primitives;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Runtime;

namespace GeneralTest.Runtime;

public sealed class SimplePresentationPrimitiveTests
{
    [TestCase(PrimitiveInvocationMode.Normal)]
    [TestCase(PrimitiveInvocationMode.Quick)]
    [TestCase(PrimitiveInvocationMode.CatchUp)]
    public async Task AllFiveInstancesKeepStateAndPresentationModePolicy(PrimitiveInvocationMode mode)
    {
        var runtime = BuiltinTestRuntime.Create(null);
        var presenter = new Presenter();
        var definition = new ParticleEmitterDefinition("snow");
        PrimitiveInstance[] instances =
        [
            new ApplyEffectPrimitiveInstance(runtime, presenter, new EffectRequest("blur", "effect"), "batch", default),
            new PlayParticlePrimitiveInstance(runtime, presenter, new ParticleEmitterRequest("particle", definition, 0), "batch", default),
            new BurstParticlePrimitiveInstance(presenter, new ParticleBurstRequest(definition, 3, 0), "batch", default),
            new StopEffectPrimitiveInstance(runtime, presenter, "effect", "batch", default),
            new StopParticlePrimitiveInstance(runtime, presenter, "particle", "batch", default)
        ];
        for (var i = 0; i < instances.Length; i++)
        {
            var instance = instances[i];
            var completion = instance.PresentationCompletion;
            instance.Dispatch(mode);
            if (i == 0) Assert.That(runtime.State.Table<EffectInstance>().Values.Single().Id, Is.EqualTo("effect"));
            if (i == 1) Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values.Single().Id, Is.EqualTo("particle"));
            await completion;
            Assert.That(instance.IsCompleted, Is.True);
            Assert.That(instance.IsBlocking, Is.False);
            Assert.That(instance.IsSkippable, Is.False);
            Assert.That(instance.BatchId, Is.EqualTo("batch"));
        }
        Assert.That(runtime.State.Table<EffectInstance>().Values, Is.Empty);
        Assert.That(runtime.State.Table<ParticleEmitterInstance>().Values, Is.Empty);
        Assert.That(presenter.Calls, Is.EqualTo(mode == PrimitiveInvocationMode.CatchUp ? 0 : 5));
    }

    [Test]
    public async Task BackendFailureAndScopeCancellationRemainHandled()
    {
        using var cancellation = new CancellationTokenSource();
        var presenter = new Presenter { Operation = _ => throw new IOException("backend") };
        var runtime = BuiltinTestRuntime.Create(null);
        var failed = new StopEffectPrimitiveInstance(runtime, presenter, "effect", null, cancellation.Token);
        failed.Dispatch();
        await failed.PresentationCompletion;
        Assert.That(failed.IsCompleted, Is.True);
        presenter.Operation = token => Task.Delay(Timeout.Infinite, token);
        var cancelled = new StopParticlePrimitiveInstance(runtime, presenter, "particle", null, cancellation.Token);
        var logicalCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cancelled.Completed += _ => logicalCompletion.TrySetResult();
        cancelled.Dispatch();
        Assert.That(cancelled.IsCompleted, Is.False);
        cancellation.Cancel();
        await cancelled.PresentationCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        await logicalCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(cancelled.IsCompleted, Is.True);
    }

    private sealed class Presenter : IEffectPresenter, IParticlePresenter
    {
        public int Calls { get; private set; }
        public Func<CancellationToken, Task> Operation { get; set; } = _ => Task.CompletedTask;
        private Task Run(CancellationToken token) { Calls++; return Operation(token); }
        public Task StartEffectAsync(EffectRequest request, CancellationToken cancellationToken) => Run(cancellationToken);
        public Task StopEffectAsync(string instanceId, CancellationToken cancellationToken) => Run(cancellationToken);
        public Task StartParticleEmitterAsync(ParticleEmitterRequest request, CancellationToken cancellationToken) => Run(cancellationToken);
        public Task StopParticleEmitterAsync(string instanceId, CancellationToken cancellationToken) => Run(cancellationToken);
        public Task BurstParticlesAsync(ParticleBurstRequest request, CancellationToken cancellationToken) => Run(cancellationToken);
    }
}
