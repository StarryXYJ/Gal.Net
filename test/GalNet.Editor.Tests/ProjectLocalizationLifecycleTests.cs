using GalNet.Contracts.Builtins;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.History;
using GalNet.Editor.Models.Graph;
using GalNet.Editor.Services;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task OpenFoundationViewsHistoryErrorsAndDialogRefreshAcrossFourLanguagesWithoutChangingContent()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUi = CultureInfo.CurrentUICulture;
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            var localization = new EditorLocalizationService();
            using var services = Services(localization: localization);
            var projects = services.GetRequiredService<IProjectService>();
            var audio = GalNet.Contracts.Builtins.BuiltinAudioDefaults.Create();
            var project = await projects.CreateAsync(Path.Combine(_root, "Localized"), "Authored 名称",
                new DefaultProjectOptions { AudioGroups = audio.Groups.ToList(), AudioTracks = audio.Tracks.ToList() }.ToConfiguration(), Contracts());
            var workspace = project.Services.GetRequiredService<EditorWorkspaceViewModel>();
            var panel = project.Services.GetRequiredService<ProjectSettingsPanelViewModel>();
            var page = project.Services.GetRequiredService<EditorPageViewModel>();
            var editorSettings = project.Services.GetRequiredService<EditorSettingsPanelViewModel>();
            var settingsView = new ProjectSettingsPanelView { DataContext = panel };
            var graphView = new NodeGraphPanelView { DataContext = workspace };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
            Grid.SetColumn(graphView, 1); grid.Children.Add(settingsView); grid.Children.Add(graphView);
            var window = new Window { Width = 1440, Height = 800, Content = grid };
            var preferenceView = new EditorSettingsPanelView { DataContext = editorSettings };
            var preferences = new Window { Width = 680, Height = 760, Content = preferenceView };
            var inspectorModel = project.Services.GetRequiredService<GalNet.Editor.Inspector.ViewModels.NodeInspectorControlViewModel>();
            var inspectorView = new GalNet.Editor.Inspector.Views.NodeInspectorControl { DataContext = inspectorModel };
            var inspector = new Window { Width = 480, Height = 700, Content = inspectorView };
            var dialog = ProjectClosePrompt.CreateDialog(project.Name, localization);
            window.Show(); preferences.Show(window); inspector.Show(window); dialog.Show(window);
            try
            {
                window.UpdateLayout(); preferences.UpdateLayout(); inspector.UpdateLayout(); dialog.UpdateLayout();
                var group = workspace.Nodes.Single(node => node.NodeKind == GraphNodeKind.LinearGroup);
                group.Name = "Keep author name";
                workspace.SelectNode(group);
                panel.ProjectName = "";
                await panel.SaveSettingsCommand.ExecuteAsync(null);
                var content = project.Services.GetRequiredService<IProjectContentSession>();
                content.ReportError(new EditorOperationException("project.externalSettings", "Raw diagnostic"));
                var snapshot = JsonSerializer.Serialize(content.CaptureWorking());
                var beforeDirty = content.IsDirty;
                var history = project.Services.GetRequiredService<EditorHistories>().Content;
                var undo = history.UndoDescription;
                var shortcut = editorSettings.Shortcuts.First();
                var shortcutId = shortcut.CommandId;
                shortcut.GestureText = "Not a shortcut!";
                var language = preferenceView.GetVisualDescendants().OfType<ComboBox>().Single(box => ReferenceEquals(box.ItemsSource, editorSettings.AvailableCultures));
                foreach (var locale in new[] { "zh-CN", "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    language.SelectedItem = editorSettings.AvailableCultures.Single(culture => culture.Name == locale);
                    window.UpdateLayout(); preferences.UpdateLayout(); inspector.UpdateLayout(); dialog.UpdateLayout();
                    Assert.That(localization.CurrentCulture.Name, Is.EqualTo(locale));
                    Assert.That(services.GetRequiredService<IEditorSettingsService>().GetSettings().UiLocale.Code, Is.EqualTo(locale));
                    Assert.That(dialog.Title, Is.EqualTo(localization["Close.Title"]));
                    var save = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "CloseSave");
                    Assert.That(save.Content, Is.EqualTo(localization["Close.Save"]));
                    Assert.That(save.Bounds.Width, Is.GreaterThan(0));
                    Assert.That(settingsView.FindControl<Button>("ApplySettingsButton")!.Content, Is.EqualTo(localization["Settings.Project.Apply"]));
                    Assert.That(graphView.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == localization["Graph.Kind.LinearGroup"]), Is.True);
                    Assert.That(inspectorView.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == localization["Graph.Kind.LinearGroup"]), Is.True);
                    Assert.That(page.MenuItems.SelectMany(menu => menu.Children ?? []).Any(item => item.Header?.Contains(localization["History.Rename"], StringComparison.Ordinal) == true), Is.True);
                    Assert.That(panel.IsDirtyText, Does.Contain(localization["Error.project.nameRequired"]));
                    Assert.That(page.StatusText, Does.Contain(localization["Error.project.externalSettings"]));
                    Assert.That(panel.ProjectName, Is.Empty, "Changing language must not discard a draft.");
                    Assert.That(editorSettings.Shortcuts.Single(item => item.CommandId == shortcutId).GestureText, Is.EqualTo("Not a shortcut!"));
                    Assert.That(editorSettings.Shortcuts.Single(item => item.CommandId == shortcutId).ValidationMessage, Is.EqualTo(localization["Settings.ShortcutInvalid"]));
                    Assert.That(JsonSerializer.Serialize(content.CaptureWorking()), Is.EqualTo(snapshot));
                    Assert.That(content.IsDirty, Is.EqualTo(beforeDirty));
                    Assert.That(history.UndoDescription, Is.EqualTo(undo));
                    Assert.That(project.Configuration.ReadDefaultOptions().TargetLocale.Code, Is.EqualTo("zh-CN"));
                    if (Environment.GetEnvironmentVariable("GALNET_EDITOR_TEST_ARTIFACTS") is { Length: > 0 } artifacts)
                    {
                        using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(artifacts, "p2i-workspace-" + locale + ".png"));
                        using var closeFrame = dialog.CaptureRenderedFrame(); closeFrame!.Save(Path.Combine(artifacts, "p2i-close-" + locale + ".png"));
                    }
                }
                panel.CancelDraftCommand.Execute(null);
                var gains = new[] { 0.625m, 0.375m, 0.125m, 0.875m };
                var gainIndex = 0;
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    language.SelectedItem = editorSettings.AvailableCultures.Single(culture => culture.Name == locale);
                    window.UpdateLayout();
                    var gain = settingsView.GetVisualDescendants().OfType<TextBox>().Single(input => input.DataContext is GalNet.Game.Controls.Forms.FieldEditorContext context && context.Field.Id == "audioTracks");
                    var expectedGain = gains[gainIndex++];
                    var tracks = project.Configuration.ReadDefaultOptions().AudioTracks;
                    tracks[0] = tracks[0] with { DefaultGain = (float)expectedGain };
                    gain.Text = JsonSerializer.Serialize(tracks, new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
                    await panel.SaveSettingsCommand.ExecuteAsync(null);
                    await workspace.SaveAsync();
                    using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(project.RootPath, "settings.json")));
                    Assert.That(json.RootElement.GetProperty("sections").GetProperty("galnet.project").GetProperty("data").GetProperty("audioTracks")[0].GetProperty("defaultGain").GetDouble(), Is.EqualTo((double)expectedGain).Within(0.00001));
                }
            }
            finally
            {
                dialog.Close(); preferences.Close(); inspector.Close(); window.Close(); inspectorModel.Dispose(); editorSettings.Dispose(); panel.Dispose();
                await projects.CloseAsync();
                CultureInfo.CurrentCulture = originalCulture; CultureInfo.CurrentUICulture = originalUi;
            }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task NewProjectErrorsAndDisposedViewsKeepCorrectLocalizationLifetime()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            Application.Current!.Styles.Add(new Semi.Avalonia.SemiTheme());
            var localization = new EditorLocalizationService();
            using var services = Services(localization: localization);
            var panel = services.GetRequiredService<NewProjectPanelViewModel>();
            var view = new NewProjectPanelView { DataContext = panel };
            var window = new Window { Width = 700, Height = 700, Content = view };
            window.Show();
            try
            {
                view.FindControl<TextBox>("ProjectNameInput")!.Text = "";
                await ((IAsyncRelayCommand)view.FindControl<Button>("CreateProjectButton")!.Command!).ExecuteAsync(null);
                foreach (var locale in new[] { "en-US", "ja-JP", "ko-KR", "zh-CN" })
                {
                    localization.ApplyLocale(locale); window.UpdateLayout();
                    Assert.That(panel.Message, Is.EqualTo(localization["Error.project.nameRequired"]));
                    Assert.That(view.FindControl<Button>("CreateProjectButton")!.Content, Is.EqualTo(localization["NewProject.Create"]));
                }
                panel.Dispose();
                var changed = 0;
                panel.PropertyChanged += (_, _) => changed++;
                localization.ApplyLocale("en-US");
                Assert.That(changed, Is.Zero);
                for (var index = 0; index < 4; index++)
                {
                    var dialog = ProjectClosePrompt.CreateDialog("User/{0}", localization);
                    dialog.Show(window); dialog.UpdateLayout(); dialog.Close();
                    var title = dialog.Title;
                    localization.ApplyLocale(index % 2 == 0 ? "ja-JP" : "en-US");
                    Assert.That(dialog.Title, Is.EqualTo(title), "Closed dialogs must release localization bindings.");
                }
            }
            finally { window.Close(); panel.Dispose(); }
            return true;
        }, CancellationToken.None);
    }
}
