using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using GalNet.Avalonia.GameView.Input;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Page;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Parameters;
using GalNet.Core.Serialization;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class SampleAotPreparationTests
{
    [Test]
    public void InputPreferencesKeepTheExistingWireFormatWithoutReflectionMetadata()
    {
        const string legacy = """{"Version":1,"EastConfirms":true,"Bindings":{"advance":[{"Device":1,"Gesture":"East"}],"toggle.auto":[{"Device":0,"Gesture":"Shift+A"}],"toggle.ui":[{"Device":2,"Gesture":"TwoFingerTap"}]}}""";
        using var settings = new PlayerInputSettings();
        settings.Restore(legacy);
        Assert.That(settings.EastConfirms, Is.True);
        Assert.That(settings.Resolve(PlayerInputDevice.Gamepad, "East"), Is.EqualTo("advance"));
        Assert.That(settings.Resolve(PlayerInputDevice.KeyboardMouse, "Shift+A"), Is.EqualTo("toggle.auto"));
        Assert.That(settings.Resolve(PlayerInputDevice.Touch, "TwoFingerTap"), Is.EqualTo("toggle.ui"));
        using var original = JsonDocument.Parse(legacy);
        using var saved = JsonDocument.Parse(settings.Serialize());
        Assert.That(JsonElement.DeepEquals(original.RootElement, saved.RootElement), Is.True);
    }

    [TestCase("null")]
    [TestCase("{}")]
    [TestCase("{\"Version\":1,\"Bindings\":null}")]
    [TestCase("{\"Version\":1,\"Bindings\":{\"advance\":[{\"Device\":9,\"Gesture\":\"East\"}]}}")]
    [TestCase("{\"Version\":1,\"Bindings\":{\"advance\":[{\"Device\":\"Gamepad\",\"Gesture\":\"East\"}]}}")]
    public void InvalidInputPreferencesStillFallBackToDefaults(string json)
    {
        using var settings = new PlayerInputSettings();
        settings.Restore(json);
        Assert.That(PlayerInputSettings.Validate(settings.Configuration), Is.Null);
        Assert.That(settings.Resolve(PlayerInputDevice.KeyboardMouse, "Space"), Is.EqualTo("advance"));
        Assert.That(settings.EastConfirms, Is.False);
    }

    [Test]
    public async Task NumericBoundsStillRejectInvalidEditsAndPreservePrecision()
    {
        var integer = 0;
        var intSetting = new IntSetting("int", "int", 0, int.MinValue, int.MaxValue, 1,
            () => integer, (value, _) => { integer = value; return Task.CompletedTask; });
        Assert.That(intSetting.ValueContract.TryGetConstraint("minimum", out var minimum), Is.True);
        Assert.That(minimum.GetInt32(), Is.EqualTo(int.MinValue));
        Assert.That(await intSetting.TrySetValueAsync(int.MaxValue), Is.True);
        Assert.That(integer, Is.EqualTo(int.MaxValue));

        var number = 0.125;
        var doubleSetting = new DoubleSetting("double", "double", number, -0.125, 0.875, 0.125,
            () => number, (value, _) => { number = value; return Task.CompletedTask; });
        Assert.That(await doubleSetting.TrySetValueAsync(1), Is.False);
        Assert.That(number, Is.EqualTo(0.125));
        Assert.That(await doubleSetting.TrySetValueAsync(0.875), Is.True);
        Assert.That(number, Is.EqualTo(0.875));
        Assert.Throws<ArgumentException>(() => new DoubleSetting("invalid", "invalid", 0, 0, double.NaN, 1,
            () => 0, (_, _) => Task.CompletedTask));
    }

    [Test]
    public void EnumOptionsKeepAliasesAndUnsignedValuesWithoutDynamicEnumArrays()
    {
        var options = JsonMetadata.CreateOptions(resolver: PreparationJsonContext.Default);
        var field = new FieldDescriptor("enum", new ValueDescriptor(typeof(PreparationChoice), jsonOptions: options));
        using var editor = new FieldEditorContext(field, new EmptyBinding());
        Assert.That(editor.Options.Select(option => option.Value.GetUInt64()),
            Is.EqualTo(new[] { 1UL, 1UL, ulong.MaxValue }));
    }

    [Test]
    public async Task GameSurfaceBindingsFollowDataContextReplacementAndOverlayChanges()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(() =>
        {
            using var services = new ServiceCollection().BuildServiceProvider();
            var navigation = new GameNavigationService(services, new GameNavigationTransitionCoordinator());
            var first = new GamePageViewModel(navigation) { IsDialogueVisible = true, IsChoiceVisible = true };
            var second = new GamePageViewModel(navigation) { IsNvlMode = true };
            var page = new GamePage { DataContext = first };
            var window = new Window { Content = page };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs();
                Assert.That(page.SceneHost.ItemsSource, Is.SameAs(first.Layers));
                Assert.That(page.Choice.ItemsSource, Is.SameAs(first.Choices));
                Assert.That(page.Choice.SelectionCommand, Is.SameAs(first.CompleteChoiceCommand));
                Assert.That(page.Dialogue.IsVisible, Is.True);
                Assert.That(page.Choice.IsVisible, Is.True);
                first.IsUiHidden = true;
                Assert.That(page.Dialogue.IsVisible, Is.False);
                Assert.That(page.Choice.IsVisible, Is.False);

                page.DataContext = second;
                Assert.That(page.SceneHost.ItemsSource, Is.SameAs(second.Layers));
                Assert.That(page.SceneHost.EffectsSource, Is.SameAs(second.TextureEffects));
                Assert.That(page.SceneHost.RenderablesSource, Is.SameAs(second.SceneRenderables));
                Assert.That(page.Choice.SelectionCommand, Is.SameAs(second.CompleteChoiceCommand));
                Assert.That(page.Nvl.IsVisible, Is.True);
                first.IsUiHidden = false;
                Assert.That(page.Dialogue.IsVisible, Is.False, "The old context must no longer drive the surface.");
                second.IsUiHidden = true;
                Assert.That(page.Nvl.IsVisible, Is.False);
            }
            finally { window.Close(); }
            return Task.FromResult(true);
        }, CancellationToken.None);
    }

    private sealed class EmptyBinding : IFieldBinding
    {
        public FieldValue Read() => default;
        public event EventHandler? Changed { add { } remove { } }
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken) =>
            Task.FromResult<ValueIssue?>(null);
    }
}

internal enum PreparationChoice : ulong { First = 1, Alias = 1, Last = ulong.MaxValue }

[JsonSerializable(typeof(PreparationChoice))]
internal sealed partial class PreparationJsonContext : JsonSerializerContext;
