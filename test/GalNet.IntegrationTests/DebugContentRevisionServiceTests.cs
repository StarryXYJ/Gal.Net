using GalNet.Avalonia.Debugging;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Debugging;
using GalNet.Editor.Shared.Services;
using GalNet.Sample.CrossPlatform;
using GalNet.Storage.FileSystem;

namespace GalNet.IntegrationTests;

public sealed class DebugContentRevisionServiceTests
{
    [Test]
    public async Task InstallSeparatesLatestFromRunAndNewRunSelectsLatestResources()
    {
        var root = Path.Combine(Path.GetTempPath(), $"galnet-debug-revisions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "Graph", "groups"));
        Directory.CreateDirectory(Path.Combine(root, "I18n", "zh-CN"));
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Graph", "graph.json"),
                """{"version":2,"name":"Debug","rootNodeId":"main","nodes":[{"id":"main","type":"Group","name":"Main","file":"groups/main.rawgalgroup"}],"edges":[],"playerVariables":[],"saveVariables":[]}""");
            await File.WriteAllTextAsync(Path.Combine(root, "I18n", "zh-CN", "main.json"),
                """{"group.main.entry.line.content":"Line","group.main.entry.next.content":"Next"}""");
            var group = Path.Combine(root, "Graph", "groups", "main.rawgalgroup");
            await File.WriteAllTextAsync(group,
                """{"version":2,"kind":"Raw","entries":[{"id":"line","type":"dialogue.text","parameters":{"content":"group.main.entry.line.content"}}]}""");
            var builder = new ProjectContentBuilder(new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules()));
            var initial = await DebugContentRevisionBuilder.BuildAsync(builder, root, "project", "contract");
            await File.WriteAllTextAsync(group,
                """{"version":2,"kind":"Raw","entries":[{"id":"line","type":"dialogue.text","parameters":{"content":"group.main.entry.line.content"}},{"id":"next","type":"dialogue.text","parameters":{"content":"group.main.entry.next.content"}}]}""");
            var candidate = await DebugContentRevisionBuilder.BuildAsync(builder, root, "project", "contract");
            var options = SampleCustomization.CreateHostOptions();
            var catalogs = options.CreateContentCatalogs();
            using var initialResources = await GameResourceScope.OpenAsync(initial.OutputPath,
                catalogs.Resources, catalogs.Gallery, options.Content);
            await using var revisions = new DebugContentRevisionService(options);
            revisions.SetIdentity(new("project", initial.Manifest!.ContentFingerprint, "contract"));

            Assert.That(revisions.SelectForNewRun(initialResources), Is.SameAs(initialResources));
            var installed = await revisions.InstallAsync(new(candidate.OutputPath, candidate.Manifest!.Revision,
                candidate.Manifest.ContentFingerprint, candidate.Manifest.ContractFingerprint), CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(installed.LatestRevision, Is.EqualTo(candidate.Manifest.Revision));
                Assert.That(installed.RunRevision, Is.EqualTo(initial.Manifest.Revision));
                Assert.That(revisions.GetLatestGraph(candidate.Manifest.Revision).Nodes.OfType<GalNet.Core.Graph.Group>()
                    .Single().Entries, Has.Count.EqualTo(2));
            });

            revisions.CommitActivation(candidate.Manifest.Revision);
            var selected = revisions.SelectForNewRun(initialResources);
            Assert.That(selected.GameDirectory, Is.EqualTo(Path.GetFullPath(candidate.OutputPath)));
            Assert.That(revisions.Versions, Is.EqualTo(new DebugContentVersions(candidate.Manifest.Revision, candidate.Manifest.Revision)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Test]
    public async Task InstallRejectsContractMismatchWithoutChangingVersions()
    {
        var options = SampleCustomization.CreateHostOptions();
        await using var revisions = new DebugContentRevisionService(options);
        revisions.SetIdentity(new("project", "content", "expected"));

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => revisions.InstallAsync(
            new("missing", "revision", "content", "changed"), CancellationToken.None));

        Assert.That(error!.Message, Does.Contain("contracts changed").IgnoreCase);
        Assert.That(revisions.Versions, Is.EqualTo(new DebugContentVersions("", "")));
    }
}
