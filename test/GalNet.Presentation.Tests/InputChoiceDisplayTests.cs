using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Input;
using GalNet.Avalonia.GameView.MarkupExtensions;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes;
using GalNet.Avalonia.Themes.Common;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class InputChoiceDisplayTests
{
    [TestCase("Classic", PlayerInputDevice.Touch)]
    [TestCase("Minimal", PlayerInputDevice.Touch)]
    [TestCase("Paper", PlayerInputDevice.Touch)]
    [TestCase("Classic", PlayerInputDevice.Gamepad)]
    [TestCase("Minimal", PlayerInputDevice.Gamepad)]
    [TestCase("Paper", PlayerInputDevice.Gamepad)]
    public Task SelectedInputChoiceSurvivesPopupClosureCultureChangesAndEditorRecreation(string layout, PlayerInputDevice device) =>
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly).Dispatch(async () =>
        {
            using var localization = AvaloniaPlayerLocalization.FromEmbeddedResources([]);
            var previous = PlayerTextExtension.Resolve;
            PlayerTextExtension.Resolve = localization.GetString;
            using var service = new AvaloniaPlayerSettingsService(localization, new GamePageViewModel(null!));
            service.Input.GamepadSupported = true;
            service.Input.SelectedDevice = device;
            var operation = service.Categories.Single(category => category.Id == "operation");
            var view = new SettingsCategories { Categories = [operation], Editors = service.Editors };
            var root = new Border { Classes = { "galnet-player" }, Child = view };
            root.Styles.Add(layout switch
            {
                "Minimal" => new MinimalTheme().CreateStyles(),
                "Paper" => new PaperTheme().CreateStyles(),
                _ => new ClassicTheme().CreateStyles()
            });
            var window = new Window { Width = 1000, Height = 800, Content = root };
            window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
            window.Show();
            try
            {
                window.UpdateLayout();
                var panel = view.GetVisualDescendants().OfType<InputBindingsPanel>().Single();
                var combo = panel.GetVisualDescendants().OfType<ComboBox>().First();
                var key = device == PlayerInputDevice.Touch ? "Input.Touch.DoubleTap" : "Input.EastConfirms";
                var selectedIndex = device == PlayerInputDevice.Touch ? 2 : 1;
                combo.IsDropDownOpen = true;
                window.UpdateLayout();
                combo.SelectedIndex = selectedIndex;
                await service.FlushAsync();
                window.UpdateLayout();
                combo.IsDropDownOpen = false;
                window.UpdateLayout();
                Assert.That(SelectedText(combo)?.Text, Is.EqualTo(localization[key]),
                    "The closed selection must not depend on a detached popup item's text binding.");
                Assert.That(combo.SelectedIndex, Is.EqualTo(selectedIndex));
                if (device == PlayerInputDevice.Touch)
                    Assert.That(service.Input.Resolve(device, "DoubleTap"), Is.EqualTo("advance"));
                else
                    Assert.That(service.Input.EastConfirms, Is.True);

                foreach (var culture in new[] { "zh-CN", "ja-JP", "en-US" })
                {
                    localization.ApplyCulture(culture);
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    Assert.That(SelectedText(combo)?.Text, Is.EqualTo(localization[key]), culture);
                }

                combo.IsDropDownOpen = true;
                window.UpdateLayout();
                combo.IsDropDownOpen = false;
                window.UpdateLayout();
                Assert.That(SelectedText(combo)?.Text, Is.EqualTo(localization[key]));
                root.Child = null;
                root.Child = view;
                window.UpdateLayout();
                panel = view.GetVisualDescendants().OfType<InputBindingsPanel>().Single();
                combo = panel.GetVisualDescendants().OfType<ComboBox>().First();
                Assert.That(combo.SelectedIndex, Is.EqualTo(selectedIndex));
                Assert.That(SelectedText(combo)?.Text, Is.EqualTo(localization[key]));
                Assert.That(combo.Items.Cast<object>().Any(item => item is Control), Is.False,
                    "Popup and selection displays must be created from data, not share a visual.");
                return true;
            }
            finally
            {
                window.Close();
                PlayerTextExtension.Resolve = previous;
            }
        }, TestContext.CurrentContext.CancellationToken);

    private static TextBlock? SelectedText(ComboBox combo) =>
        combo.SelectionBoxItem is Rectangle { Fill: VisualBrush { Visual: TextBlock text } }
            ? text
            : combo.GetVisualDescendants().OfType<PlayerTextBlock>().FirstOrDefault();
}
