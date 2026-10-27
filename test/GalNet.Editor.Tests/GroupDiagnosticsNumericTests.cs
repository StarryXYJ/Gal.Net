using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Contracts.Builtins;
using GalNet.Core.Parameters;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Controls;
using GalNet.Editor.Dock;
using GalNet.Editor.History;
using GalNet.Editor.Inspector.ViewModels;
using GalNet.Editor.Inspector.Views;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task NumericEditorPathsKeepExactInputBesideSliderAndFallBackWithoutLosingPrecision()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme();
            using var services = Services(); services.GetRequiredService<IEditorLocalizationService>().ApplyLocale("zh-CN");
            var binding = new NumericPathBinding("0.5");
            var field = new FieldDescriptor("opacity", new(typeof(float), constraints: JsonSerializer.SerializeToElement(new { minimum = 0, maximum = 1 })), editor: new(ValueEditorIds.Slider, Step: .1));
            var form = new SchemaForm { Schema = [field], Registry = EditorFieldEditors.CreateDefault(), Binding = _ => binding };
            var window = new Window { Content = form, Width = 700, Height = 400 };
            window.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://GalNet.Editor/")) { Source = new Uri("avares://GalNet.Editor/Controls/AuthorFormStyles.axaml") });
            window.Show(); TimelinePump(window);
            Assert.That(form.Diagnostics, Is.Empty);
            var slider = form.GetVisualDescendants().OfType<Slider>().Single(); var up = form.GetVisualDescendants().OfType<NumericUpDown>().Single();
            var text = up.GetVisualDescendants().OfType<TextBox>().Single(); text.Text = ".53"; PressGroupKey(text, Key.Enter);
            Assert.That(binding.Value.Json!.Value.GetSingle(), Is.EqualTo(.53f));
            Assert.That(slider.Value, Is.EqualTo(.53).Within(.000001), "Step does not quantize a legal precise typed value.");
            slider.Value = .8; Assert.That(binding.Value.Json!.Value.GetSingle(), Is.EqualTo(.8f));
            form.Schema = [new("exact", new(typeof(ulong)), editor: new(ValueEditorIds.NumericUpDown))]; binding.Value = new(JsonSerializer.SerializeToElement(ulong.MaxValue)); binding.Notify();
            var exact = (TextBox)form.Editors.Single().Control; exact.Text = "18446744073709551614"; PressGroupKey(exact, Key.Enter);
            Assert.That(binding.Value.Json!.Value.GetUInt64(), Is.EqualTo(ulong.MaxValue - 1));
            form.Schema = [new("decimal", new(typeof(decimal)), editor: new(ValueEditorIds.NumericUpDown))]; binding.Value = new(JsonSerializer.SerializeToElement(0m)); binding.Notify();
            exact = (TextBox)form.Editors.Single().Control; exact.Text = ".1234567890123456789012345678"; PressGroupKey(exact, Key.Enter);
            Assert.That(binding.Value.Json!.Value.GetDecimal(), Is.EqualTo(.1234567890123456789012345678m));
            form.Schema = [new("text", new(typeof(float)), editor: new(ValueEditorIds.NumericText))];
            Assert.That(form.Editors.Single().Control, Is.InstanceOf<TextBox>());
            form.Schema = [new("badSlider", new(typeof(float)), editor: new(ValueEditorIds.Slider))];
            Assert.That(form.Editors.Single().Control, Is.InstanceOf<TextBox>());
            Assert.That(form.Diagnostics.Single().Code, Is.EqualTo("editor.numericFallback"));
            Assert.That(form.Diagnostics.Single().Message, Does.Contain("精确"));
            window.Close(); return true;
        }, CancellationToken.None);
    }

    private sealed class NumericPathBinding(string json) : IFieldBinding
    {
        public FieldValue Value { get; set; } = new(JsonSerializer.Deserialize<JsonElement>(json));
        public bool IsEnabled => true;
        public bool IsVisible => true;
        public event EventHandler? Changed;
        public FieldValue Read() => Value;
        public void Notify() => Changed?.Invoke(this, EventArgs.Empty);
        public Task<ValueIssue?> CommitAsync(FieldValue value, CancellationToken cancellationToken)
        { Value = value; Notify(); return Task.FromResult<ValueIssue?>(null); }
    }

    private static async Task AwaitAutomaticProblems(ProblemsPanelViewModel problems)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var task = problems.ValidationTask;
            await task.WaitAsync(TimeSpan.FromSeconds(10));
            if (ReferenceEquals(task, problems.ValidationTask)) return;
        }
        Assert.Fail("Automatic checks did not settle.");
    }

    [Test]
    public async Task LocalizationAnalysisWarningsNavigateToTheirOwnEntriesWithoutResolvedReferences()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "LocalizationProblemNavigation"), "Localization", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entries = workspace.InsertEntriesInto(group, 0, 2, BuiltinEntryIds.MoveLayer).ToArray();
            var authoring = scope.GetRequiredService<EntryFieldAuthoringService>();
            foreach (var entry in entries)
            {
                var parameters = new Dictionary<string, JsonElement>(entry.Source.Parameters)
                {
                    ["transform"] = JsonSerializer.SerializeToElement(new { X = 1f, Y = 2f, ScaleX = 1f, ScaleY = 1f, RotationDegrees = 0f })
                };
                authoring.SetParameters(group.Id, entry.Source, entry.Type, parameters);
            }
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id);
            var problems = scope.GetRequiredService<ProblemsPanelViewModel>(); await AwaitAutomaticProblems(problems);
            var warnings = problems.Problems.Where(problem => problem.Code == "localization.field.opaque" && problem.Target?.FieldId == "transform").ToArray();
            Assert.That(warnings.Select(problem => problem.Target!.EntryId), Is.EquivalentTo(entries.Select(entry => entry.StableId)),
                "Incomplete analysis still has an exact diagnostic owner; identical field paths in different entries must not merge.");
            var factory = scope.GetRequiredService<EditorDockFactory>(); var layout = factory.CreateLayout(); factory.InitLayout(layout);
            foreach (var warning in warnings)
            {
                Assert.That(warning.IsWarning, Is.True); Assert.That(warning.CanNavigate, Is.True);
                Assert.That(warning.Details, Is.EqualTo("Structured value has no traversable field schema."));
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    var text = scope.GetRequiredService<IEditorLocalizationService>(); text.ApplyLocale(locale);
                    Assert.That(problems.Problems.Single(problem => problem.Code == warning.Code && problem.Target == warning.Target).Message,
                        Is.EqualTo(text["Problem.Localization.Opaque"]));
                }
                page.SearchText = "hide all entries";
                problems.NavigateCommand.Execute(warning);
                Assert.That(scope.GetRequiredService<EntrySelectionContext>().Current?.EntryId, Is.EqualTo(warning.Target!.EntryId));
                Assert.That(entries.Single(entry => entry.StableId == warning.Target.EntryId).IsParametersExpanded, Is.True);
                var inspector = TimelineDockables(layout).Select(item => item.Context).OfType<InspectorHostViewModel>()
                    .Select(item => item.CurrentInspectorViewModel).OfType<EntryInspectorViewModel>().Single();
                Assert.That(inspector.Entry?.StableId, Is.EqualTo(warning.Target.EntryId));
                Assert.That(inspector.RequestedField, Is.EqualTo("transform"));
            }
            var owner = entries[1];
            var unknown = new Dictionary<string, JsonElement>(owner.Source.Parameters) { ["custom/field~name"] = JsonSerializer.SerializeToElement(1) };
            authoring.SetParameters(group.Id, owner.Source, owner.Type, unknown); await AwaitAutomaticProblems(problems);
            var unknownProblem = problems.Problems.Single(problem => problem.Code == "localization.field.unknown" && problem.Target?.EntryId == owner.StableId);
            Assert.That(unknownProblem.Target!.FieldId, Is.EqualTo("custom/field~name"), "JSON Pointer escaping must not leak into field identity.");
            Assert.That(unknownProblem.CanNavigate, Is.True);
            problems.NavigateCommand.Execute(unknownProblem);
            Assert.That(scope.GetRequiredService<EntrySelectionContext>().Current?.EntryId, Is.EqualTo(owner.StableId));
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task GroupProblemsAutomaticallyFollowEditsUndoAndRedoAndKeepPendingInputNeutral()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "AutoGroupProblems"), "Automatic", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>();
            var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
            using var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id);
            var problems = scope.GetRequiredService<ProblemsPanelViewModel>(); await AwaitAutomaticProblems(problems);
            var field = entry.DetailForm!.Fields.Single(); var input = scope.GetRequiredService<EntryEditSession>().Get(entry, field);
            input.Update(".22", true, null, false);
            Assert.That(problems.Problems.Single(problem => problem.Target?.EntryId == entry.StableId).Severity, Is.EqualTo("info"));
            Assert.That(entry.ProblemCount, Is.Zero);
            var before = problems.ValidationTask; input.Update(".3", true, null, false);
            Assert.That(problems.ValidationTask, Is.SameAs(before), "Typing does not schedule full project validation.");
            input.Clear();
            entry.Condition = "("; await AwaitAutomaticProblems(problems);
            var issue = problems.Problems.Single(problem => problem.Code == "condition.syntaxInvalid" && problem.Target?.EntryId == entry.StableId);
            Assert.That(issue.Message, Does.Contain("条件")); Assert.That(issue.Source, Does.Not.Contain(group.Id));
            Assert.That(issue.HasDetails, Is.True); Assert.That(issue.CanNavigate, Is.True); Assert.That(entry.HasProblemErrors, Is.True);
            var factory = scope.GetRequiredService<EditorDockFactory>(); var layout = factory.CreateLayout(); factory.InitLayout(layout);
            page.SearchText = "no match"; entry.IsParametersExpanded = false;
            problems.NavigateCommand.Execute(issue);
            Assert.That(scope.GetRequiredService<EntrySelectionContext>().Current?.EntryId, Is.EqualTo(entry.StableId));
            Assert.That(entry.IsParametersExpanded, Is.True);
            var host = TimelineDockables(layout).Select(item => item.Context).OfType<InspectorHostViewModel>().First(item => item.CurrentInspectorViewModel is EntryInspectorViewModel);
            var inspector = (EntryInspectorViewModel)host.CurrentInspectorViewModel!;
            Assert.That(inspector.RequestedField, Is.EqualTo("condition"));
            var view = new EntryInspectorView { DataContext = inspector }; var window = new Window { Content = view, Width = 700, Height = 600 }; window.Show(); TimelinePump(window);
            Assert.That(view.FindControl<ConditionEditorControl>("EntryConditionInput")!.GetVisualDescendants().OfType<TextBox>().Any(control => control.IsFocused), Is.True);
            window.Close();
            entry.Condition = "";
            Assert.That(problems.Problems.Any(problem => problem.Code == "condition.syntaxInvalid"), Is.False, "Old validation errors leave the active list immediately.");
            await AwaitAutomaticProblems(problems); Assert.That(entry.HasProblemErrors, Is.False);
            await scope.GetRequiredService<EditorHistories>().Content.UndoAsync(); await AwaitAutomaticProblems(problems);
            Assert.That(problems.Problems.Any(problem => problem.Code == "condition.syntaxInvalid"), Is.True);
            await scope.GetRequiredService<EditorHistories>().Content.RedoAsync(); await AwaitAutomaticProblems(problems);
            Assert.That(problems.Problems.Any(problem => problem.Code == "condition.syntaxInvalid"), Is.False);
            var authoring = scope.GetRequiredService<EntryFieldAuthoringService>();
            authoring.SetParameters(group.Id, entry.Source, entry.Type, new() { [field.Id] = JsonSerializer.SerializeToElement(-1f) });
            await AwaitAutomaticProblems(problems);
            var range = problems.Problems.Single(problem => problem.IsError && problem.Target?.EntryId == entry.StableId);
            Assert.That(range.Code, Is.EqualTo("value.range")); Assert.That(range.Message, Does.Contain("时长").And.Contain("0"));
            foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
            {
                var local = scope.GetRequiredService<IEditorLocalizationService>(); local.ApplyLocale(locale);
                Assert.That(problems.Problems.Single(problem => problem.IsError && problem.Target?.EntryId == entry.StableId).Message,
                    Is.EqualTo(local.Format("Problem.Value.Minimum", local["Entry.Parameter.duration"], local["Problem.Kind.Number"], "0", "+∞")));
            }
            var problemsView = new ProblemsPanelView { DataContext = problems };
            var problemsWindow = new Window { Content = problemsView, Width = 900, Height = 600 }; problemsWindow.Show(); TimelinePump(problemsWindow);
            var problemButton = problemsView.GetVisualDescendants().OfType<Button>().Single(button =>
                ReferenceEquals(button.Command, problems.NavigateCommand) && button.CommandParameter is AuthorProblem { Code: "value.range" });
            Assert.That(problemButton.Bounds.Width, Is.GreaterThan(600));
            var problemMessage = problemButton.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.That(problemMessage.TranslatePoint(default, problemsView)!.Value.X, Is.LessThan(90), "Actionable descriptions align with the row instead of centering in the panel.");
            CaptureNumericQa(problemsWindow, "group-problems.png"); problemsWindow.Close();
            authoring.SetParameters(group.Id, entry.Source, entry.Type, new() { [field.Id] = JsonSerializer.SerializeToElement(-2f) });
            authoring.SetParameters(group.Id, entry.Source, entry.Type, new() { [field.Id] = JsonSerializer.SerializeToElement(.5f) });
            await AwaitAutomaticProblems(problems);
            Assert.That(problems.Problems.Any(problem => problem.IsError && problem.Target?.EntryId == entry.StableId), Is.False);
            var nested = workspace.InsertEntriesInto(group, 1, 1, BuiltinEntryIds.MoveLayer).Single();
            var transformField = nested.DetailForm!.Fields.Single(field => field.Id == "transform");
            var transformDraft = scope.GetRequiredService<EntryEditSession>().Get(nested, transformField);
            var scaleDraft = transformDraft.Child!(new FieldDescriptor("ScaleY", new(typeof(float)), labelKey: "Form.Transform.ScaleY"), "ScaleY");
            scaleDraft.Update("invalid", true, new ValueIssue("value.type", "invalid"), false);
            var scaleProblem = problems.Problems.Single(problem => problem.Target?.EntryId == nested.StableId && problem.Code == "value.type");
            Assert.That(scaleProblem.Message, Does.Contain("缩放 Y")); Assert.That(scaleProblem.Source, Does.EndWith("缩放 Y"));
            problems.NavigateCommand.Execute(scaleProblem);
            var nestedInspector = TimelineDockables(layout).Select(item => item.Context).OfType<InspectorHostViewModel>()
                .Select(item => item.CurrentInspectorViewModel).OfType<EntryInspectorViewModel>().First(item => item.Entry?.StableId == nested.StableId);
            var nestedView = new EntryInspectorView { DataContext = nestedInspector };
            var nestedWindow = new Window { Content = nestedView, Width = 800, Height = 700 }; nestedWindow.Show(); TimelinePump(nestedWindow);
            Assert.That(NumericControlTestSupport.NamedInputs(nestedView, "TransformScaleY").Single().IsFocused, Is.True, "Nested problems focus the failing component.");
            nestedWindow.Close(); scaleDraft.Clear();
            scope.GetRequiredService<GroupEntryAuthoringService>().Delete(group.Id, [entry.StableId]);
            Assert.That(scope.GetRequiredService<AuthorNavigationService>().CanNavigate(issue.Target!), Is.False);
            problems.NavigateCommand.Execute(issue);
            Assert.That(problems.Problems.Any(problem => problem.Message == scope.GetRequiredService<IEditorLocalizationService>()["Problem.Unavailable"]), Is.True);
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task GroupNumericUpDownPreservesDraftsSupportsRealSpinnerAndSavesShorthandAfterViewsClose()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme(); using var services = Services(); var projects = services.GetRequiredService<IProjectService>();
            var project = await projects.CreateAsync(Path.Combine(_root, "GroupNumbers"), "Numbers", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            var scope = project.Services; var workspace = scope.GetRequiredService<EditorWorkspaceViewModel>(); var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
            var entry = workspace.InsertEntriesInto(group, 0, 1, BuiltinEntryIds.Wait).Single();
            var page = ActivatorUtilities.CreateInstance<GroupEditorPanelViewModel>(scope, group.Id);
            var form = new DtoEditorHost { Item = entry.DetailForm!.Item, Registry = page.FieldEditors };
            var window = new Window { Content = form, Width = 700, Height = 500 };
            window.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://GalNet.Editor/")) { Source = new Uri("avares://GalNet.Editor/Controls/AuthorFormStyles.axaml") });
            window.Show(); TimelinePump(window);
            var schema = form.GetVisualDescendants().OfType<SchemaForm>().Single(); var context = schema.Contexts.Single();
            Assert.That(scope.GetRequiredService<EntryEditSession>().PendingCount, Is.Zero, "Mounting a spinner must never create input drafts.");
            var up = schema.Editors.Single().Control as NumericUpDown; Assert.That(up, Is.Not.Null);
            var text = up!.GetVisualDescendants().OfType<TextBox>().Single(); text.Focus(); text.Text = ".22";
            Assert.That(context.Text, Is.EqualTo(".22")); Assert.That(context.Issue, Is.Null); Assert.That(context.HasDraft, Is.True);
            PressGroupKey(text, Key.Enter); TimelinePump(window);
            Assert.That(entry.Source.Parameters[context.Field.Id].GetSingle(), Is.EqualTo(.22f)); Assert.That(text.Text, Is.EqualTo("0.22"));
            CaptureNumericQa(window, "group-numeric.png");
            text.Text = "-."; Assert.That(context.Issue, Is.Null); PressGroupKey(text, Key.Enter);
            Assert.That(context.Issue, Is.Not.Null); Assert.That(entry.Source.Parameters[context.Field.Id].GetSingle(), Is.EqualTo(.22f));
            PressGroupKey(text, Key.Escape); Assert.That(text.Text, Is.EqualTo("0.22"));
            var history = scope.GetRequiredService<EditorHistories>().Content; history.Clear();
            var spinner = up.GetVisualDescendants().OfType<ButtonSpinner>().Single();
            var increase = spinner.GetVisualDescendants().OfType<RepeatButton>().First(button => button.Name == "PART_IncreaseButton");
            Click(window, increase); TimelinePump(window);
            Assert.That(entry.Source.Parameters[context.Field.Id].GetSingle(), Is.EqualTo(.32f).Within(.000001));
            Assert.That(history.CanUndo, Is.True); await history.UndoAsync(); Assert.That(entry.Source.Parameters[context.Field.Id].GetSingle(), Is.EqualTo(.22f));
            PressGroupKey(text, Key.Up); Assert.That(entry.Source.Parameters[context.Field.Id].GetSingle(), Is.EqualTo(.32f).Within(.000001));
            await history.UndoAsync();
            text.Text = ".3";
            window.Close(); page.Dispose(); await workspace.SaveAsync();
            Assert.That(entry.Source.Parameters[context.Field.Id].GetSingle(), Is.EqualTo(.3f));
            var root = project.RootPath; var stableId = entry.StableId; var groupId = group.Id;
            await projects.CloseAsync(); project = await projects.OpenAsync(root);
            var reopened = project.Services.GetRequiredService<EditorGroupProjectionStore>().Get(groupId).Entries.Single(entry => entry.StableId == stableId);
            Assert.That(reopened.Source.Parameters[context.Field.Id].GetSingle(), Is.EqualTo(.3f));
            await projects.CloseAsync(); return true;
        }, CancellationToken.None);
    }

    private static void CaptureNumericQa(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is not { Length: > 0 } artifacts) return;
        foreach (var animated in window.GetVisualDescendants().OfType<Avalonia.Animation.Animatable>()) animated.Transitions = null;
        TimelinePump(window); using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, name));
    }
}
