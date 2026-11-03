using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Themes;
using GalNet.Editor.ViewModels;
using GalNet.Editor.Views;
using GalNet.Game.Controls.Forms;
using GalNet.Game.Controls.Timeline;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task EditorFontSettingUpdatesMountedControlsAndRestoresFromSettings()
    {
        var ui = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EditorFormTestApplication).Assembly);
        await ui.Dispatch<bool>(async () =>
        {
            PrepareGroupTheme();
            var app = Application.Current!;
            EditorTypography.Apply(app.Resources, EditorTypography.DefaultSize);
            using var services = Services();
            var project = await services.GetRequiredService<IProjectService>().CreateAsync(Path.Combine(_root, "EditorTypography"), "Typography", new(), Path.Combine(AppContext.BaseDirectory, "DefaultContracts"));
            using var settings = project.Services.GetRequiredService<EditorSettingsPanelViewModel>();
            var body = new TextBlock { Text = "Body" };
            var heading = new TextBlock { Text = "Heading" };
            heading[!TextBlock.FontSizeProperty] = new DynamicResourceExtension("Editor.Font.Heading");
            var input = new TextBox { Text = "Input" };
            var view = new EditorSettingsPanelView { DataContext = settings };
            var timeline = new TimelineCanvas { Height = 30 };
            var picker = new ResourcePickerPanel { Height = 160 };
            picker.SetItems([new ResourcePickerItem("one", "Label", "Detail", Badge: "Badge")]);
            var window = new Window { Content = new StackPanel { Children = { body, heading, input, timeline, picker, view } }, Width = 800, Height = 1000 };
            window.Styles.Add(new StyleInclude(new Uri("avares://GalNet.Editor/")) { Source = new Uri("avares://GalNet.Editor/Themes/EditorControlStyles.axaml") });
            window.Show(); TimelinePump(window);
            try
            {
                Assert.That(settings.FontSize, Is.EqualTo(12));
                Assert.That(body.FontSize, Is.EqualTo(12));
                Assert.That(input.FontSize, Is.EqualTo(12));
                Assert.That(heading.FontSize, Is.EqualTo(14));
                Assert.That(timeline.LabelFontSize, Is.EqualTo(10.5));
                Assert.That(picker.BadgeFontSize, Is.EqualTo(10));
                var setting = view.FindControl<NumericUpDown>("EditorFontSize")!;
                setting.Value = 16; TimelinePump(window);
                Assert.That(settings.FontSize, Is.EqualTo(16));
                Assert.That(services.GetRequiredService<IEditorSettingsService>().GetSettings().FontSize, Is.EqualTo(16));
                Assert.That(body.FontSize, Is.EqualTo(16));
                Assert.That(input.FontSize, Is.EqualTo(16));
                Assert.That(heading.FontSize, Is.EqualTo(14d * 16 / 12).Within(.001));
                Assert.That(timeline.LabelFontSize, Is.EqualTo(14));
                Assert.That(picker.BadgeFontSize, Is.EqualTo(10d * 16 / 12).Within(.001));
                using var reopened = project.Services.GetRequiredService<EditorSettingsPanelViewModel>();
                Assert.That(reopened.FontSize, Is.EqualTo(16));
                settings.FontSize = 100;
                Assert.That(settings.FontSize, Is.EqualTo(18));
                settings.FontSize = double.NaN;
                Assert.That(settings.FontSize, Is.EqualTo(12));
            }
            finally { window.Close(); EditorTypography.Apply(app.Resources, EditorTypography.DefaultSize); }
            await Task.CompletedTask; return true;
        }, CancellationToken.None);
    }
}
