using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Assets;
using GalNet.Contracts.Builtins.Scene;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Composition;
using GalNet.Editor.History;
using GalNet.Editor.Models;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Inspector.Views;
using GalNet.Editor.Services;
using GalNet.Editor.Services.Interfaces;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private const string EffectSource = """
        /*
        @gal.effect v=1
        @input source
        @targets layer,scenePost
        @param amount
          uniform: uAmount
          type: float
          default: 0.25
          range: 0..1
        */
        """;
    private const string EffectEditorSource = """
        /*
        @gal.effect v=1
        @input source
        @targets layer,scenePost
        @param amount
          uniform: uAmount
          type: float
          default: 0.25
          range: 0..1
        @param tint
          uniform: uTint
          type: color
          default: #ff0000ff
        @param offset
          uniform: uOffset
          type: vec2
          default: [0,0]
        @param direction
          uniform: uDirection
          type: vec3
        @param channels
          uniform: uChannels
          type: vec4
        @param style
          uniform: uStyle
          type: enum
          options: soft,hard
        @texture mask
          uniform: uMask
        */
        """;
    private const string EffectPreviewSource = """
        /*
        @gal.effect v=1
        @input source
        @targets layer
        @param amount
          uniform: amount
          type: float
          default: 0
        */
        uniform shader source;
        uniform float amount;
        half4 main(float2 p) {
            half4 color = source.eval(p);
            return half4(color.r * amount, color.g, color.b, color.a);
        }
        """;
    private static ServiceProvider EffectServices(MetadataAssets assets, MetadataResources resources) => new ServiceCollection().AddEditorServices()
        .AddSingleton<IEditorLocalizationService>(new EditorLocalizationService())
        .AddSingleton<IProjectClosePrompt>(new DiscardProjectClosePrompt())
        .AddSingleton<IEditorSettingsService>(new TestSettings()).AddSingleton<IFileDialogService>(new TestDialogs())
        .AddSingleton<IEffectCatalog>(new EffectCatalog([])).AddSingleton<IAssetManager>(assets)
        .AddSingleton<IAssetCatalogService>(resources).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

    [Test]
    public async Task PresetInspectorReusesTransformVectorAndShaderMetadataEditors()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var assets = new MetadataAssets { File = { Source = EffectEditorSource } };
            using var resources = new MetadataResources(); using var services = EffectServices(assets, resources);
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "PresetInspector"), "Preset", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.SpeedLinesPreset).Single();
            entry.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program");
            entry.Source.Parameters["parameters"] = JsonSerializer.Deserialize<JsonElement>("{\"amount\":0.6,\"unknown\":[9007199254740993,null]}");
            scope.GetRequiredService<IEditorDocumentService>().SynchronizeGroups();
            var query = scope.GetRequiredService<ProjectSceneTargets>(); await query.PrepareAsync();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
            using var inspector = new EntryInspectorViewModel(page, sceneTargets: query);
            var view = new EntryInspectorView { DataContext = inspector };
            var window = new Window { Content = view, Width = 650, Height = 950 }; window.Show(); TimelinePump(window);
            try
            {
                Assert.That(query.ProgramDescriptor("program"), Is.Not.Null);
                Assert.That(inspector.Form!.Item.Fields.Select(field => field.Id), Does.Contain("parameters"));
                var host = view.GetVisualDescendants().OfType<DtoEditorHost>().Single(control => control.Name == "EntryDetails");
                Assert.That(host.Diagnostics, Is.Empty);
                var mounted = view.GetVisualDescendants().OfType<FieldEditorRow>().ToArray();
                Assert.That(mounted.Select(row => row.Context?.Field.Id), Does.Contain("parameters"));
                var parameters = mounted.Single(row => row.Context?.Field.Id == "parameters");
                var preset = new[] { parameters.Editor! }.Concat(parameters.Editor!.GetVisualDescendants())
                    .OfType<StackPanel>().Single(panel => panel.Name == "PresetShaderParameters");
                Assert.That(preset.GetVisualDescendants().OfType<TextBox>().Any(), Is.True);
                var fields = preset.GetVisualDescendants().OfType<TextBox>().ToArray();
                Assert.That(fields.Length, Is.GreaterThanOrEqualTo(2));
                var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
                var amount = fields.First(); amount.Focus(); amount.Text = "0.8"; TimelinePump(window); PressGroupKey(amount, Key.Enter); TimelinePump(window);
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("amount").GetSingle(), Is.EqualTo(.8f));
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("unknown")[0].GetInt64(), Is.EqualTo(9007199254740993L));
                await history.UndoAsync(); TimelinePump(window);
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("amount").GetSingle(), Is.EqualTo(.6f));

                entry.Source.Parameters["program"] = JsonSerializer.SerializeToElement("missing");
                entry.RefreshAuthorReferences(scope.GetRequiredService<GalNet.Core.Entry.IEntryCatalog>()); TimelinePump(window);
                Assert.That(preset.GetVisualDescendants().OfType<TextBox>(), Is.Empty);

                var motion = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.MotionPreset).Single();
                page.SelectedEntry = motion; TimelinePump(window);
                var transform = view.GetVisualDescendants().OfType<FieldEditorRow>().Single(row => row.Context?.Field.Id == "transform");
                Assert.That(new[] { transform.Editor! }.Concat(transform.Editor!.GetVisualDescendants()).OfType<Grid>().Any(grid => grid.Name == "TransformEditor"), Is.True);

                var reveal = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.RevealTransition).Single();
                page.SelectedEntry = reveal; TimelinePump(window);
                var center = view.GetVisualDescendants().OfType<FieldEditorRow>().Single(row => row.Context?.Field.Id == "center");
                Assert.That(new[] { center.Editor! }.Concat(center.Editor!.GetVisualDescendants()).OfType<StackPanel>().Any(panel => panel.Name == "ShaderVectorEditor"), Is.True);

                var impact = workspace.InsertEntriesInto(group, 3, 1, BuiltinEntryIds.ImpactPreset).Single();
                impact.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program");
                impact.RefreshAuthorReferences(scope.GetRequiredService<GalNet.Core.Entry.IEntryCatalog>());
                page.SelectedEntry = impact; await query.PrepareAsync(); TimelinePump(window);
                var impactParameters = view.GetVisualDescendants().OfType<FieldEditorRow>().Single(row => row.Context?.Field.Id == "parameters");
                Assert.That(new[] { impactParameters.Editor! }.Concat(impactParameters.Editor!.GetVisualDescendants())
                    .OfType<StackPanel>().Any(panel => panel.Name == "PresetShaderParameters"), Is.True);
                return true;
            }
            finally { window.Close(); await projects.CloseAsync(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ShaderInspectorPreviewsSelectedProgramAndRefreshesAfterParameterEdit()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var assets = new MetadataAssets(); assets.File.Source = EffectPreviewSource;
            using var resources = new MetadataResources(); using var services = EffectServices(assets, resources);
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "ShaderPreview"), "Shader", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ApplyEffect).Single();
            entry.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program");
            entry.Source.Parameters["parameters"] = JsonSerializer.Deserialize<JsonElement>("{\"amount\":0}");
            scope.GetRequiredService<IEditorDocumentService>().SynchronizeGroups();
            var query = scope.GetRequiredService<ProjectSceneTargets>(); await query.PrepareAsync();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
            using var inspector = new EntryInspectorViewModel(page, sceneTargets: query, assets: assets);
            var view = new EntryInspectorView { DataContext = inspector };
            var window = new Window { Content = view, Width = 480, Height = 780 }; window.Show(); TimelinePump(window);
            try
            {
                await inspector.PreviewLoading; TimelinePump(window);
                Assert.That(inspector.ShaderPreviewError, Is.Null);
                Assert.That(inspector.ShaderPreview, Is.Not.Null);
                var previewPanel = view.GetVisualDescendants().OfType<Expander>().Single(control => control.Name == "ShaderPreviewPanel");
                Assert.That(Grid.GetRow(previewPanel), Is.EqualTo(2), "The preview is docked below the scrollable fields.");
                Assert.That(previewPanel.Parent, Is.TypeOf<Grid>());
                var image = view.GetVisualDescendants().OfType<Image>().Single(control => control.Name == "ShaderEffectPreview");
                Assert.That(image.Source, Is.SameAs(inspector.ShaderPreview));
                using var beforeStream = new MemoryStream(); inspector.ShaderPreview!.Save(beforeStream); beforeStream.Position = 0;
                using var before = SKBitmap.Decode(beforeStream);
                Assert.That(before.Width, Is.EqualTo(ShaderEffectPreviewRenderer.Size));
                Assert.That(before.Height, Is.EqualTo(ShaderEffectPreviewRenderer.Size));
                Assert.That(before.GetPixel(64, 64).Red, Is.LessThan(10));

                var form = view.GetVisualDescendants().OfType<DtoEditorHost>().Single(host => host.Name == "ShaderParameters");
                var amount = form.GetVisualDescendants().OfType<FieldEditorRow>().Single(row => row.Context!.Field.Id == "amount");
                Assert.That(await amount.Context!.CommitValueAsync(1f), Is.True);
                await inspector.PreviewLoading; TimelinePump(window);
                using var afterStream = new MemoryStream(); inspector.ShaderPreview!.Save(afterStream); afterStream.Position = 0;
                using var after = SKBitmap.Decode(afterStream);
                Assert.That(after.GetPixel(64, 64).Red, Is.GreaterThan(200));

                assets.File.Source = EffectPreviewSource.Replace("color.r * amount", "color.r * (1 - amount)", StringComparison.Ordinal);
                resources.RaiseChanged(); TimelinePump(window); await query.PrepareAsync(); TimelinePump(window);
                await inspector.PreviewLoading; TimelinePump(window);
                using var refreshedStream = new MemoryStream(); inspector.ShaderPreview!.Save(refreshedStream); refreshedStream.Position = 0;
                using var refreshed = SKBitmap.Decode(refreshedStream);
                Assert.That(refreshed.GetPixel(64, 64).Red, Is.LessThan(10));

                entry.Source.Parameters["program"] = JsonSerializer.SerializeToElement("missing");
                entry.RefreshAuthorReferences(scope.GetRequiredService<GalNet.Core.Entry.IEntryCatalog>());
                await inspector.PreviewLoading; TimelinePump(window);
                Assert.That(inspector.ShaderPreview, Is.Null);
                Assert.That(inspector.ShaderPreviewError, Does.Contain("not found"));
                return true;
            }
            finally { window.Close(); await projects.CloseAsync(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task ShaderInspectorBuildsMetadataFieldsAndPatchesOnlySelectedParameter()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var assets = new MetadataAssets(); assets.File.Source = EffectEditorSource;
            using var resources = new MetadataResources(); using var services = EffectServices(assets, resources);
            var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "ShaderInspector"), "Shader", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ApplyEffect).Single();
            entry.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program");
            entry.Source.Parameters["parameters"] = JsonSerializer.Deserialize<JsonElement>("{\"amount\":0.6,\"unknown\":[9007199254740993,null]}");
            scope.GetRequiredService<IEditorDocumentService>().SynchronizeGroups();
            var query = scope.GetRequiredService<ProjectSceneTargets>(); await query.PrepareAsync();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = entry;
            using var inspector = new EntryInspectorViewModel(page, sceneTargets: query);
            var view = new EntryInspectorView { DataContext = inspector };
            var groupView = new GroupEditorPanelView { DataContext = page };
            var layout = new Grid { ColumnDefinitions = new("45*,55*") }; layout.Children.Add(groupView); Grid.SetColumn(view, 1); layout.Children.Add(view);
            var window = new Window { Content = layout, Width = 1200, Height = 900 }; window.Show(); TimelinePump(window);
            try
            {
                Assert.That(entry.EffectInlineForm!.Item.Fields.Select(field => field.Id), Is.EqualTo(new[] { "program" }));
                var inline = groupView.GetVisualDescendants().OfType<DtoEditorHost>().Single(host => ReferenceEquals(host.Item, entry.EffectInlineForm.Item));
                Assert.That(inline.GetVisualDescendants().OfType<FieldEditorRow>().Select(row => row.Context!.Field.Id), Is.EqualTo(new[] { "program" }));
                Assert.That(inspector.Form!.Item.Fields.Select(field => field.Id), Does.Not.Contain("id"));
                Assert.That(inspector.ShaderForm!.Item.Fields.Select(field => field.Id), Is.EqualTo(new[] { "amount", "tint", "offset", "direction", "channels", "style", "mask" }));
                Assert.That(view.GetVisualDescendants().OfType<DtoEditorHost>().Count(host => host.Item?.Fields.Any(field => field.Id == "parameters") == true), Is.Zero,
                    "The shader inspector must not expose a second raw JSON editor.");
                var form = view.GetVisualDescendants().OfType<DtoEditorHost>().Single(host => host.Name == "ShaderParameters");
                var rows = form.GetVisualDescendants().OfType<FieldEditorRow>().ToDictionary(row => row.Context!.Field.Id);
                Assert.That(rows.Keys, Is.EquivalentTo(new[] { "amount", "tint", "offset", "direction", "channels", "style", "mask" }));
                Assert.That(rows["amount"].Editor, Is.InstanceOf<NumericUpDown>());
                foreach (var (name, count) in new[] { ("offset", 2), ("direction", 3), ("channels", 4) })
                {
                    Assert.That(rows[name].Editor, Is.Not.Null);
                    Assert.That(rows[name].Editor!.GetVisualDescendants().OfType<TextBox>().Count(), Is.EqualTo(count));
                    Assert.That(rows[name].Editor!.GetVisualDescendants().OfType<Button>().Any(button => button.Name is "AddItem" or "RemoveItem" or "MoveItem"), Is.False);
                }
                Assert.That(entry.Source.Parameters["parameters"].TryGetProperty("style", out _), Is.False, "Metadata defaults must remain unprovided until edited.");
                var old = rows["amount"].Context!;
                var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
                Assert.That(await old.CommitValueAsync(.8f), Is.True);
                var changed = entry.Source.Parameters["parameters"];
                Assert.That(changed.GetProperty("amount").GetSingle(), Is.EqualTo(.8f));
                Assert.That(changed.GetProperty("unknown")[0].GetInt64(), Is.EqualTo(9007199254740993L));
                var vectorX = NumericControlTestSupport.NamedInputs(rows["direction"].Editor!, "ShaderVectorX").Single();
                vectorX.Focus(); vectorX.Text = "2.5"; TimelinePump(window); PressGroupKey(vectorX, Key.Enter); TimelinePump(window);
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("direction").EnumerateArray().Select(value => value.GetSingle()), Is.EqualTo(new[] { 2.5f, 0f, 0f }));
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("unknown")[0].GetInt64(), Is.EqualTo(9007199254740993L));
                Assert.That(history.CanUndo, Is.True);
                await history.UndoAsync();
                Assert.That(entry.Source.Parameters["parameters"].TryGetProperty("direction", out _), Is.False);
                await history.UndoAsync();
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("amount").GetSingle(), Is.EqualTo(.6f));
                var tint = rows["tint"].Editor!.GetVisualDescendants().OfType<TextBox>().Single(input => input.Name == "ColorHex");
                tint.Focus(); tint.Text = "#11223344"; TimelinePump(window); PressGroupKey(tint, Key.Enter); TimelinePump(window);
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("tint").GetString(), Is.EqualTo("#11223344"));
                assets.File.Source = EffectEditorSource.Replace("amount", "replacement", StringComparison.Ordinal);
                resources.RaiseChanged(); TimelinePump(window); await query.PrepareAsync(); TimelinePump(window);
                Assert.That(inspector.ShaderForm!.Item.Fields.Any(field => field.Id == "replacement"), Is.True);
                Assert.That(await old.CommitValueAsync(.9f), Is.False, "Reloading metadata must retire the old field binding.");
                entry.Source.Parameters["program"] = JsonSerializer.SerializeToElement("other");
                entry.RefreshAuthorReferences(scope.GetRequiredService<GalNet.Core.Entry.IEntryCatalog>());
                Assert.That(inspector.ShaderForm, Is.Null);
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("amount").GetSingle(), Is.EqualTo(.6f));
                entry.Source.Parameters["program"] = JsonSerializer.SerializeToElement("");
                entry.RefreshAuthorReferences(scope.GetRequiredService<GalNet.Core.Entry.IEntryCatalog>());
                Assert.That(inspector.Form!.Item.Fields.Select(field => field.Id), Does.Contain("id"));
                return true;
            }
            finally { window.Close(); await projects.CloseAsync(); }
        }, CancellationToken.None);
    }

    [Test]
    public async Task EffectMetadataLoadsOffQueryAndRefreshRejectsOldLeasesWithoutResurrectingDisposedProjects()
    {
        using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
        var project = await projects.CreateAsync(Path.Combine(_root, "MetadataLifetime"), "Metadata", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
        var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
        var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
        var fx = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ApplyEffect).Single();
        fx.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program");
        using var assets = new MetadataAssets(); var old = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously); assets.File.Gate = old;
        using var query = new ProjectSceneTargets(scope.GetRequiredService<IEditorDocumentService>(), assets);
        var state = query.Before(group.Id, null); var revision = query.Revision;
        Assert.That(state.Complete, Is.False); Assert.That(state.Targets.Single().Properties.Select(property => property.Name), Is.EqualTo(new[] { "progress" }));
        await assets.File.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var cancelled = new CancellationTokenSource(); var wait = query.PrepareAsync(cancelled.Token); cancelled.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await wait);
        Assert.That(query.Revision, Is.EqualTo(revision), "Cancelling one caller must not cancel the shared resource generation.");
        var firstPreparation = query.PrepareAsync();
        assets.File.Gate = null; assets.File.Source = EffectSource.Replace("amount", "replacement"); query.InvalidateResources();
        await query.PrepareAsync(); var refreshed = query.Revision;
        Assert.That(query.Before(group.Id, null).Targets.Single().Properties.Any(property => property.Name == "replacement"), Is.True);
        old.SetResult(Encoding.UTF8.GetBytes(EffectSource)); await firstPreparation;
        Assert.That(query.Before(group.Id, null).Targets.Single().Properties.Any(property => property.Name == "amount"), Is.False);
        Assert.That(query.Revision, Is.EqualTo(refreshed), "A superseded loader cannot publish into the refreshed generation.");
        assets.File.Source = "invalid metadata"; query.InvalidateResources(); await query.PrepareAsync();
        Assert.That(query.Before(group.Id, null).Complete, Is.False); var reads = assets.File.Reads;
        await query.PrepareAsync(); Assert.That(assets.File.Reads, Is.EqualTo(reads), "Negative results are cached until a resource refresh.");
        assets.File.Source = EffectSource; assets.File.TypeId = "sprite"; query.InvalidateResources(); await query.PrepareAsync();
        Assert.That(assets.File.Reads, Is.EqualTo(reads)); Assert.That(query.Before(group.Id, null).Complete, Is.False);
        assets.File.TypeId = "effect-program"; assets.File.DeclaredLength = 1024 * 1024 + 1; query.InvalidateResources(); await query.PrepareAsync();
        Assert.That(assets.File.Reads, Is.EqualTo(reads)); Assert.That(query.Before(group.Id, null).Complete, Is.False);
        assets.File.DeclaredLength = null; assets.File.Bytes = [0xff]; query.InvalidateResources(); await query.PrepareAsync();
        Assert.That(query.Before(group.Id, null).Complete, Is.False, "Malformed UTF-8 cannot publish declarations."); assets.File.Bytes = null;
        fx.Source.Parameters["program"] = JsonSerializer.SerializeToElement("missing"); await query.PrepareAsync();
        Assert.That(query.Before(group.Id, null).Complete, Is.False); fx.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program");
        var pending = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously); assets.File.Gate = pending;
        query.InvalidateResources(); var preparing = query.PrepareAsync(); query.Dispose(); var disposedRevision = query.Revision;
        pending.SetResult(Encoding.UTF8.GetBytes(EffectSource)); await preparing;
        Assert.That(query.Revision, Is.EqualTo(disposedRevision)); Assert.That(query.Before(group.Id, null).Reachable, Is.False);
        Assert.That(assets.SynchronousReads, Is.Zero); await projects.CloseAsync();
    }

    [Test]
    public async Task AnimationPropertyChoicesFollowSelectedLayerAndShaderMetadata()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            using var assets = new MetadataAssets(); using var resources = new MetadataResources();
            using var services = EffectServices(assets, resources); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "AnimationChoices"), "Choices", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            try
            {
                var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
                var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
                var layer = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ShowLayer).Single();
                var fx = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.ApplyEffect).Single();
                fx.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program");
                var animation = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.Animate).Single();
                scope.GetRequiredService<IEditorDocumentService>().SynchronizeGroups();
                var catalog = scope.GetRequiredService<SceneTargetCatalog>();
                await scope.GetRequiredService<ProjectSceneTargets>().PrepareAsync();
                string[] Choices() => catalog.FieldOptions(group.Id, animation.StableId, animation.Type, "property")!.Select(option => option.Id).ToArray();
                animation.Source.Parameters["handleId"] = layer.Source.Parameters["handleId"].Clone();
                Assert.That(Choices(), Does.Contain("opacity")); Assert.That(Choices(), Does.Not.Contain("amount"));
                animation.Source.Parameters["handleId"] = fx.Source.Parameters["instanceId"].Clone();
                Assert.That(Choices(), Does.Contain("amount")); Assert.That(Choices(), Does.Contain("progress")); Assert.That(Choices(), Does.Not.Contain("opacity"));
                animation.Source.Parameters["handleId"] = JsonSerializer.SerializeToElement("unknown");
                Assert.That(Choices(), Is.Empty);
            }
            finally { await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task EffectMetadataTimelineUsesRealPropertyMenuHistoryRefreshAndSaveReopen()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var assets = new MetadataAssets(); using var resources = new MetadataResources();
            using var services = EffectServices(assets, resources); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "MetadataTimeline"), "Metadata", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var fx = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ApplyEffect).Single(); var handle = fx.Source.Parameters["instanceId"].GetString();
            fx.Source.Parameters["program"] = JsonSerializer.SerializeToElement("program"); fx.Source.Parameters["parameters"] = JsonSerializer.Deserialize<JsonElement>("{\"amount\":0.6,\"unknown\":[9007199254740993,null]}");
            var timeline = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.PlayTimeline).Single();
            timeline.Source.Parameters["timeline"] = JsonSerializer.Deserialize<JsonElement>("{\"durationFrames\":20,\"tracks\":[],\"events\":[]}");
            scope.GetRequiredService<IEditorDocumentService>().SynchronizeGroups();
            var query = scope.GetRequiredService<ProjectSceneTargets>(); await query.PrepareAsync();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id); page.SelectedEntry = timeline;
            var session = scope.GetRequiredService<TimelineEditSession>(); var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var catalog = scope.GetRequiredService<SceneTargetCatalog>(); using var lease = new SceneTargetSelection(catalog, catalog.Query(group.Id, timeline.StableId));
            Assert.That(lease.Select(lease.Items.Single().Id), Is.True);
            var view = scope.GetRequiredService<GalNet.Editor.Views.TimelinePanelView>(); view.DataContext = scope.GetRequiredService<TimelinePanelViewModel>();
            var window = new Window { Content = view, Width = 1350, Height = 950 }; window.Show(); TimelinePump(window);
            try
            {
                Assert.That(session.AnalysisIncomplete, Is.False);
                var draft = session.BeginObjectEdit()!; draft.Select(draft.Choices.Single().Id); Assert.That(draft.Commit(), Is.True);
                session.Frame = 10; TimelinePump(window);
                var plus = view.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "AddTimelineProperty" && control.IsVisible && control.DataContext is TimelineTreeNode node && node.ObjectHandle == handle);
                TimelineClick(window, plus); var menu = (MenuFlyout)FlyoutBase.GetAttachedFlyout(plus)!;
                menu.Items.OfType<MenuItem>().Single(item => item.DataContext is TimelineValueRow row && row.Property == "amount").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.Hide(); TimelinePump(window);
                Assert.That(timeline.Source.Parameters["timeline"].GetProperty("tracks")[0].GetProperty("keys")[0].GetProperty("value").GetSingle(), Is.EqualTo(.6f));
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); history.Clear(); TimelinePump(window);
                var row = session.Rows.Single(item => item.HasTrack); var input = view.GetVisualDescendants().OfType<NumericUpDown>().Single(control => ReferenceEquals(control.DataContext, row));
                var text = input.GetVisualDescendants().OfType<TextBox>().Single(); text.Focus(); text.Text = "0.8"; TimelinePump(window); PressGroupKey(text, Key.Enter); TimelinePump(window);
                await history.UndoAsync(); Assert.That(history.CanUndo, Is.False); await history.RedoAsync(); TimelinePump(window);
                var stale = session.Rows.Single(item => item.HasTrack); var saved = timeline.Source.Parameters["timeline"].Clone(); history.Clear();
                assets.File.Source = EffectSource.Replace("amount", "replacement"); resources.RaiseChanged(); TimelinePump(window); await query.PrepareAsync(); TimelinePump(window);
                Assert.That(lease.TryResolve(out _), Is.False); stale.DraftValue = .9m; Assert.That(stale.Commit(), Is.False);
                Assert.That(session.Rows.Any(item => item.Property == "replacement"), Is.True);
                Assert.That(query.Before(group.Id, timeline.StableId).Targets.Single().Properties.Any(property => property.Name == "amount"), Is.False);
                Assert.That(session.Rows.Single(item => item.HasTrack).Property, Is.EqualTo("amount"));
                Assert.That(JsonElement.DeepEquals(timeline.Source.Parameters["timeline"], saved), Is.True); Assert.That(history.CanUndo, Is.False);
                await workspace.SaveAsync(); var path = project.RootPath; var groupId = group.Id; var entryId = timeline.StableId; var fxId = fx.StableId;
                window.Close(); page.Dispose(); await projects.CloseAsync(); project = await projects.OpenAsync(path);
                var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId);
                Assert.That(JsonElement.DeepEquals(reopened.Entries.Single(item => item.StableId == entryId).Source.Parameters["timeline"], saved), Is.True);
                Assert.That(reopened.Entries.Single(item => item.StableId == fxId).Source.Parameters["parameters"].GetProperty("unknown")[0].GetInt64(), Is.EqualTo(9007199254740993L));
                await project.Services.GetRequiredService<ProjectSceneTargets>().PrepareAsync();
                Assert.That(project.Services.GetRequiredService<ProjectSceneTargets>().Before(groupId, entryId).Targets.Single().Properties.Any(property => property.Name == "replacement"), Is.True);
                await projects.CloseAsync();
            }
            finally { window.Close(); if (projects.Current is not null) await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    private sealed class MetadataAssets : IAssetManager
    {
        public MetadataFile File { get; } = new();
        public int SynchronousReads => File.SynchronousReads;
        public Task<IGameFile?> GetFileAsync(string assetId, CancellationToken ct = default) => Task.FromResult<IGameFile?>(assetId == "program" ? File : null);
        public Task<IReadOnlyList<IGameFile>> GetFilesAsync(string? typeId = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<IGameFile>>([File]);
        public Task<AssetHandle<T>?> AcquireAsync<T>(string assetId, CancellationToken ct = default) where T : class => throw new NotSupportedException();
        public Task<AssetHandle<T>?> AcquireByPathAsync<T>(string path, CancellationToken ct = default) where T : class => throw new NotSupportedException();
        public void RegisterDecoder<T>(string resourceTypeId, IAssetDecoder<T> decoder) where T : class => throw new NotSupportedException();
        public void RegisterProvider(IAssetProvider provider) => throw new NotSupportedException();
        public void Dispose() { }
    }
    private sealed class MetadataFile : IGameFile
    {
        public string Source { get; set; } = EffectSource;
        public TaskCompletionSource<byte[]>? Gate { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads, SynchronousReads;
        public string Id => "program";
        public string Path => "Effects/test.sksl";
        public string TypeId { get; set; } = "effect-program";
        public AssetMeta Metadata => new() { Id = Id, Path = Path, TypeId = TypeId };
        public long? DeclaredLength { get; set; }
        public byte[]? Bytes { get; set; }
        public long Length => DeclaredLength ?? Source.Length;
        public string? Hash => null;
        public Stream OpenRead() { Interlocked.Increment(ref SynchronousReads); throw new NotSupportedException(); }
        public byte[] ReadAllBytes() { Interlocked.Increment(ref SynchronousReads); throw new NotSupportedException(); }
        public Task<byte[]> ReadAllBytesAsync(CancellationToken ct = default)
        { Interlocked.Increment(ref Reads); var gate = Gate; Entered.TrySetResult(); return gate?.Task ?? Task.FromResult(Bytes ?? Encoding.UTF8.GetBytes(Source)); }
    }
    private sealed class MetadataResources : IAssetCatalogService
    {
        public event Action? Changed;
        public void RaiseChanged() => Changed?.Invoke();
        public IReadOnlyList<AssetEntry> GetDirectory(string relativeDirectory) => [];
        public bool DirectoryExists(string relativeDirectory) => false;
        public AssetEntry? GetEntry(string relativePath) => null;
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> GetThumbnailPathAsync(AssetEntry entry, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public void Dispose() { }
    }
}
