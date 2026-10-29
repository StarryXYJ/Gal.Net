using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task EntrySearchUsesLocalizedNamesIdsAliasesAndKeyboardWithoutChangingOnCancel()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            var localization = new EditorLocalizationService(); localization.ApplyLocale("zh-CN");
            var catalog = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
            var picker = new EntryTypePicker { Options = catalog.Definitions.Select(item => new EntryTypeOption(item.Type, item.Category, "Entry.Type." + item.Type)).Append(new("custom.echo", "Custom", "Entry.Type.custom.echo", ["huisheng", "hs"])).ToArray() };
            var window = new Window { Content = picker, Width = 650, Height = 500 }; window.Show(); window.UpdateLayout();
            try
            {
                var confirmations = new List<string>(); picker.TypeConfirmed += (_, args) => confirmations.Add(args.Type);
                var search = picker.FindControl<TextBox>("SearchInput")!; var tree = picker.FindControl<TreeView>("TypeTree")!;
                picker.OpenAt(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Assert.That(search.IsFocused, Is.True);
                SetGroupSearch(search, "等待"); Assert.That(picker.Results.Single().Value, Is.EqualTo(BuiltinEntryIds.Wait)); Assert.That(tree.SelectedItem, Is.Null);
                tree.UpdateLayout(); Assert.That(tree.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == BuiltinEntryIds.Wait), Is.True, "The leaf displays its original English ID alongside the localized name.");
                PressGroupKey(search, Key.Up); Assert.That(((EntryTypeLeafItem)tree.SelectedItem!).Value, Is.EqualTo(BuiltinEntryIds.Wait));
                PressGroupKey(search, Key.Enter); Assert.That(confirmations, Is.EqualTo(new[] { BuiltinEntryIds.Wait })); Assert.That(picker.IsOpen, Is.False);
                picker.OpenAt(); Dispatcher.UIThread.RunJobs(); SetGroupSearch(search, "FLOW.WAIT"); PressGroupKey(search, Key.Down); PressGroupKey(search, Key.Enter);
                Assert.That(confirmations.Count, Is.EqualTo(2), "Confirming the same type again is an event, even if SelectedValue did not change.");
                picker.OpenAt(); Dispatcher.UIThread.RunJobs(); SetGroupSearch(search, "dengdai"); Assert.That(picker.Results.Single().Value, Is.EqualTo(BuiltinEntryIds.Wait));
                PressGroupKey(search, Key.Down); var presenter = search.GetVisualDescendants().OfType<TextPresenter>().Single(); presenter.PreeditText = "拼";
                PressGroupKey(search, Key.Enter); Assert.That(confirmations.Count, Is.EqualTo(2), "IME Enter must not confirm a type."); presenter.PreeditText = null;
                SetGroupSearch(search, "hs"); Assert.That(picker.Results.Single().DisplayName, Is.EqualTo("custom.echo"));
                SetGroupSearch(search, "no such entry"); PressGroupKey(search, Key.Down); PressGroupKey(search, Key.Enter); Assert.That(confirmations.Count, Is.EqualTo(2));
                SetGroupSearch(search, ""); PressGroupKey(search, Key.Up); var first = tree.SelectedItem; PressGroupKey(search, Key.Down); Assert.That(tree.SelectedItem, Is.Not.SameAs(first)); PressGroupKey(search, Key.Up); Assert.That(tree.SelectedItem, Is.SameAs(first));
                localization.ApplyLocale("en-US"); SetGroupSearch(search, "Wait"); Assert.That(picker.Results.Any(item => item.Value == BuiltinEntryIds.Wait), Is.True);
                foreach (var locale in new[] { "ja-JP", "ko-KR", "zh-CN" }) { localization.ApplyLocale(locale); SetGroupSearch(search, localization["Entry.Type.flow.wait"]); Assert.That(picker.Results.Any(item => item.Value == BuiltinEntryIds.Wait), Is.True); }
                PressGroupKey(search, Key.Escape); Assert.That(confirmations.Count, Is.EqualTo(2));
            }
            finally { window.Close(); }
            await Task.CompletedTask; return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task GroupToolbarBatchesAreSingleHistoryStepsAndSpaceInsertsOneAtCapturedAnchor()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme();
            using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "GroupUi"), "GroupUi", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            workspace.InsertEntriesInto(group, 0, 3, BuiltinEntryIds.Wait);
            using var vm = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id); vm.InsertionType = BuiltinEntryIds.Wait;
            var view = new EntryListEditorControl { DataContext = vm }; var window = new Window { Content = view, Width = 1100, Height = 750 }; window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            try
            {
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear();
                var original = group.Entries.Select(entry => entry.StableId).ToArray(); vm.SelectedEntry = group.Entries[0]; vm.BatchAddCount = 3;
                var picker = view.FindControl<EntryTypePicker>("InsertPicker")!; var appendPicker = view.FindControl<EntryTypePicker>("AppendPicker")!;
                Click(window, picker.FindControl<ToggleButton>("OpenButton")!); Dispatcher.UIThread.RunJobs(); var search = picker.FindControl<TextBox>("SearchInput")!; SetGroupSearch(search, "flow.wait"); PressGroupKey(search, Key.Down); PressGroupKey(search, Key.Enter);
                Assert.That(group.Entries, Has.Count.EqualTo(6)); Assert.That(group.Entries[0].StableId, Is.EqualTo(original[0])); Assert.That(group.Entries[4].StableId, Is.EqualTo(original[1]));
                var inserted = group.Entries.Skip(1).Take(3).Select(entry => entry.StableId).ToArray(); Assert.That(inserted.Distinct().Count(), Is.EqualTo(3));
                await history.UndoAsync(); Assert.That(group.Entries.Select(entry => entry.StableId), Is.EqualTo(original)); Assert.That(history.CanUndo, Is.False);
                await history.RedoAsync(); Assert.That(group.Entries.Skip(1).Take(3).Select(entry => entry.StableId), Is.EqualTo(inserted));
                vm.SelectedEntry = group.Entries[0]; vm.BatchAddCount = 2; Click(window, appendPicker.FindControl<ToggleButton>("OpenButton")!); Dispatcher.UIThread.RunJobs(); var appendSearch = appendPicker.FindControl<TextBox>("SearchInput")!; SetGroupSearch(appendSearch, "flow.wait"); PressGroupKey(appendSearch, Key.Down); PressGroupKey(appendSearch, Key.Enter); Assert.That(group.Entries, Has.Count.EqualTo(8)); Assert.That(vm.SelectedEntry, Is.SameAs(group.Entries[^1]));
                var list = view.FindControl<ListBox>("ItemsListBox")!;
                vm.SelectedEntry = group.Entries[0]; list.Focus(); PressGroupKey(list, Key.Space); Dispatcher.UIThread.RunJobs();
                Assert.That(picker.IsOpen, Is.True); SetGroupSearch(search, "flow.wait"); PressGroupKey(search, Key.Down); PressGroupKey(search, Key.Enter);
                Assert.That(group.Entries, Has.Count.EqualTo(9), "Picker confirmation always inserts one, independent of batch count."); Assert.That(vm.SelectedEntry, Is.SameAs(group.Entries[1])); Assert.That(vm.SelectedEntry!.Type, Is.EqualTo(BuiltinEntryIds.Wait));
                foreach (var entry in group.Entries) entry.IsParametersExpanded = false;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().First(); scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)); window.UpdateLayout();
                var point = list.TranslatePoint(new Point(list.Bounds.Width / 2, list.Bounds.Height - 16), window)!.Value; window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                Assert.That(vm.SelectedEntry, Is.Null); list.Focus(); PressGroupKey(list, Key.Space); Dispatcher.UIThread.RunJobs(); SetGroupSearch(search, "flow.wait"); PressGroupKey(search, Key.Down); PressGroupKey(search, Key.Enter);
                Assert.That(group.Entries, Has.Count.EqualTo(10)); Assert.That(vm.SelectedEntry, Is.SameAs(group.Entries[^1]));
                vm.SelectedEntry = group.Entries[0]; vm.InsertionType = "not.available"; vm.BatchAddCount = 3;
                Click(window, appendPicker.FindControl<ToggleButton>("OpenButton")!); Dispatcher.UIThread.RunJobs(); Assert.That(appendPicker.IsOpen, Is.True);
                SetGroupSearch(appendSearch, "flow.wait"); PressGroupKey(appendSearch, Key.Down); PressGroupKey(appendSearch, Key.Enter);
                Assert.That(group.Entries, Has.Count.EqualTo(13), "A button that requires type selection retains its batch count and append intent."); Assert.That(vm.SelectedEntry, Is.SameAs(group.Entries[^1]));
                foreach (var entry in group.Entries) entry.IsParametersExpanded = false;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); scroll.Offset = new Vector(0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)); window.UpdateLayout();
                var lastContainer = list.ContainerFromIndex(group.Entries.Count - 1)!;
                var bottom = lastContainer.TranslatePoint(new Point(0, lastContainer.Bounds.Height), list)!.Value.Y;
                Assert.That(list.Bounds.Height - bottom, Is.GreaterThanOrEqualTo(40), "The scrollable tail remains available even when entries fill the viewport.");
                vm.SelectedEntry = group.Entries[0]; list.Focus(); PressGroupKey(list, Key.Space); Dispatcher.UIThread.RunJobs();
                var anchor = vm.SelectedEntry!; workspace.RemoveEntryFrom(group, anchor); var count = group.Entries.Count;
                SetGroupSearch(search, "flow.wait"); PressGroupKey(search, Key.Down); PressGroupKey(search, Key.Enter); Assert.That(group.Entries.Count, Is.EqualTo(count)); Assert.That(vm.ErrorText, Is.Not.Empty);
                var number = view.FindControl<NumericUpDown>("BatchCount")!; number.Focus(); var key = PressGroupKey(number, Key.Space); Assert.That(picker.IsOpen, Is.False); Assert.That(key.Handled, Is.False);
                var preferences = project.Services.GetRequiredService<EditorSettingsPanelViewModel>();
                preferences.ExpandNewEntries = false;
                var newEntry = workspace.InsertEntriesInto(group, group.Entries.Count, 1, BuiltinEntryIds.Wait).Single();
                Assert.That(newEntry.IsParametersExpanded, Is.False);
                newEntry.IsParametersExpanded = true;
                preferences.ExpandNewEntries = true;
                Assert.That(newEntry.IsParametersExpanded, Is.True, "Changing the preference does not reset existing entries.");
                Assert.That(workspace.InsertEntriesInto(group, group.Entries.Count, 1, BuiltinEntryIds.Wait).Single().IsParametersExpanded, Is.True);
                preferences.Dispose();
                await project.Services.GetRequiredService<IProjectContentSession>().SaveAsync(); Assert.That(project.Services.GetRequiredService<IProjectContentSession>().IsDirty, Is.False);
                if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts) { using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "group-toolbar.png")); }
            }
            finally { window.Close(); vm.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task CollapsedEntryRemainsCollapsedInDragPreviewAfterReorderAndUndo()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "FoldDrag"), "FoldDrag", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            workspace.InsertEntriesInto(group, 0, 3, BuiltinEntryIds.Wait); using var vm = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(project.Services, group.Id);
            var view = new EntryListEditorControl { DataContext = vm }; var window = new Window { Content = view, Width = 1000, Height = 700 }; window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            try
            {
                var history = project.Services.GetRequiredService<EditorHistories>().Content; history.Clear(); var entry = group.Entries[0];
                var snapshot = JsonSerializer.Serialize(project.Services.GetRequiredService<IProjectContentSession>().CaptureWorking());
                var toggle = view.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "ParametersToggle" && ReferenceEquals(button.DataContext, entry)); Click(window, toggle); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.That(entry.IsParametersExpanded, Is.False); Assert.That(history.CanUndo, Is.False); Assert.That(JsonSerializer.Serialize(project.Services.GetRequiredService<IProjectContentSession>().CaptureWorking()), Is.EqualTo(snapshot));
                var handle = view.GetVisualDescendants().OfType<Border>().Single(border => border.Child is TextBlock { Text: "::" } && ReferenceEquals(border.DataContext, entry));
                var list = view.FindControl<ListBox>("ItemsListBox")!; var target = list.ContainerFromIndex(2)!;
                var start = handle.TranslatePoint(new Point(8, 8), window)!.Value; var end = target.TranslatePoint(new Point(20, target.Bounds.Height - 5), window)!.Value;
                window.MouseDown(start, MouseButton.Left); window.MouseMove(end); window.UpdateLayout();
                Assert.That(view.GetVisualDescendants().OfType<ToggleButton>().Where(button => button.Name == "ParametersToggle" && ReferenceEquals(button.DataContext, entry)).All(button => button.IsChecked == false), Is.True);
                window.MouseUp(end, MouseButton.Left); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.That(group.Entries[^1], Is.SameAs(entry)); Assert.That(entry.IsParametersExpanded, Is.False);
                await history.UndoAsync(); Assert.That(group.Entries[0], Is.SameAs(entry)); Assert.That(entry.IsParametersExpanded, Is.False); Assert.That(history.CanUndo, Is.False);
                var parameters = entry.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
                vm.ChangeType(entry, BuiltinEntryIds.HideLayer); Assert.That(entry.Type, Is.EqualTo(BuiltinEntryIds.HideLayer));
                await history.UndoAsync(); Assert.That(entry.Type, Is.EqualTo(BuiltinEntryIds.Wait)); Assert.That(entry.Parameters, Is.EquivalentTo(parameters)); Assert.That(history.CanUndo, Is.False, "Type and all old parameters are restored in one operation.");
            }
            finally { window.Close(); vm.Dispose(); await projects.CloseAsync(); }
            return true;
        }, CancellationToken.None);
    }

    private static void PrepareGroupTheme()
    {
        Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
        foreach (var key in new[] { "AddIcon", "DeleteRegularIcon", "ChevronUpRegular", "ChevronDownRegular" }) Application.Current.Resources[key] = StreamGeometry.Parse("M0,0 L8,8");
    }
    private static void SetGroupSearch(TextBox box, string text) { box.Text = text; Dispatcher.UIThread.RunJobs(); }
    private static KeyEventArgs PressGroupKey(Control control, Key key) { var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key }; control.RaiseEvent(args); return args; }
    private static void Click(Window window, Control control) { window.UpdateLayout(); var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value; window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); window.UpdateLayout(); }
}
