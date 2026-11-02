using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameHost.Settings;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.Controls;
using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Input;
using GalNet.Avalonia.GameView.Page;
using GalNet.Sample.CrossPlatform;
using Microsoft.Extensions.DependencyInjection;

namespace GalNet.IntegrationTests;

[NonParallelizable]
public sealed class PlayerLayoutPolishTests
{
    [Test]
    public void LowDpiMobileControlsGetOnlyAModestScaleIncrease()
    {
        var viewport = new Size(800, 360);
        var normal = PlayerLayout.UiScale(viewport, mobile: true, renderScaling: 3);
        var lowDpi = PlayerLayout.UiScale(viewport, mobile: true, renderScaling: 1);
        Assert.That(lowDpi / normal, Is.InRange(1.05, 1.1));
        Assert.That(PlayerLayout.UiScale(viewport, mobile: false, renderScaling: 1), Is.LessThan(normal));
    }

    [Test]
    public Task HomeRotationOnlyChangesHorizontalAlignmentAndKeepsItsButtons() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await CreateHost();
        var window = new Window { Width = 1280, Height = 720, Content = host.View };
        window.Show();
        try
        {
            window.UpdateLayout();
            var page = window.GetVisualDescendants().OfType<GalNet.Sample.CrossPlatform.Views.TitlePage>().Single();
            var button = page.FindControl<Button>("NewGameButton")!;
            var originalSize = button.Bounds.Size;
            var fontSize = button.FontSize;
            host.View.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
            host.View.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
            host.View.Width = 360;
            host.View.Height = 800;
            window.Width = 360;
            window.Height = 800;
            window.UpdateLayout();
            Assert.That(page.FindControl<Button>("NewGameButton"), Is.SameAs(button));
            Assert.That(button.Bounds.Size, Is.EqualTo(originalSize));
            Assert.That(button.FontSize, Is.EqualTo(fontSize));
            Assert.That(page.FindControl<TextBlock>("GameTitleText")!.TextAlignment, Is.EqualTo(global::Avalonia.Media.TextAlignment.Center));
            var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, 0), host.View)!.Value.X;
            Assert.That(center, Is.EqualTo(180).Within(1));
            window.Width = 1280;
            window.Height = 720;
            host.View.Width = 1280;
            host.View.Height = 720;
            window.UpdateLayout();
            Assert.That(page.FindControl<TextBlock>("GameTitleText")!.TextAlignment, Is.EqualTo(global::Avalonia.Media.TextAlignment.Left));
            Assert.That(button.TranslatePoint(new Point(button.Bounds.Width / 2, 0), host.View)!.Value.X, Is.LessThan(640));
        }
        finally { window.Close(); }
    });

    [TestCase(320, 480)]
    [TestCase(360, 800)]
    [TestCase(640, 480)]
    [TestCase(800, 360)]
    [TestCase(1280, 720)]
    [TestCase(1920, 1080)]
    [TestCase(3840, 2160)]
    public Task MainMenuAndSettingsUseBoundedContainers(int width, int height) => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await CreateHost();
        var window = new Window { Width = width, Height = height, Content = host.View };
        window.Show();
        try
        {
            window.UpdateLayout();
            var title = window.GetVisualDescendants().OfType<GalNet.Sample.CrossPlatform.Views.TitlePage>().Single();
            var group = title.FindControl<Grid>("TitleLayout")!;
            Assert.That(title.FindControl<TextBlock>("GameTitleText")!.TextLayout.TextLines.Any(line => line.HasCollapsed), Is.False);
            var menu = title.FindControl<StackPanel>("TitleButtons")!;
            var buttons = menu.Children.OfType<Button>().Where(button => button.IsVisible).ToArray();
            Assert.That(group.Bounds.Width, Is.LessThanOrEqualTo(1600));
            Assert.That(buttons.Select(button => button.Bounds.Width).Distinct().Count(), Is.EqualTo(1));
            foreach (var button in buttons)
            {
                var point = button.TranslatePoint(default, host.View)!.Value;
                var end = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), host.View)!.Value;
                Assert.That(button.Bounds.Width, Is.EqualTo(380).Within(1));
                Assert.That(button.Bounds.Height, Is.InRange(46, 60));
                Assert.That(point.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(end.X, Is.LessThanOrEqualTo(width));
                Assert.That(end.Y, Is.LessThanOrEqualTo(height));
            }
            Capture(window, $"title-{width}x{height}");

            host.Services.GetRequiredService<IGameNavigationService>().ResetTo<GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel>(NavigationTransition.None);
            window.UpdateLayout();
            var page = window.GetVisualDescendants().OfType<GalNet.Sample.CrossPlatform.Views.SettingsPage>().Single();
            Assert.That(page.GetVisualDescendants().OfType<Border>().Any(border => border.Classes.Contains("player-settings-card")), Is.False);
            var scroll = page.GetVisualDescendants().OfType<ScrollViewer>().Single(item => item.Name == "CategoryContentScroll");
            var tabs = page.GetVisualDescendants().OfType<TabControl>().Single(item => item.Name == "CategoryTabs");
            Assert.That(scroll.Bounds.Height, Is.GreaterThan(80));
            var tabsTop = tabs.TranslatePoint(default, page)!.Value.Y;
            scroll.Offset = new Vector(0, 100);
            window.UpdateLayout();
            Assert.That(tabs.TranslatePoint(default, page)!.Value.Y, Is.EqualTo(tabsTop));
            foreach (var row in page.GetVisualDescendants().OfType<SettingRow>())
                Assert.That(row.Bounds.Width, Is.LessThanOrEqualTo(1000));
            foreach (var input in page.GetVisualDescendants().OfType<TextBox>().Where(input => input.Classes.Contains("player-number")))
                Assert.That(input.Bounds.Width, Is.LessThanOrEqualTo(160));
            foreach (var slider in page.GetVisualDescendants().OfType<Slider>())
                Assert.That(slider.Bounds.Width, Is.InRange(120, 520), "A bounded slider still needs a usable track.");
            Capture(window, $"settings-{width}x{height}");
        }
        finally { window.Close(); }
    });

    [TestCase(360, 800)]
    [TestCase(800, 360)]
    [TestCase(1280, 720)]
    public Task ScaledPagesAcceptMouseNavigationAndCategorySelection(int width, int height) => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await CreateHost();
        var navigation = host.Services.GetRequiredService<IGameNavigationService>();
        var title = host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.ViewModels.TitlePageViewModel>();
        var window = new Window { Width = width, Height = height, Content = host.View };
        window.Show();
        void Click(Control control)
        {
            var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.UpdateLayout();
        }
        try
        {
            window.UpdateLayout();
            Click(window.GetVisualDescendants().OfType<Button>().Single(button => ReferenceEquals(button.Command, title.OpenAboutCommand)));
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<GalNet.Sample.CrossPlatform.ViewModels.AboutPageViewModel>());
            await navigation.NavigateAsync(host.Services.GetRequiredService<GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel>(), NavigationTransition.None);
            window.UpdateLayout();
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(item => item.Name == "CategoryTabs");
            Click((TabItem)tabs.ContainerFromIndex(1)!);
            Assert.That(tabs.SelectedIndex, Is.EqualTo(1));
            Click(window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PageBackButton"));
            Assert.That(navigation.CurrentViewModel, Is.TypeOf<GalNet.Sample.CrossPlatform.ViewModels.AboutPageViewModel>());
        }
        finally { window.Close(); }
    });

    [Test]
    public Task CategoryNavigationRestoresScrollAndResizePreservesTheEditor() => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await CreateHost();
        host.Services.GetRequiredService<IGameNavigationService>().ResetTo<GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel>(NavigationTransition.None);
        var window = new Window { Width = 800, Height = 360, Content = host.View };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var categories = window.GetVisualDescendants().OfType<SettingsCategories>().Single();
            var tabs = categories.FindControl<TabControl>("CategoryTabs")!;
            ScrollViewer Content() => categories.GetVisualDescendants().OfType<ScrollViewer>().Single(item => item.Name == "CategoryContentScroll");
            var content = Content();
            content.Offset = new Vector(0, 120);
            window.UpdateLayout();
            var offset = content.Offset.Y;
            Assert.That(offset, Is.GreaterThan(0));
            tabs.SelectedIndex = tabs.ItemCount - 1;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            tabs.SelectedIndex = 0;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.That(Content().Offset.Y, Is.EqualTo(offset).Within(1));
            TestContext.WriteLine($"Category content ScrollViewer reused: {ReferenceEquals(content, Content())}");

            Content().Offset = default;
            var row = categories.GetVisualDescendants().OfType<SettingRow>().First(item => item.Children[0].IsVisible);
            var editor = row.Children[1];
            var font = (double)host.Services.GetRequiredService<GameShell>().Resources["GalNet.Theme.BodyFontSize"]!;
            foreach (var size in new[] { new Size(360, 800), new Size(640, 480), new Size(1280, 720) })
            {
                host.View.Width = size.Width;
                host.View.Height = size.Height;
                window.Width = size.Width;
                window.Height = size.Height;
                window.UpdateLayout();
                Assert.That(row.Children[1], Is.SameAs(editor));
                Assert.That(host.Services.GetRequiredService<GameShell>().Resources["GalNet.Theme.BodyFontSize"], Is.EqualTo(font));
            }
            var firstTab = (TabItem)tabs.ContainerFromIndex(0)!;
            firstTab.Focus();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.That(tabs.SelectedIndex, Is.EqualTo(1));
        }
        finally { window.Close(); }
    });

    [TestCase("en-US", 1280, 720, FontSizePreset.Smaller)]
    [TestCase("zh-CN", 360, 800, FontSizePreset.Large)]
    [TestCase("en-US", 800, 360, FontSizePreset.Larger)]
    [TestCase("zh-CN", 1920, 1080, FontSizePreset.Large)]
    public Task PageHeadersStayAlignedAndNavigationTextFollowsTheFontChoice(string culture, int width, int height, FontSizePreset preset)
        => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await CreateHost();
        host.Services.GetRequiredService<AvaloniaPlayerLocalization>().ApplyCulture(culture);
        var settings = host.Services.GetRequiredService<AvaloniaPlayerSettingsService>();
        Assert.That(await settings.Categories.SelectMany(category => category.Items).OfType<ChoiceSetting<FontSizePreset>>().Single().TrySetValueAsync(preset), Is.True);
        var navigation = host.Services.GetRequiredService<IGameNavigationService>();
        var sizes = PlayerTypography.Presets[preset];
        var window = new Window { Width = width, Height = height, Content = host.View };
        window.Show();
        try
        {
            Point? headerPosition = null;
            Point? backPosition = null;
            foreach (var type in new[] { typeof(GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel), typeof(GalNet.Sample.CrossPlatform.ViewModels.AboutPageViewModel), typeof(GalNet.Sample.CrossPlatform.ViewModels.GalleryPageViewModel), typeof(GalNet.Sample.CrossPlatform.ViewModels.SaveSlotsPageViewModel) })
            {
                await navigation.NavigateAsync((PageViewModelBase)host.Services.GetRequiredService(type), NavigationTransition.None);
                window.UpdateLayout();
                var header = window.GetVisualDescendants().OfType<Grid>().Single(control => control.Name == "PageHeader");
                var back = window.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "PageBackButton");
                var heading = header.GetVisualDescendants().OfType<TextBlock>().First();
                var position = header.TranslatePoint(default, host.View)!.Value;
                var backPoint = back.TranslatePoint(default, host.View)!.Value;
                if (type != typeof(GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel))
                {
                    headerPosition ??= position;
                    backPosition ??= backPoint;
                    Assert.That(position.X, Is.EqualTo(headerPosition.Value.X).Within(.1));
                    Assert.That(position.Y, Is.EqualTo(headerPosition.Value.Y).Within(.1));
                    Assert.That(backPoint.X, Is.EqualTo(backPosition.Value.X).Within(.1));
                    Assert.That(backPoint.Y, Is.EqualTo(backPosition.Value.Y).Within(.1));
                }
                Assert.That(heading.FontSize, Is.EqualTo(sizes.Heading));
                Assert.That(back.FontSize, Is.EqualTo(sizes.Action));
                if (type == typeof(GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel))
                {
                    var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(control => control.Name == "CategoryTabs");
                    var categories = window.GetVisualDescendants().OfType<SettingsCategories>().Single();
                    Assert.That(categories.TranslatePoint(default, host.View)!.Value.X, Is.EqualTo(position.X).Within(.1));
                    Assert.That(categories.Bounds.Width, Is.EqualTo(header.Bounds.Width).Within(.1));
                    Assert.That(header.Bounds.Width, Is.LessThanOrEqualTo(800));
                    Assert.That(header.TranslatePoint(new Point(header.Bounds.Width / 2, 0), host.View)!.Value.X,
                        Is.EqualTo(width / 2d).Within(1), "The heading and form share one centered frame, allowing layout pixel rounding.");
                    var tab = (TabItem)tabs.ContainerFromIndex(0)!;
                    Assert.That(tab.GetVisualDescendants().OfType<TextBlock>().First().FontSize, Is.EqualTo(sizes.Heading * 22 / 34));
                    Assert.That(tab.GetVisualDescendants().OfType<TextBlock>().First().FontWeight, Is.EqualTo(global::Avalonia.Media.FontWeight.SemiBold));
                    Assert.That(tab.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_SelectedPipe").IsVisible, Is.False);
                    Assert.That(tab.GetVisualDescendants().OfType<TextBlock>().First().TranslatePoint(default, host.View)!.Value.X,
                        Is.EqualTo(heading.TranslatePoint(default, host.View)!.Value.X).Within(1),
                        "The first category label must not retain TabItem's leading padding.");
                    foreach (var row in tabs.GetVisualDescendants().OfType<SettingRow>().Where(row => row.IsEffectivelyVisible))
                    {
                        var editorLeft = row.Children[1].TranslatePoint(default, row)!.Value.X;
                        foreach (var input in row.Children[1].GetVisualDescendants().OfType<Control>()
                            .Where(control => control is ComboBox or Slider || control is TextBox && control.Classes.Contains("player-number")))
                            Assert.That(input.TranslatePoint(default, row)!.Value.X, Is.EqualTo(editorLeft).Within(.1),
                                "Bounded editors must start at the same column, including after narrow-screen reflow.");
                    }
                }
                Capture(window, $"{type.Name}-{culture}-{width}x{height}-{preset}");
            }
        }
        finally { window.Close(); }
    });

    [TestCase("en-US", 1280, 720, FontSizePreset.Smaller)]
    [TestCase("zh-CN", 360, 800, FontSizePreset.Large)]
    [TestCase("en-US", 800, 360, FontSizePreset.Larger)]
    [TestCase("zh-CN", 1920, 1080, FontSizePreset.Large)]
    public Task SettingsNavigationKeepsTwoDistinctLevelsAndStablePositions(string culture, int width, int height, FontSizePreset preset)
        => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await CreateHost();
        host.Services.GetRequiredService<AvaloniaPlayerLocalization>().ApplyCulture(culture);
        var settings = host.Services.GetRequiredService<AvaloniaPlayerSettingsService>();
        await settings.Categories.SelectMany(category => category.Items).OfType<ChoiceSetting<FontSizePreset>>().Single().TrySetValueAsync(preset);
        settings.Input.SelectedDevice = PlayerInputDevice.KeyboardMouse;
        host.Services.GetRequiredService<IGameNavigationService>().ResetTo<GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel>(NavigationTransition.None);
        var window = new Window { Width = width, Height = height, Content = host.View };
        window.Show();
        try
        {
            window.UpdateLayout();
            var categories = window.GetVisualDescendants().OfType<TabControl>().Single(tabs => tabs.Name == "CategoryTabs");
            var headers = Enumerable.Range(0, categories.ItemCount).Select(index => (TabItem)categories.ContainerFromIndex(index)!).ToArray();
            var positions = headers.Select(tab => tab.TranslatePoint(default, categories)!.Value.X).ToArray();
            for (var index = 0; index < headers.Length; index++)
            {
                categories.SelectedIndex = index;
                window.UpdateLayout();
                for (var other = 0; other < headers.Length; other++)
                {
                    Assert.That(headers[other].TranslatePoint(default, categories)!.Value.X, Is.EqualTo(positions[other]).Within(1),
                        "Changing the selected weight must not move neighboring category labels.");
                    Assert.That(headers[other].FontWeight, Is.EqualTo(other == index ? global::Avalonia.Media.FontWeight.SemiBold : global::Avalonia.Media.FontWeight.Normal));
                }
            }
            categories.SelectedIndex = 1;
            window.UpdateLayout();
            var primary = headers[1];
            var primaryBackground = primary.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_LayoutRoot");
            Assert.That(((global::Avalonia.Media.ISolidColorBrush)primaryBackground.Background!).Color.A, Is.Zero);
            var pointer = primary.TranslatePoint(new Point(primary.Bounds.Width / 2, primary.Bounds.Height / 2), window)!.Value;
            window.MouseMove(pointer);
            Assert.That(((global::Avalonia.Media.ISolidColorBrush)primaryBackground.Background!).Color.A, Is.GreaterThan(0),
                "A selected category still provides hover feedback.");
            window.MouseMove(default);
            Assert.That(((global::Avalonia.Media.ISolidColorBrush)primaryBackground.Background!).Color.A, Is.Zero);
            var panel = window.GetVisualDescendants().OfType<InputBindingsPanel>().Single();
            var devices = panel.GetVisualDescendants().OfType<TabControl>().Single();
            var keyboard = (TabItem)devices.ContainerFromIndex(0)!;
            var label = keyboard.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.That(label.FontSize, Is.EqualTo(PlayerTypography.Presets[preset].Action));
            Assert.That(label.FontSize, Is.LessThan(primary.FontSize));
            Assert.That(label.FontWeight, Is.EqualTo(global::Avalonia.Media.FontWeight.Normal));
            Assert.That(keyboard.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_SelectedPipe").IsVisible, Is.False);
            var background = keyboard.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PART_LayoutRoot");
            Assert.That(((global::Avalonia.Media.ISolidColorBrush)background.Background!).Color.A, Is.GreaterThan(0));
            Assert.That(panel.TranslatePoint(default, categories)!.Value.X, Is.EqualTo(0).Within(1));
            var editor = panel.GetVisualDescendants().OfType<InputBindingCapture>().First();
            Capture(window, $"operation-{culture}-{width}x{height}-{preset}");
            keyboard.Focus();
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Assert.That(devices.SelectedIndex, Is.EqualTo(1));
            Assert.That(settings.Input.SelectedDevice, Is.EqualTo(PlayerInputDevice.Gamepad));
            devices.SelectedIndex = 0;
            window.UpdateLayout();
            Assert.That(panel.GetVisualDescendants().OfType<InputBindingCapture>().First(), Is.SameAs(editor));
            Assert.That(label.FontWeight, Is.EqualTo(global::Avalonia.Media.FontWeight.Normal));
        }
        finally { window.Close(); }
    });

    private static async Task<AvaloniaPlayerHost> CreateHost()
    {
        var host = await AvaloniaPlayerHost.CreateAsync(SampleCustomization.CreateHostOptions(), services => SampleCustomization.ConfigureServices(services));
        host.Services.GetRequiredService<IPlayerApplication>().CompleteStartup(null);
        return host;
    }

    [TestCase("zh-CN", 360, 800, FontSizePreset.Large)]
    [TestCase("zh-CN", 800, 360, FontSizePreset.Larger)]
    [TestCase("en-US", 800, 360, FontSizePreset.Large)]
    public Task LargeTextAndLocalizedCategoriesKeepNavigationAndControlsUsable(string culture, int width, int height, FontSizePreset preset)
        => SampleUiTests.RunAsync(async () =>
    {
        await using var host = await CreateHost();
        host.Services.GetRequiredService<AvaloniaPlayerLocalization>().ApplyCulture(culture);
        var settings = host.Services.GetRequiredService<AvaloniaPlayerSettingsService>();
        Assert.That(await settings.Categories.SelectMany(category => category.Items).OfType<ChoiceSetting<FontSizePreset>>().Single().TrySetValueAsync(preset), Is.True);
        host.Services.GetRequiredService<IGameNavigationService>().ResetTo<GalNet.Sample.CrossPlatform.ViewModels.SettingsPageViewModel>(NavigationTransition.None);
        var window = new Window { Width = width, Height = height, Content = host.View };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var page = window.GetVisualDescendants().OfType<GalNet.Sample.CrossPlatform.Views.SettingsPage>().Single();
            var content = page.GetVisualDescendants().OfType<ScrollViewer>().Single(item => item.Name == "CategoryContentScroll");
            Assert.That(content.Viewport.Height, Is.GreaterThan(80));
            foreach (var control in page.GetVisualDescendants().OfType<Control>().Where(control => control is ComboBox or Slider or TextBox))
            {
                var point = control.TranslatePoint(default, host.View)!.Value;
                var end = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), host.View)!.Value;
                Assert.That(point.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(end.X, Is.LessThanOrEqualTo(width));
            }
            var tabs = page.GetVisualDescendants().OfType<TabControl>().Single(item => item.Name == "CategoryTabs");
            var initialTop = tabs.TranslatePoint(default, page)!.Value.Y;
            content.Offset = new Vector(0, content.Extent.Height);
            window.UpdateLayout();
            Assert.That(tabs.TranslatePoint(default, page)!.Value.Y, Is.EqualTo(initialTop));
            Capture(window, $"settings-{culture}-{width}x{height}-{preset}");
        }
        finally { window.Close(); }
    });

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("GALNET_MOBILE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var image = window.CaptureRenderedFrame();
        image!.Save(Path.Combine(directory, name + ".png"));
    }
}
