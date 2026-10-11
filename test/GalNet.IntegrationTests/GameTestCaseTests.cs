using DynamicLocalization.Core;
using GalNet.Core.Assets;
using GalNet.Core.Entry;
using GalNet.Core.Gallery;
using GalNet.Core.Graph;
using GalNet.Core.Settings;
using GalNet.Editor.Shared.Services;
using GalNet.Presentation.Abstractions.View;
using GalNet.Primitives.Builtins;
using GalNet.Runtime.Engine;
using GalNet.Sample.Avalonia.Services;
using GalNet.Storage.FileSystem;

namespace GeneralTest.Runtime;

public class GameTestCaseTests
{
    [TestCase("en-US", 0, "Guide", "Runtime smoke test complete.")]
    [TestCase("en-US", 1, "Guide", "Runtime smoke test complete.")]
    [TestCase("zh-CN", 0, "向导", "运行时冒烟测试完成。")]
    [TestCase("zh-CN", 1, "向导", "运行时冒烟测试完成。")]
    public async Task LocalizedSmokeGameCompletesBothRoutes(string culture, int route, string name, string ending)
    {
        var output = Path.Combine(Path.GetTempPath(), "galnet-localized-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            await new ProjectContentBuilder().BuildAsync(Path.Combine(FindRepositoryDirectory(), "GameTestCase"), output);
            var types = BuiltinResourceTypes.CreateCatalog();
            var content = await new ProjectGameContentProvider(output, types, BuiltinGalleryTypes.CreateCatalog(types)).LoadAsync();
            var cultures = new CultureService();
            using var localization = new SampleLocalizationService(cultures);
            localization.AttachGame(content);
            localization.ApplyCulture(culture);
            var presenter = new SmokeTextPresenter(route);
            await using var view = new GameRegistration().AddBuiltins(presenter, gallery: content.Gallery,
                nvlPresenter: presenter, characters: content.Localization.Characters).BuildView();
            var settings = new SettingsContainer();
            settings.Set(content.Settings);
            await using var engine = new GameEngine(content.Graph, view, localization, settings, choicePresenter: presenter);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await engine.AdvanceAsync(timeout.Token);
            while (engine.State == GameEngineState.Running)
            {
                await engine.AdvanceAsync(timeout.Token);
                await Task.Delay(1, timeout.Token);
            }
            Assert.Multiple(() =>
            {
                Assert.That(engine.State, Is.EqualTo(GameEngineState.Completed));
                Assert.That(presenter.Lines.Last(), Is.EqualTo((name, ending)));
                Assert.That(presenter.Nvl, Has.Count.EqualTo(3));
                Assert.That(presenter.Choices, Has.Count.EqualTo(2));
                Assert.That(presenter.Lines.Select(line => line.Text), Has.None.StartsWith("group."));
                Assert.That(presenter.Nvl, Has.None.StartsWith("group."));
                Assert.That(presenter.Choices, Has.None.StartsWith("branch."));
                Assert.That(presenter.Nvl[0], Does.Contain("<b>").And.Contain("\\skip"));
            });
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    [Test]
    public async Task SharedSmokeTestBuildsRawContentBeforeLoadingCompiledRuntimeContent()
    {
        var directory = FindRepositoryDirectory();
        var output = Path.Combine(Path.GetTempPath(), $"galnet-game-testcase-{Guid.NewGuid():N}");
        try
        {
            await new ProjectContentBuilder().BuildAsync(Path.Combine(directory, "GameTestCase"), output);

            var resourceTypes = BuiltinResourceTypes.CreateCatalog();
            var galleryTypes = BuiltinGalleryTypes.CreateCatalog(resourceTypes);
            var content = await new ProjectGameContentProvider(output, resourceTypes, galleryTypes).LoadAsync();

            var groups = content.Graph.Nodes.OfType<Group>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(content.Graph.RootNodeId, Is.EqualTo("intro"));
                Assert.That(groups, Has.Length.EqualTo(4));
                Assert.That(groups, Has.All.Matches<Group>(group => group.Entries.Count > 0));
                Assert.That(groups.SelectMany(group => group.Entries), Has.All.TypeOf<PrimitiveEntry>());
                Assert.That(groups.Single(group => group.Id == "intro").Entries.OfType<PrimitiveEntry>(),
                    Has.One.Matches<PrimitiveEntry>(entry =>
                        entry.Type == "gallery.unlock" && entry.Arguments.GetProperty("id").GetInt32() == 100));
                Assert.That(content.Gallery.GetItems("cg"), Has.One.Matches<GalNet.Core.Gallery.GalleryItem>(item =>
                    item.Id == 100 && item.ResourceId == "a1000000000000000000000000000001"));
            });
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        }
    }

    private static string FindRepositoryDirectory()
    {
        for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "GameTestCase")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository GameTestCase directory.");
    }

    private sealed class SmokeTextPresenter(int route) : IDialoguePresenter, INvlPresenter, IChoicePresenter
    {
        public List<(string Speaker, string Text)> Lines { get; } = [];
        public List<string> Nvl { get; } = [];
        public IReadOnlyList<string> Choices { get; private set; } = [];
        public Task PresentTextAsync(string speaker, string text, CancellationToken cancellationToken)
        {
            Lines.Add((speaker, text));
            return Task.CompletedTask;
        }
        public Task AppendNvlTextAsync(string text, CancellationToken cancellationToken)
        {
            Nvl.Add(text);
            return Task.CompletedTask;
        }
        public Task<int> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellationToken)
        {
            Choices = options;
            return Task.FromResult(route);
        }
        public void ShowDialogue() { }
        public void HideDialogue() { }
        public void SkipText() { }
        public void ShowNvl() { }
        public void HideNvl() { }
        public void ClearNvl() { }
        public void SkipNvlText() { }
    }
}
