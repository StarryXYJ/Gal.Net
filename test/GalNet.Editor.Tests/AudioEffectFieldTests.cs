using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    private static async Task AudioSet(EntryEditorItemViewModel entry, string id, object value)
    {
        var form = entry.DetailForm!; var descriptor = form.Fields.Single(field => field.Id == id);
        Assert.That(await form.Binding(descriptor).CommitAsync(new(JsonSerializer.SerializeToElement(value)), default), Is.Null);
    }

    [Test]
    public async Task AudioEffectTypeAndNumericParametersAreMountedAndTypeChangeIsOneUndo()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var defaults = BuiltinAudioDefaults.Create();
            var project = await projects.CreateAsync(Path.Combine(_root, "EffectTypes"), "Audio", new DefaultProjectOptions
            { AudioTracks = defaults.Tracks.ToList(), AudioBuses = defaults.Buses.ToList() }.ToConfiguration(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ApplyAudioSEffect).Single();
            var identity = entry.Source.Parameters["instanceId"].GetString(); Assert.That(Guid.TryParse(identity, out _), Is.True);
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            await AudioSet(entry, "targetId", "bgm");
            var inspector = new DtoEditorHost { Item = entry.DetailForm!.Item, Registry = page.FieldEditors };
            inspector.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Editor/")) { Source = new Uri("avares://GalNet.Editor/Controls/AuthorFormStyles.axaml") });
            var window = new Window { Content = inspector, Width = 900, Height = 1000 }; window.Show(); TimelinePump(window);
            try
            {
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
                var input = inspector.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "AudioEffectTypeInput");
                Assert.That(input.Items.Count, Is.EqualTo(4));
                var reverb = input.Items.Cast<object>().Single(item => item.ToString() == project.Services.GetRequiredService<IEditorLocalizationService>()["Audio.Type.seffect.reverb"]);
                input.SelectedItem = reverb; TimelinePump(window);
                Assert.That(entry.Source.Parameters["id"].GetString(), Is.EqualTo("seffect.reverb"));
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("mix").GetSingle(), Is.EqualTo(.25f));
                Assert.That(inspector.GetVisualDescendants().OfType<TextBox>().Count(control => control.Name?.StartsWith("AudioParameter_") == true), Is.EqualTo(4));
                Assert.That(inspector.GetVisualDescendants().OfType<FieldEditorRow>().Where(row => row.Context?.Field.Id == "instanceId").All(row => !row.IsEffectivelyVisible), Is.True);
                await history.UndoAsync(); TimelinePump(window);
                Assert.That(entry.Source.Parameters.GetValueOrDefault("id").ValueKind is JsonValueKind.Undefined or JsonValueKind.Null, Is.True);
                Assert.That(entry.Source.Parameters["instanceId"].GetString(), Is.EqualTo(identity));
                Assert.That(history.CanUndo, Is.False);
                await history.RedoAsync(); TimelinePump(window); history.Clear();
                var mix = inspector.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "AudioParameter_mix");
                mix.Text = "0.4"; PressGroupKey(mix, Key.Enter); TimelinePump(window);
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("mix").GetSingle(), Is.EqualTo(.4f));
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("roomSize").GetSingle(), Is.EqualTo(.5f));
                await history.UndoAsync(); TimelinePump(window);
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("mix").GetSingle(), Is.EqualTo(.25f));
                mix = inspector.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "AudioParameter_mix");
                mix.Text = "99"; PressGroupKey(mix, Key.Enter); TimelinePump(window);
                Assert.That(entry.Source.Parameters["parameters"].GetProperty("mix").GetSingle(), Is.EqualTo(.25f));
                var issue = ((FieldEditorContext)mix.DataContext!).Issue;
                Assert.That(issue?.Code, Is.EqualTo("value.range"));
                Assert.That(inspector.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Text == issue!.Message), Is.True);
            }
            finally { window.Close(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task AudioStopAndUpdateUseOnlyPriorEffectsOnTheSelectedAttachment()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var defaults = BuiltinAudioDefaults.Create();
            var project = await projects.CreateAsync(Path.Combine(_root, "EffectTargets"), "Audio", new DefaultProjectOptions
            { AudioTracks = defaults.Tracks.ToList(), AudioBuses = defaults.Buses.ToList() }.ToConfiguration(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var first = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.ApplyAudioSEffect).Single();
            var second = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.ApplyAudioSEffect).Single();
            var bus = workspace.InsertEntriesInto(group, 2, 1, BuiltinEntryIds.ApplyAudioSEffect).Single();
            var stop = workspace.InsertEntriesInto(group, 3, 1, BuiltinEntryIds.StopAudioSEffect).Single();
            var update = workspace.InsertEntriesInto(group, 3, 1, BuiltinEntryIds.UpdateAudioSEffect).Single();
            var future = workspace.InsertEntriesInto(group, group.Entries.Count, 1, BuiltinEntryIds.ApplyAudioSEffect).Single();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            foreach (var entry in new[] { first, second, bus, future }) { await AudioSet(entry, "targetId", "bgm"); await AudioSet(entry, "id", "seffect.reverb"); }
            await AudioSet(bus, "scope", "bus"); await AudioSet(stop, "targetId", "bgm"); await AudioSet(update, "targetId", "bgm");
            var inspector = new DtoEditorHost { Item = stop.DetailForm!.Item, Registry = page.FieldEditors };
            inspector.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Editor/")) { Source = new Uri("avares://GalNet.Editor/Controls/AuthorFormStyles.axaml") });
            var window = new Window { Content = inspector, Width = 900, Height = 1000 }; window.Show(); TimelinePump(window);
            try
            {
                var input = inspector.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "AudioEffectInstanceInput");
                Assert.That(input.Items.Count, Is.EqualTo(3)); // Two Track effects plus the explicit all option.
                Assert.That(input.Items.Cast<object>().Select(item => item.ToString()).Distinct().Count(), Is.EqualTo(3));
                input.SelectedIndex = 1; TimelinePump(window);
                Assert.That(new[] { first, second }.Select(entry => entry.Source.Parameters["instanceId"].GetString()), Does.Contain(stop.Source.Parameters["instanceId"].GetString()));
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
                input.SelectedIndex = 0; TimelinePump(window);
                Assert.That(stop.Source.Parameters["all"].GetBoolean(), Is.True); Assert.That(stop.Source.Parameters.ContainsKey("instanceId"), Is.False);
                await history.UndoAsync(); TimelinePump(window); Assert.That(stop.Source.Parameters["all"].GetBoolean(), Is.False);
                inspector.Item = update.DetailForm!.Item; TimelinePump(window);
                input = inspector.GetVisualDescendants().OfType<ComboBox>().Single(control => control.Name == "AudioEffectInstanceInput");
                Assert.That(input.Items.Count, Is.EqualTo(2)); input.SelectedIndex = 0; TimelinePump(window);
                Assert.That(inspector.GetVisualDescendants().OfType<TextBox>().Count(control => control.Name?.StartsWith("AudioParameter_") == true), Is.EqualTo(4));
                Assert.That(inspector.GetVisualDescendants().OfType<ComboBox>().Any(control => control.Name == "AudioEffectTypeInput"), Is.False);
                var mix = inspector.GetVisualDescendants().OfType<TextBox>().Single(control => control.Name == "AudioParameter_mix");
                mix.Text = "0.6"; PressGroupKey(mix, Key.Enter); TimelinePump(window);
                Assert.That(update.Source.Parameters["parameters"].EnumerateObject().Select(property => property.Name), Is.EqualTo(new[] { "mix" }));
                var catalog = project.Services.GetRequiredService<SceneTargetCatalog>();
                var snapshot = catalog.QueryAudio(group.Id, update.StableId);
                Assert.That(catalog.IsCurrent(snapshot), Is.True);
                await AudioSet(first, "id", "seffect.gain");
                Assert.That(catalog.IsCurrent(snapshot), Is.False);
                await history.UndoAsync();
                Assert.That(catalog.IsCurrent(snapshot), Is.False, "Undo must not revive the old selection lease.");

            }
            finally { window.Close(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }
}
