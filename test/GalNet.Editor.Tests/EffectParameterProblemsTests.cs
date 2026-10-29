using System.Text;
using System.Text.Json;
using Avalonia.Headless;
using GalNet.Contracts.Builtins;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.Shared.Visuals;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task TimelineProblemsUseKnownRadiusTargetAndRetainLegalOldShaderParametersWithoutWarnings()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); var shader = DefaultVisualTemplate.ReadShader("Blur");
            using var assets = new MetadataAssets { File = { Source = shader } };
            using var resources = new MetadataResources(); using var services = EffectServices(assets, resources);
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "TimelineParameterProblems"), "Timeline", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var effect = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ApplyEffect).Single();
            var unknown = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.ApplyEffect).Single();
            var clip = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.PlayTimeline).Single();
            effect.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program");
            effect.Source.Parameters["instanceId"] = JsonSerializer.SerializeToElement("c2121d4d69544d4d8a584d392dfedbe6");
            effect.Source.Parameters["parameters"] = JsonSerializer.SerializeToElement(new { radius = 32, angle = 0 });
            unknown.Source.Parameters["program"] = JsonSerializer.SerializeToElement("unavailable");
            clip.Source.Parameters["timeline"] = JsonSerializer.SerializeToElement(new
            {
                playbackHandleId = "play",
                frameRate = 60,
                durationFrames = 60,
                tracks = new[] { new { handleId = "c2121d4d69544d4d8a584d392dfedbe6", property = "radius", blendMode = "Replace",
                    keys = new[] { new { frame = 0, value = 32f, interpolationToNext = "Linear" }, new { frame = 10, value = 34.3f, interpolationToNext = "Linear" } } } },
                events = new[] { new { frame = 8, type = BuiltinEntryIds.ShowLayer, editorId = "create", parameters = new { transform = new { }, z = 0, opacity = 1, displayMode = "Native", handleId = "new-layer" } } }
            });
            scope.GetRequiredService<IEditorDocumentService>().SynchronizeGroups();
            var scenes = scope.GetRequiredService<ProjectSceneTargets>(); await scenes.PrepareAsync();
            var problems = scope.GetRequiredService<ProblemsPanelViewModel>(); await AwaitAutomaticProblems(problems);
            Assert.That(scope.GetRequiredService<ProjectLocalizationQuery>().Get()!.Issues, Is.Empty, "The unused angle is legal shader data.");
            Assert.That(problems.Problems.Where(problem => problem.Code == "localization.field.opaque" && problem.Target?.EntryId == clip.StableId), Is.Empty);
            Assert.That(scenes.Diagnostics().Where(issue => issue.Owner.EntryId == clip.StableId && issue.JsonPointer.Contains("/tracks/", StringComparison.Ordinal)), Is.Empty);

            assets.File.Source = shader.Replace("@param radius", "@param amount", StringComparison.Ordinal);
            scenes.InvalidateResources(); await scenes.PrepareAsync(); await AwaitAutomaticProblems(problems);
            Assert.That(effect.Source.Parameters["parameters"].GetProperty("radius").GetInt32(), Is.EqualTo(32), "Old shader parameters remain author data.");
            Assert.That(scope.GetRequiredService<ProjectLocalizationQuery>().Get()!.Issues.Where(issue => issue.EntryId == effect.StableId), Is.Empty,
                "Preserved shader parameters are legal even when the current shader does not consume them.");
            Assert.That(scenes.Diagnostics().Single(issue => issue.Owner.EntryId == clip.StableId && issue.JsonPointer.Contains("/tracks/", StringComparison.Ordinal)).Code,
                Is.EqualTo("scene.reference.uncertain"), "The target shader no longer supports radius, so this remains a real warning.");
            assets.File.Source = shader; scenes.InvalidateResources(); await scenes.PrepareAsync(); await AwaitAutomaticProblems(problems);
            Assert.That(scenes.Diagnostics().Where(issue => issue.Owner.EntryId == clip.StableId && issue.JsonPointer.Contains("/tracks/", StringComparison.Ordinal)), Is.Empty);
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task EffectParameterProblemsFollowMetadataRefreshAndUnsavedShaderContent()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var assets = new MetadataAssets { File = { Source = EffectEditorSource } };
            using var resources = new MetadataResources(); using var services = EffectServices(assets, resources);
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "EffectParameterProblems"), "Effects", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var effect = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ApplyEffect).Single();
            var audio = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.ApplyAudioSEffect).Single();
            effect.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program");
            effect.Source.Parameters["parameters"] = JsonSerializer.SerializeToElement(new { amount = .5, direction = new[] { 1, 2, 3 } });
            audio.Source.Parameters["id"] = JsonSerializer.SerializeToElement("seffect.reverb");
            audio.Source.Parameters["targetId"] = JsonSerializer.SerializeToElement("bgm");
            audio.Source.Parameters["parameters"] = JsonSerializer.SerializeToElement(new { mix = .4 });
            scope.GetRequiredService<IEditorDocumentService>().SynchronizeGroups();
            var scenes = scope.GetRequiredService<ProjectSceneTargets>(); await scenes.PrepareAsync();
            var query = scope.GetRequiredService<ProjectLocalizationQuery>();
            var index = query.Get()!; var revision = query.Revision;
            Assert.That(index.Issues, Is.Empty);
            var problems = scope.GetRequiredService<ProblemsPanelViewModel>(); await AwaitAutomaticProblems(problems);
            Assert.That(problems.Problems.Where(problem => problem.Code?.StartsWith("localization.") == true), Is.Empty);

            // The working shader overrides disk/asset bytes before Save All.
            var files = scope.GetRequiredService<IProjectContentSession>();
            var file = files.TrackFile("Assets/" + assets.File.Path, Encoding.UTF8.GetBytes(EffectEditorSource));
            file.Data = Encoding.UTF8.GetBytes(EffectEditorSource.Replace("@param amount", "@param changed", StringComparison.Ordinal));
            scenes.InvalidateResources(); await scenes.PrepareAsync();
            var changed = query.Get()!;
            Assert.That(query.Revision, Is.GreaterThan(revision));
            Assert.That(changed.Issues, Is.Empty, "The old amount remains valid additional shader data.");
            await AwaitAutomaticProblems(problems);
            Assert.That(problems.Problems.Where(problem => problem.Code?.StartsWith("localization.") == true), Is.Empty);

            file.Data = Encoding.UTF8.GetBytes("missing shader metadata");
            scenes.InvalidateResources(); await scenes.PrepareAsync();
            Assert.That(query.Get()!.Issues.Single().Code, Is.EqualTo("field.opaque"));
            file.Data = Encoding.UTF8.GetBytes(EffectEditorSource);
            scenes.InvalidateResources(); await scenes.PrepareAsync();
            Assert.That(query.Get()!.Issues, Is.Empty);
            await AwaitAutomaticProblems(problems);
            Assert.That(problems.Problems.Where(problem => problem.Code?.StartsWith("localization.") == true), Is.Empty);
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }
}
