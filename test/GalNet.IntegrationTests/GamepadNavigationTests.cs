using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Input;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.Themes;
using GalNet.Game.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class GamepadNavigationTests
{
    [TestCase(2)]
    [TestCase(7)]
    [TestCase(13)]
    public Task DynamicMenusWrapAndSkipHiddenOrDisabledButtons(int count) => Ui(() =>
    {
        using var input = Settings();
        var panel = new StackPanel();
        var buttons = Enumerable.Range(0, count).Select(i => new Button { Content = $"Action {i}", Height = 30 }).ToArray();
        foreach (var button in buttons) panel.Children.Add(button);
        panel.Children.Insert(1, new Button { Content = "Hidden", IsVisible = false });
        panel.Children.Insert(2, new Button { Content = "Disabled", IsEnabled = false });
        var clicks = 0;
        buttons[^1].Click += (_, _) => clicks++;
        using var view = new View(panel);
        using var router = new PlayerInputRouter(panel, input, () => null, new Session(), () => { });
        buttons[0].Focus();
        Tap(input, "Up");
        Assert.That(view.Focused, Is.SameAs(buttons[^1]));
        Assert.That(buttons[^1].Classes, Does.Contain(":focus-visible"));
        Tap(input, "South");
        Assert.That(clicks, Is.EqualTo(1));
        Tap(input, "Down");
        Assert.That(view.Focused, Is.SameAs(buttons[0]));
        var added = new Button { Content = "Added later", Height = 30 };
        panel.Children.Add(added);
        view.Window.UpdateLayout();
        Tap(input, "Up");
        Assert.That(view.Focused, Is.SameAs(added));
        panel.Children.Remove(added);
        view.Window.UpdateLayout();
        Tap(input, "Down");
        Assert.That(view.Focused, Is.SameAs(buttons[0]));
        return Task.CompletedTask;
    });

    [TestCase(2)]
    [TestCase(4)]
    public Task GridUsesActualColumnsAndWrapsWithinRowsAndColumns(int columns) => Ui(() =>
    {
        using var input = Settings();
        var panel = new Canvas();
        var buttons = Enumerable.Range(0, columns * 3).Select(i => new Button { Content = i.ToString(), Width = 90, Height = 40 }).ToArray();
        for (var i = 0; i < buttons.Length; i++)
        { Canvas.SetLeft(buttons[i], i % columns * 110); Canvas.SetTop(buttons[i], i / columns * 70); panel.Children.Add(buttons[i]); }
        using var view = new View(panel);
        using var router = new PlayerInputRouter(panel, input, () => null, new Session(), () => { });
        buttons[1].Focus();
        Tap(input, "Down"); Assert.That(view.Focused, Is.SameAs(buttons[columns + 1]));
        Tap(input, "Down"); Assert.That(view.Focused, Is.SameAs(buttons[columns * 2 + 1]));
        Tap(input, "Down"); Assert.That(view.Focused, Is.SameAs(buttons[1]));
        buttons[columns - 1].Focus();
        Tap(input, "Right"); Assert.That(view.Focused, Is.SameAs(buttons[0]));
        // Reflow the same controls without rebuilding navigation metadata.
        var newColumns = columns + 1;
        for (var i = 0; i < buttons.Length; i++)
        { Canvas.SetLeft(buttons[i], i % newColumns * 110); Canvas.SetTop(buttons[i], i / newColumns * 70); }
        view.Window.UpdateLayout(); buttons[1].Focus(); Tap(input, "Down");
        Assert.That(view.Focused, Is.SameAs(buttons[newColumns + 1]));
        return Task.CompletedTask;
    });

    [Test]
    public Task ToggleActivationRunsNativeClickAndCommandOnce() => Ui(() =>
    {
        using var input = Settings();
        var calls = 0; var clicks = 0;
        var toggle = new ToggleButton { Content = "Auto", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => calls++) };
        toggle.Click += (_, _) => clicks++;
        using var view = new View(toggle);
        using var router = new PlayerInputRouter(toggle, input, () => null, new Session(), () => { });
        toggle.Focus(); Tap(input, "South");
        Assert.Multiple(() => { Assert.That(toggle.IsChecked, Is.True); Assert.That(calls, Is.EqualTo(1)); Assert.That(clicks, Is.EqualTo(1)); });
        return Task.CompletedTask;
    });

    [Test]
    public Task ComboPreviewCanCancelOrCommitAndSliderKeepsBounds() => Ui(() =>
    {
        using var input = Settings();
        var combo = new ComboBox { ItemsSource = new[] { "A", "B", "C" }, SelectedIndex = 0 };
        var slider = new Slider { Minimum = 0, Maximum = 10, SmallChange = 3, Value = 9 };
        var panel = new StackPanel { Children = { combo, slider } };
        using var view = new View(panel);
        using var router = new PlayerInputRouter(panel, input, () => null, new Session(), () => { });
        combo.Focus(); Tap(input, "South"); Tap(input, "Down");
        Assert.That(combo.SelectedIndex, Is.EqualTo(1)); Assert.That(input.IsPreviewingChoice, Is.True);
        Tap(input, "East"); Assert.That(combo.SelectedIndex, Is.Zero); Assert.That(combo.IsDropDownOpen, Is.False);
        Tap(input, "South"); Tap(input, "Down"); Tap(input, "South");
        Assert.That(combo.SelectedIndex, Is.EqualTo(1)); Assert.That(input.IsPreviewingChoice, Is.False);
        slider.Focus(); Tap(input, "Right"); Assert.That(slider.Value, Is.EqualTo(10));
        Tap(input, "Right"); Assert.That(slider.Value, Is.EqualTo(10));
        return Task.CompletedTask;
    });

    [Test]
    public Task PageScopePreventsOutgoingPageActivationAndRemembersFocus() => Ui(() =>
    {
        using var input = Settings();
        var oldButton = new Button { Content = "Old" };
        var first = new Button { Content = "First" }; var second = new Button { Content = "Second" };
        var oldPage = new StackPanel { Children = { oldButton } };
        var nextPage = new StackPanel { Children = { first, second } };
        var root = new StackPanel { Children = { oldPage, nextPage } };
        Control activePage = oldPage; var clicks = 0;
        second.Click += (_, _) => clicks++;
        using var view = new View(root);
        using var router = new PlayerInputRouter(root, input, () => null, new Session(), () => { }, activePage: () => activePage);
        oldButton.Focus(); Tap(input, "Down");
        activePage = nextPage;
        Tap(input, "Down"); Assert.That(view.Focused, Is.SameAs(second));
        Tap(input, "South"); Assert.That(clicks, Is.EqualTo(1));
        activePage = oldPage; Tap(input, "Down");
        activePage = nextPage; Tap(input, "South");
        Assert.That(view.Focused, Is.SameAs(second)); Assert.That(clicks, Is.EqualTo(2));
        return Task.CompletedTask;
    });

    [Test]
    public Task StoryChoicesHighlightWithoutSubmittingAndCancelCannotEscape() => Ui(async () =>
    {
        using var input = Settings();
        var game = new GamePresentationState { IsGameUiReady = true };
        var choiceTask = game.WaitForChoiceAsync(new[] { "One", "Two", "Three" }, CancellationToken.None);
        var page = new GalNet.Sample.CrossPlatform.Views.GamePage(game);
        using var view = new View(page);
        using var router = new PlayerInputRouter(page, input, () => game, new Session(), () => Assert.Fail("Choice must own navigation"));
        Tap(input, "Down");
        Assert.That(choiceTask.IsCompleted, Is.False);
        Tap(input, "East"); Assert.That(choiceTask.IsCompleted, Is.False);
        Tap(input, "Down"); Assert.That(choiceTask.IsCompleted, Is.False);
        var selected = page.Choice.SelectedIndex;
        Capture(view.Window, "choice-highlight");
        Tap(input, "South"); Assert.That(await choiceTask, Is.EqualTo(selected));
    });

    [Test]
    public Task ChoicePointerAndKeyboardStillCommitExplicitly() => Ui(() =>
    {
        var choice = new ChoiceList { ItemsSource = new[] { "One", "Two" }, Height = 160 };
        var commits = new List<int>(); choice.ChoiceSelected += (_, index) => commits.Add(index);
        using var view = new View(choice);
        choice.PreviewSelection(1); Assert.That(commits, Is.Empty);
        Assert.That(choice.Focus(), Is.True);
        view.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        view.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.That(commits, Is.EqualTo(new[] { 1 }));
        var item = choice.ContainerFromIndex(0)!;
        var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), view.Window)!.Value;
        view.Window.MouseDown(point, MouseButton.Left); view.Window.MouseUp(point, MouseButton.Left);
        Assert.That(commits, Is.EqualTo(new[] { 1, 0 }));
        view.Window.MouseDown(point, MouseButton.Left);
        view.Window.MouseMove(new Point(point.X, point.Y + 80));
        view.Window.MouseUp(new Point(point.X, point.Y + 80), MouseButton.Left);
        Assert.That(commits, Is.EqualTo(new[] { 1, 0 }), "Dragging the list must not commit a branch.");
        return Task.CompletedTask;
    });

    [Test]
    public Task StartEntersToolbarAndCancelOrStartReturnsToStory() => Ui(() =>
    {
        using var input = Settings();
        var game = new GamePresentationState();
        var button = new Button { Content = "Toolbar" }; var toolbar = new StackPanel { Children = { button } };
        PlayerNavigation.SetIsToolbar(toolbar, true);
        var root = new Border { Child = toolbar }; var actions = 0;
        using var view = new View(root);
        using var router = new PlayerInputRouter(root, input, () => game, new Session(), () => { }, _ => { actions++; return Task.CompletedTask; });
        Tap(input, "Start"); Assert.That(view.Focused, Is.SameAs(button)); Assert.That(actions, Is.Zero);
        Tap(input, "East"); Assert.That(view.Focused, Is.SameAs(root));
        Tap(input, "Start"); Assert.That(view.Focused, Is.SameAs(button));
        Tap(input, "Start"); Assert.That(view.Focused, Is.SameAs(root));
        return Task.CompletedTask;
    });

    [TestCase(false)]
    [TestCase(true)]
    public Task HostModalOwnsNavigationEvenWhenShellIsDisabled(bool eastConfirms) => Ui(async () =>
    {
        await using var host = await GalNet.Avalonia.GameHost.AvaloniaPlayerHost.CreateAsync(
            GalNet.Sample.CrossPlatform.SampleCustomization.CreateHostOptions(), services => GalNet.Sample.CrossPlatform.SampleCustomization.ConfigureServices(services));
        host.Services.GetRequiredService<GalNet.Avalonia.GameHost.IPlayerApplication>().CompleteStartup(null);
        var input = host.Settings.Input;
        await input.SetLayoutAsync(eastConfirms);
        using var view = new View(host.View);
        var confirmation = host.ConfirmExitAsync(); view.Window.UpdateLayout();
        Tap(input, "Right");
        Capture(view.Window, eastConfirms ? "modal-east" : "modal-south");
        Assert.That((view.Focused as Control)?.Name, Is.EqualTo("ConfirmExitButton"));
        Tap(input, eastConfirms ? "South" : "East");
        Assert.That(await confirmation, Is.False);
        confirmation = host.ConfirmExitAsync(); view.Window.UpdateLayout();
        Tap(input, "Right"); Tap(input, eastConfirms ? "East" : "South");
        Assert.That(await confirmation, Is.True);
    });

    [Test]
    public Task MoreMenuCanOpenNavigateActivateAndCancelWithGamepad() => Ui(() =>
    {
        using var input = Settings(); var clicks = 0;
        var first = new MenuItem { Header = "First" }; var second = new MenuItem { Header = "Second", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => clicks++) };
        var flyout = new MenuFlyout { Items = { first, second } };
        var button = new Button { Content = "More", Flyout = flyout };
        using var view = new View(button);
        using var router = new PlayerInputRouter(button, input, () => null, new Session(), () => Assert.Fail("Cancel must close flyout"));
        button.Focus(); Tap(input, "South"); view.Window.UpdateLayout();
        Assert.That(flyout.IsOpen, Is.True);
        first.Focus(); Tap(input, "Up"); Assert.That(view.Focused, Is.SameAs(second));
        Tap(input, "Down"); Assert.That(view.Focused, Is.SameAs(first));
        Tap(input, "Down"); Tap(input, "South");
        Assert.That(clicks, Is.EqualTo(1)); Assert.That(flyout.IsOpen, Is.False);
        Tap(input, "South"); view.Window.UpdateLayout(); Tap(input, "East"); Assert.That(flyout.IsOpen, Is.False);
        return Task.CompletedTask;
    });

    [Test]
    public void OldStartBindingIsRemovedWithoutLosingOtherPreferences()
    {
        using var input = Settings();
        var config = PlayerInputSettings.Defaults();
        config.Bindings["open.settings"] = [new(PlayerInputDevice.Gamepad, "Start")];
        config.Bindings["toggle.auto"] = [new(PlayerInputDevice.Gamepad, "LB")];
        input.Restore(System.Text.Json.JsonSerializer.Serialize(config));
        Assert.That(input.Resolve(PlayerInputDevice.Gamepad, "Start"), Is.Null);
        Assert.That(input.Resolve(PlayerInputDevice.Gamepad, "LB"), Is.EqualTo("toggle.auto"));
    }

    [Test]
    public Task RealChoiceEditorDoesNotApplyPreviewUntilConfirmed() => Ui(async () =>
    {
        using var input = Settings();
        var value = "one"; var applied = new List<string>();
        var setting = new GalNet.Avalonia.GameView.Settings.ChoiceSetting<string>("choice", "Choice", "one",
            [new("one", "One", "one"), new("two", "Two", "two"), new("three", "Three", "three")], () => value,
            (next, _) => { value = next; applied.Add(next); return Task.CompletedTask; });
        var categories = new[] { new GalNet.Avalonia.GameView.Settings.SettingCategory("game", "Game", [setting]) };
        var coordinator = new GalNet.Avalonia.GameView.Settings.SettingsCoordinator(); coordinator.Attach(categories);
        var panel = new GalNet.Avalonia.GameView.Controls.SettingsCategories { Categories = categories, Editors = GalNet.Game.Controls.Forms.EditorRegistry.CreateDefault() };
        using var view = new View(panel);
        using var router = new PlayerInputRouter(panel, input, () => null, new Session(), () => { });
        var combo = panel.GetVisualDescendants().OfType<ComboBox>().Single();
        combo.Focus(); Tap(input, "South"); Tap(input, "Down"); await coordinator.FlushAsync();
        Assert.That(applied, Is.Empty); Assert.That(value, Is.EqualTo("one"));
        Tap(input, "East"); await coordinator.FlushAsync(); Assert.That(applied, Is.Empty);
        Tap(input, "South"); Tap(input, "Down");
        view.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        view.Window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        await coordinator.FlushAsync(); Assert.That(applied, Is.Empty); Assert.That(combo.SelectedIndex, Is.Zero);
        Tap(input, "South"); Tap(input, "Down"); Tap(input, "South"); await coordinator.FlushAsync();
        Assert.That(applied, Is.EqualTo(new[] { "two" }));
        Tap(input, "South"); Tap(input, "Down"); input.CancelGamepad(); await coordinator.FlushAsync();
        Assert.That(applied, Is.EqualTo(new[] { "two" })); Assert.That(input.IsPreviewingChoice, Is.False);
    });

    [Test]
    public Task ShoulderButtonsUseNearestTabsAndWrap() => Ui(() =>
    {
        using var input = Settings();
        var hidden = new TabControl { IsVisible = false, Items = { new TabItem { Header = "Wrong" } } };
        var nested = new TabControl { Items = { new TabItem { Header = "A", Content = new Button { Content = "Inside" } }, new TabItem { Header = "B" }, new TabItem { Header = "C" } } };
        var tabs = new TabControl { Items = { new TabItem { Header = "Outer", Content = nested }, new TabItem { Header = "Other" } } };
        var root = new StackPanel { Children = { hidden, tabs } };
        using var view = new View(root);
        using var router = new PlayerInputRouter(root, input, () => null, new Session(), () => { });
        nested.GetVisualDescendants().OfType<Button>().Single().Focus();
        Tap(input, "LB"); Assert.That(nested.SelectedIndex, Is.EqualTo(2)); Assert.That(tabs.SelectedIndex, Is.Zero);
        Tap(input, "RB"); Assert.That(nested.SelectedIndex, Is.Zero);
        return Task.CompletedTask;
    });

    [Test]
    public Task CancellationStopsDirectionRepeatAndRestoresChoicePreview() => Ui(async () =>
    {
        using var input = Settings();
        var first = new Button { Content = "First" }; var second = new Button { Content = "Second" };
        var root = new StackPanel { Children = { first, second } };
        using var view = new View(root);
        using var router = new PlayerInputRouter(root, input, () => null, new Session(), () => { });
        first.Focus(); input.ObserveGamepad(new HashSet<string> { "Down" }); Assert.That(view.Focused, Is.SameAs(second));
        input.CancelGamepad();
        await Task.Delay(550); Dispatcher.UIThread.RunJobs();
        Assert.That(view.Focused, Is.SameAs(second));
    });

    [Test]
    public Task DiagonalStickKeepsOneDirectionInsteadOfBecomingAChord() => Ui(() =>
    {
        using var input = Settings();
        var first = new Button { Content = "First" }; var second = new Button { Content = "Second" };
        var root = new StackPanel { Children = { first, second } };
        using var view = new View(root);
        using var router = new PlayerInputRouter(root, input, () => null, new Session(), () => { });
        first.Focus(); input.ObserveGamepad(new HashSet<string> { "Down", "Right" });
        Assert.That(view.Focused, Is.SameAs(second)); input.CancelGamepad();
        return Task.CompletedTask;
    });

    [Test]
    public Task PlainHistoryScrollsWithDirectionsAndTriggers() => Ui(() =>
    {
        using var input = Settings();
        var game = new GamePresentationState();
        game.HistoryLines = Enumerable.Range(0, 80).Select(i => new GalNet.Presentation.Abstractions.Application.DialogueHistoryLine("group", i.ToString(), "Speaker", "History line " + i, false)).ToArray();
        game.IsHistoryOpen = true;
        var page = new GalNet.Sample.CrossPlatform.Views.GamePage(game);
        using var view = new View(page);
        using var router = new PlayerInputRouter(page, input, () => game, new Session(), () => { });
        Dispatcher.UIThread.RunJobs(); view.Window.UpdateLayout();
        var scroll = page.History.GetVisualDescendants().OfType<ScrollViewer>().Single();
        scroll.Offset = new(0, 0); view.Window.UpdateLayout();
        Tap(input, "Down"); Assert.That(scroll.Offset.Y, Is.GreaterThan(0));
        var before = scroll.Offset.Y; Tap(input, "RT"); Assert.That(scroll.Offset.Y, Is.GreaterThan(before));
        Tap(input, "East"); Assert.That(game.IsHistoryOpen, Is.False);
        return Task.CompletedTask;
    });

    [Test]
    public Task FocusedSaveCardScrollsIntoViewWithoutRenamingIt() => Ui(() =>
    {
        using var input = Settings(); var calls = 0;
        var cards = Enumerable.Range(0, 12).Select(i => new SaveSlotCard { Height = 180, IsEmpty = false, CanRename = true,
            Description = "Save " + i, Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => calls++) }).ToArray();
        var panel = new StackPanel(); foreach (var card in cards) panel.Children.Add(card);
        var scroll = new ScrollViewer { Content = panel };
        using var view = new View(scroll);
        using var router = new PlayerInputRouter(scroll, input, () => null, new Session(), () => { });
        cards[0].GetVisualDescendants().OfType<Button>().Single().Focus();
        Tap(input, "Up"); view.Window.UpdateLayout();
        Assert.That(scroll.Offset.Y, Is.GreaterThan(0));
        Tap(input, "South"); Assert.That(calls, Is.EqualTo(1));
        return Task.CompletedTask;
    });

    [Test]
    public Task NumericEditorUsesItsExistingStepAndApplyPath() => Ui(async () =>
    {
        using var input = Settings(); var value = 5;
        var setting = new GalNet.Avalonia.GameView.Settings.IntSetting("count", "Count", 5, 0, 20, 2, () => value,
            (next, _) => { value = next; return Task.CompletedTask; });
        var categories = new[] { new GalNet.Avalonia.GameView.Settings.SettingCategory("game", "Game", [setting]) };
        var coordinator = new GalNet.Avalonia.GameView.Settings.SettingsCoordinator(); coordinator.Attach(categories);
        var editors = GalNet.Game.Controls.Forms.EditorRegistry.CreateDefault();
        editors.Replace(GalNet.Core.Parameters.ValueEditorIds.Numeric, GalNet.Game.Controls.Forms.NumericFieldEditors.CreateUpDown);
        var panel = new GalNet.Avalonia.GameView.Controls.SettingsCategories { Categories = categories, Editors = editors };
        using var view = new View(panel);
        using var router = new PlayerInputRouter(panel, input, () => null, new Session(), () => { });
        var numeric = panel.GetVisualDescendants().OfType<NumericUpDown>().Single();
        numeric.GetVisualDescendants().OfType<TextBox>().Single().Focus();
        Tap(input, "Right"); await coordinator.FlushAsync(); Assert.That(value, Is.EqualTo(7));
        Tap(input, "Left"); await coordinator.FlushAsync(); Assert.That(value, Is.EqualTo(5));
    });

    [Test]
    public Task ScreenshotOverlayAcquiresAndReleasesShellNavigation() => Ui(async () =>
    {
        using var input = Settings();
        var registrations = new ServiceCollection(); GalNet.Sample.CrossPlatform.SampleCustomization.ConfigureServices(registrations);
        await using var services = registrations.BuildServiceProvider();
        var game = new GamePresentationState();
        var story = new Button { Content = "Story" };
        var overlay = new Ursa.Controls.OverlayDialogHost { IsAnimationDisabled = true };
        var root = new Grid { Children = { story, overlay } };
        using var view = new View(root);
        using var router = new PlayerInputRouter(story, input, () => game, new Session(), () => Assert.Fail("Screenshot owns cancel"));
        var capture = services.GetRequiredService<IGameScreenshotService>().CaptureAsync(new("Test", view.Window,
            _ => Task.FromException<byte[]>(new IOException("No preview in this navigation test."))) { SetNavigationScope = router.SetNavigationScope });
        view.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        story.IsEnabled = false;
        Tap(input, "East");
        await capture.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(overlay.IsInModalStatus, Is.False);
    });

    [Test]
    public Task HostModalCancelsBindingCaptureBeforeTakingNavigation() => Ui(() =>
    {
        using var input = Settings();
        using var capture = new InputBindingCapture(input, "toggle.auto", PlayerInputDevice.Gamepad, 0);
        var cancel = new Button { Content = "Cancel" }; var modal = new StackPanel { IsVisible = false, Children = { cancel } };
        var root = new Grid { Children = { capture, modal } };
        using var view = new View(root);
        using var router = new PlayerInputRouter(capture, input, () => null, new Session(), () => Assert.Fail("Background must not receive cancel"));
        capture.BeginCapture(); Assert.That(input.IsCapturing, Is.True);
        capture.IsEnabled = false; modal.IsVisible = true; view.Window.UpdateLayout();
        var cancelled = 0; router.SetNavigationScope(modal, () => cancelled++);
        Assert.That(input.IsCapturing, Is.False);
        Tap(input, "East"); Assert.That(cancelled, Is.EqualTo(1));
        return Task.CompletedTask;
    });

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("GALNET_GAMEPAD_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(directory, name + ".png"));
    }

    private static PlayerInputSettings Settings() => new();
    private static void Tap(PlayerInputSettings input, string code)
    { input.ObserveGamepad(new HashSet<string> { code }); input.ObserveGamepad(new HashSet<string>()); Dispatcher.UIThread.RunJobs(); }
    private static Task Ui(Func<Task> action) => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(GeneralTest.Integration.ScreenshotTestApplication).Assembly)
        .Dispatch<bool>(async () => { await action(); return true; }, TestContext.CurrentContext.CancellationToken);

    private sealed class View : IDisposable
    {
        public Window Window { get; }
        public object? Focused => Window.FocusManager!.GetFocusedElement();
        public View(Control content)
        {
            var frame = new Border { Child = content, Classes = { "galnet-player" } };
            frame.Styles.Add(new TemplateAppearance().CreateStyles());
            Window = new() { Width = 900, Height = 700, Content = frame };
            Window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
            Window.Show(); Window.UpdateLayout();
        }
        public void Dispose() => Window.Close();
    }

    private sealed class Session : IGameSessionService
    {
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public string GameTitle => "Test";
        public string StatusMessage => "";
        public bool IsReady => true;
        public bool IsPlaying => true;
        public bool CanContinue => false;
        public ReadOnlyObservableCollection<GameSaveSlot> SaveSlots { get; } = new(new());
        public Task StartNewGameAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ContinueAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(int slotIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
