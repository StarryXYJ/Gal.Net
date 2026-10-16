using GalNet.Core.Variable;
using GalNet.Runtime.Persistence;
using GalNet.Runtime.Variables;

namespace GalNet.GameHost.Tests;

public sealed class PlayerPreferencesTests
{
    [Test]
    public async Task InvalidPreferencesFallBackWithoutRewritingPlayerVariables()
    {
        using var variables = await PlayerVariableService.CreateAsync(new Store());
        var preferences = new PlayerPreferences(variables);
        preferences.Set("volume", float.NaN);
        preferences.Set("interval", "500");
        preferences.Set("other", 42);
        Assert.That(preferences.Number("volume", 1, 1), Is.EqualTo(1));
        Assert.That(preferences.Integer("interval", 50, 5000), Is.EqualTo(50));
        Assert.That(preferences.Boolean("missing", true), Is.True);
        var saved = variables.GetSnapshot(VariableScope.Player);
        Assert.That(float.IsNaN(saved["volume"].AsFloat()), Is.True);
        Assert.That(saved["interval"].AsString(), Is.EqualTo("500"));
        Assert.That(saved["other"].AsInt(), Is.EqualTo(42));
    }
    private sealed class Store : IPlayerVariableStore
    {
        public Task<IReadOnlyDictionary<string, Variable>> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, Variable>>(new Dictionary<string, Variable>());
        public Task SaveAsync(IReadOnlyDictionary<string, Variable> variables, CancellationToken ct = default) => Task.CompletedTask;
    }
}
