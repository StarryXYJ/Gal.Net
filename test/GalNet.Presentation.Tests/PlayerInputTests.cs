using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Input;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Avalonia.Themes;
using GalNet.Avalonia.Themes.Common;
using GalNet.Game.Controls.Forms;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class PlayerInputTests
{
    private static PlayerInputSettings Settings()
    {
        var settings = new PlayerInputSettings();
        settings.CommitAsync = value => { settings.Apply(value); return Task.FromResult(true); };
        return settings;
    }

    [Test]
    public async Task ConflictsCancelKeysAndPersistenceProtectPreviousValues()
    {
        var settings = Settings();
        Assert.That(await settings.SetBindingAsync("toggle.auto", PlayerInputDevice.KeyboardMouse, 0, "H"), Is.False);
        Assert.That(settings.Resolve(PlayerInputDevice.KeyboardMouse, "A"), Is.EqualTo("toggle.auto"));
        Assert.That(await settings.SetBindingAsync("toggle.auto", PlayerInputDevice.KeyboardMouse, 0, "Escape"), Is.False);
        Assert.That(await settings.SetBindingAsync("toggle.auto", PlayerInputDevice.KeyboardMouse, 0, "Shift+MouseX1"), Is.True);
        var restored = Settings();
        restored.Restore(settings.Serialize());
        Assert.That(restored.Resolve(PlayerInputDevice.KeyboardMouse, "Shift+MouseX1"), Is.EqualTo("toggle.auto"));
        restored.Restore("{\"Version\":99}");
        Assert.That(restored.Resolve(PlayerInputDevice.KeyboardMouse, "A"), Is.EqualTo("toggle.auto"));
        restored.Restore("broken json");
        Assert.That(PlayerInputSettings.Validate(restored.Configuration), Is.Null);
    }

    [Test]
    public async Task ConfirmLayoutSwapsPhysicalBindingsAndResetPreservesOtherDevices()
    {
        var settings = Settings();
        await settings.SetLayoutAsync(true);
        Assert.That(settings.EastConfirms, Is.True);
        Assert.That(settings.Resolve(PlayerInputDevice.Gamepad, "East"), Is.EqualTo("advance"));
        Assert.That(await settings.SetBindingAsync("toggle.auto", PlayerInputDevice.Gamepad, 0, "South"), Is.False);
        await settings.SetBindingAsync("toggle.auto", PlayerInputDevice.KeyboardMouse, 0, "Shift+MouseX1");
        await settings.ResetAsync(PlayerInputDevice.Gamepad);
        Assert.That(settings.EastConfirms, Is.False);
        Assert.That(settings.Resolve(PlayerInputDevice.KeyboardMouse, "Shift+MouseX1"), Is.EqualTo("toggle.auto"));
        Assert.That(settings.IsActionAvailable("open.history"), Is.False);
        settings.OpenHistoryAsync = () => Task.CompletedTask;
        Assert.That(settings.IsActionAvailable("open.history"), Is.True);
    }

    [Test]
    public async Task ConcurrentEditsKeepBothChangesAndInvalidGesturesDoNotCommit()
    {
        var settings = Settings();
        settings.CommitAsync = async value => { await Task.Delay(5); settings.Apply(value); return true; };
        await Task.WhenAll(settings.SetBindingAsync("toggle.auto", PlayerInputDevice.KeyboardMouse, 0, "Shift+MouseX1"),
            settings.SetBindingAsync("toggle.quick", PlayerInputDevice.KeyboardMouse, 0, "Control+K"));
        Assert.That(settings.Resolve(PlayerInputDevice.KeyboardMouse, "Shift+MouseX1"), Is.EqualTo("toggle.auto"));
        Assert.That(settings.Resolve(PlayerInputDevice.KeyboardMouse, "Control+K"), Is.EqualTo("toggle.quick"));
        Assert.That(await settings.SetBindingAsync("toggle.auto", PlayerInputDevice.KeyboardMouse, 0, "not-a-key"), Is.False);
        Assert.That(await settings.SetBindingAsync("toggle.auto", PlayerInputDevice.Gamepad, 0, "A+Keyboard"), Is.False);
    }

    [Test]
    public async Task GamepadNavigationCanActivateCaptureAndUsesConfiguredCancel()
    {
        await Ui(async () =>
        {
            var settings = Settings();
            await settings.SetLayoutAsync(true);
            var capture = new InputBindingCapture(settings, "toggle.quick", PlayerInputDevice.Gamepad, 0);
            var root = new Border { Child = capture };
            var window = new Window { Content = root };
            using var router = new PlayerInputRouter(root, settings, () => null, new Session(), () => { });
            window.Show();
            try
            {
                capture.Focus();
                settings.ObserveGamepad(new HashSet<string> { "East" });
                settings.ObserveGamepad(new HashSet<string>());
                Assert.That(settings.IsCapturing, Is.True);
                settings.ObserveGamepad(new HashSet<string> { "South" });
                Assert.That(settings.IsCapturing, Is.False);
                Assert.That(settings.GetBindings("toggle.quick", PlayerInputDevice.Gamepad), Is.Empty);
            }
            finally { window.Close(); }
        });
    }

    [TestCase("Classic")]
    [TestCase("Minimal")]
    [TestCase("Paper")]
    public async Task OperationCategoryMountsAndReleasesCaptureInEveryTheme(string theme)
    {
        await Ui(async () =>
        {
            var settings = Settings();
            settings.GamepadSupported = true;
            settings.LastDevice = PlayerInputDevice.Gamepad;
            InputBindingsPanel? panel = null;
            var item = new CustomSetting<PlayerInputConfiguration>("input", "Input", settings.Configuration,
                () => settings.Configuration, (_, _) => Task.CompletedTask, _ =>
                {
                    panel = new InputBindingsPanel(settings);
                    return new FieldEditorLease(panel, panel.Dispose);
                }) { IsFullWidth = true };
            var view = new SettingsCategories { Categories = [new("operation", "Operation", [item])] };
            var border = new Border { Classes = { "galnet-player" }, Child = view };
            border.Styles.Add((theme switch { "Minimal" => new MinimalTheme().CreateStyles(), "Paper" => new PaperTheme().CreateStyles(), _ => new ClassicTheme().CreateStyles() }));
            var window = new Window { Width = 1280, Height = 720, Content = border };
            window.Show();
            try
            {
                window.UpdateLayout();
                Assert.That(panel, Is.Not.Null);
                var tabs = panel!.GetVisualDescendants().OfType<TabControl>().Single();
                Assert.That(tabs.ItemCount, Is.EqualTo(3));
                Assert.That(tabs.SelectedIndex, Is.EqualTo(1));
                tabs.SelectedIndex = 0;
                window.UpdateLayout();
                var capture = panel.GetVisualDescendants().OfType<InputBindingCapture>().First();
                capture.BeginCapture();
                Assert.That(settings.IsCapturing, Is.True);
                window.Content = null;
                Assert.That(settings.IsCapturing, Is.False);
                settings.ObserveGamepad(new HashSet<string> { "LB" });
            }
            finally { window.Close(); }
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task KeyboardAndMouseUseSameCaptureAndEscapeDoesNotChangeValue()
    {
        await Ui(async () =>
        {
            var settings = Settings();
            var capture = new InputBindingCapture(settings, "toggle.auto", PlayerInputDevice.KeyboardMouse, 0);
            var window = new Window { Width = 700, Height = 400, Content = capture };
            window.Show();
            try
            {
                capture.Focus();
                capture.BeginCapture();
                window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, null);
                window.KeyRelease(Key.K, RawInputModifiers.Control, PhysicalKey.K, null);
                Assert.That(settings.Resolve(PlayerInputDevice.KeyboardMouse, "Control+K"), Is.EqualTo("toggle.auto"));
                capture.BeginCapture();
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.That(settings.IsCapturing, Is.False);
                Assert.That(settings.Resolve(PlayerInputDevice.KeyboardMouse, "Control+K"), Is.EqualTo("toggle.auto"));
                capture.BeginCapture();
                window.MouseDown(new(100, 100), MouseButton.Right);
                window.MouseUp(new(100, 100), MouseButton.Right);
                Assert.That(settings.Resolve(PlayerInputDevice.KeyboardMouse, "MouseRight"), Is.EqualTo("toggle.auto"));
            }
            finally { window.Close(); }
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task GamepadChordCommitsOnceAndCancelKeepsPreviousBinding()
    {
        await Ui(async () =>
        {
            var settings = Settings();
            var capture = new InputBindingCapture(settings, "toggle.quick", PlayerInputDevice.Gamepad, 0);
            var window = new Window { Content = capture };
            window.Show();
            try
            {
                capture.BeginCapture();
                settings.ObserveGamepad(new HashSet<string> { "LB" });
                settings.ObserveGamepad(new HashSet<string> { "LB", "North" });
                settings.ObserveGamepad(new HashSet<string>());
                Assert.That(settings.Resolve(PlayerInputDevice.Gamepad, "LB+North"), Is.EqualTo("toggle.quick"));
                capture.BeginCapture();
                settings.ObserveGamepad(new HashSet<string> { "East" });
                Assert.That(settings.IsCapturing, Is.False);
                Assert.That(settings.Resolve(PlayerInputDevice.Gamepad, "LB+North"), Is.EqualTo("toggle.quick"));
            }
            finally { window.Close(); }
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task RouterDoesNotRepeatOrExecuteOnAnotherPageAndHistoryIsAnOptionalHook()
    {
        await Ui(async () =>
        {
            var settings = Settings();
            var game = new GamePageViewModel(null!);
            GamePageViewModel? active = game;
            var surface = new Border { Focusable = true };
            var window = new Window { Content = surface };
            using var router = new PlayerInputRouter(surface, settings, () => active, new Session(), () => { });
            window.Show();
            try
            {
                surface.Focus();
                window.KeyPress(Key.H, RawInputModifiers.None, PhysicalKey.H, null);
                window.KeyPress(Key.H, RawInputModifiers.None, PhysicalKey.H, null);
                Assert.That(game.IsUiHidden, Is.True);
                window.KeyRelease(Key.H, RawInputModifiers.None, PhysicalKey.H, null);
                active = null;
                window.KeyPress(Key.H, RawInputModifiers.None, PhysicalKey.H, null);
                Assert.That(game.IsUiHidden, Is.True);
                active = game;
                var opened = 0;
                settings.OpenHistoryAsync = () => { opened++; return Task.CompletedTask; };
                await router.DispatchAsync("open.history");
                Assert.That(opened, Is.EqualTo(1));
            }
            finally { window.Close(); }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GameplayShortcutWorksWithWindowOrToolbarFocus(bool toolbarFocus)
    {
        await Ui(async () =>
        {
            using var settings = Settings();
            var game = new GamePageViewModel(null!);
            var toolbar = new Button { Content = "Toolbar" };
            var surface = new Border { Child = toolbar };
            var window = new Window { Content = surface };
            using var router = new PlayerInputRouter(surface, settings, () => game, new Session(), () => { });
            window.Show();
            try
            {
                if (toolbarFocus) Assert.That(toolbar.Focus(), Is.True);
                else window.Focus();
                window.KeyPress(Key.H, RawInputModifiers.None, PhysicalKey.H, null);
                Assert.That(game.IsUiHidden, Is.True, "Gameplay shortcuts must not depend on focusing the scene.");
                window.KeyRelease(Key.H, RawInputModifiers.None, PhysicalKey.H, null);
            }
            finally { window.Close(); }
            await Task.CompletedTask;
        });
    }
    [TestCase("textbox")]
    [TestCase("combobox")]
    [TestCase("slider")]
    [TestCase("host-button")]
    public async Task WindowListenerDoesNotStealEditorOrHostInput(string kind)
    {
        await Ui(async () =>
        {
            using var settings = Settings();
            Control control = kind switch
            {
                "textbox" => new TextBox(), "combobox" => new ComboBox(),
                "slider" => new Slider(), _ => new Button()
            };
            var root = new Border { Child = kind == "host-button" ? null : control };
            var host = new StackPanel { Children = { root } };
            if (kind == "host-button") host.Children.Add(control);
            var window = new Window { Content = host };
            var actions = new List<string>();
            using var listener = new KeyboardMouseShortcutListener(root, settings, () => true,
                action => { actions.Add(action); return Task.CompletedTask; }, () => { });
            window.Show();
            try
            {
                Assert.That(control.Focus(), Is.True);
                Stroke(window, Key.H, PhysicalKey.H);
                Assert.That(actions, Is.Empty);
            }
            finally { window.Close(); }
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task ToolbarRetainsNativeActivationAndPointerInput()
    {
        await Ui(async () =>
        {
            using var settings = Settings();
            var button = new Button { Content = "Toolbar" };
            var root = new Border { Classes = { "galnet-player" }, Child = button };
            root.Styles.Add(new ClassicTheme().CreateStyles());
            var window = new Window { Content = root, Width = 700, Height = 400 };
            var actions = new List<string>();
            var clicked = 0;
            button.Click += (_, _) => clicked++;
            using var listener = new KeyboardMouseShortcutListener(root, settings, () => true,
                action => { actions.Add(action); return Task.CompletedTask; }, () => { });
            window.Show();
            try
            {
                Assert.That(button.Focus(), Is.True);
                Stroke(window, Key.Space, PhysicalKey.Space);
                Assert.That(clicked, Is.EqualTo(1));
                window.UpdateLayout();
                var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.That(clicked, Is.EqualTo(2));
                window.MouseDown(point, MouseButton.Middle);
                window.MouseUp(point, MouseButton.Middle);
                Assert.That(actions, Is.Empty, "Toolbar mouse events must remain UI input.");
                Stroke(window, Key.H, PhysicalKey.H);
                Assert.That(actions, Is.EqualTo(new[] { "toggle.ui" }));
            }
            finally { window.Close(); }
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task CaptureWinsOverWindowShortcutAndReturnsToGameplayAfterRelease()
    {
        await Ui(async () =>
        {
            using var settings = Settings();
            var capture = new InputBindingCapture(settings, "toggle.ui", PlayerInputDevice.KeyboardMouse, 0);
            var root = new Border { Child = capture };
            var window = new Window { Content = root };
            var game = new GamePageViewModel(null!);
            using var router = new PlayerInputRouter(root, settings, () => game, new Session(), () => { });
            window.Show();
            try
            {
                capture.Focus();
                capture.BeginCapture();
                Stroke(window, Key.H, PhysicalKey.H);
                Assert.That(game.IsUiHidden, Is.False);
                Assert.That(settings.IsCapturing, Is.False);
                Stroke(window, Key.H, PhysicalKey.H);
                Assert.That(game.IsUiHidden, Is.True);
            }
            finally { window.Close(); }
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task ListenerFollowsReattachmentAndDisposesWithoutDuplicateSubscriptions()
    {
        await Ui(async () =>
        {
            using var settings = Settings();
            var root = new Border { Focusable = true };
            var first = new Window { Content = root };
            var second = new Window();
            var actions = new List<string>();
            using var listener = new KeyboardMouseShortcutListener(root, settings, () => true,
                action => { actions.Add(action); return Task.CompletedTask; }, () => { });
            first.Show();
            second.Show();
            try
            {
                root.Focus();
                Stroke(first, Key.H, PhysicalKey.H);
                first.Content = null;
                first.Focus();
                Stroke(first, Key.H, PhysicalKey.H);
                Assert.That(actions.Count, Is.EqualTo(1));
                second.Content = root;
                root.Focus();
                Stroke(second, Key.H, PhysicalKey.H);
                Assert.That(actions.Count, Is.EqualTo(2));
                listener.Dispose();
                Stroke(second, Key.H, PhysicalKey.H);
                Assert.That(actions.Count, Is.EqualTo(2));
            }
            finally { first.Close(); second.Close(); }
            await Task.CompletedTask;
        });
    }

    [Test]
    public async Task HeldModifierDoesNotLeakThroughChordsFocusChangesOrDetach()
    {
        await Ui(async () =>
        {
            using var settings = Settings();
            await settings.SetBindingAsync("toggle.ui", PlayerInputDevice.KeyboardMouse, 0, "Control+H");
            var root = new Border { Focusable = true };
            var editor = new TextBox();
            var host = new StackPanel { Children = { root, editor } };
            var window = new Window { Content = host };
            var actions = new List<string>();
            var held = false;
            using var listener = new KeyboardMouseShortcutListener(root, settings, () => true,
                action => { actions.Add(action); held = action == "hold.quick"; return Task.CompletedTask; }, () => held = false);
            window.Show();
            try
            {
                root.Focus();
                window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
                window.KeyPress(Key.H, RawInputModifiers.Control, PhysicalKey.H, null);
                window.KeyRelease(Key.H, RawInputModifiers.Control, PhysicalKey.H, null);
                window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
                await Task.Delay(220);
                Assert.That(actions, Is.EqualTo(new[] { "toggle.ui" }));
                actions.Clear();
                window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
                editor.Focus();
                await Task.Delay(220);
                Assert.That(actions, Is.Empty);
                window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
                root.Focus();
                window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
                await Task.Delay(220);
                Assert.That(held, Is.True);
                host.Children.Remove(root);
                Assert.That(held, Is.False);
            }
            finally { window.Close(); }
        });
    }

    [Test]
    public async Task HoldActionSupportsConfiguredModifierChordsAndReleasesOnKeyUp()
    {
        await Ui(async () =>
        {
            using var settings = Settings();
            Assert.That(await settings.SetBindingAsync("hold.quick", PlayerInputDevice.KeyboardMouse, 0, "Control+K"), Is.True);
            var root = new Border { Focusable = true };
            var window = new Window { Content = root };
            var held = false;
            using var listener = new KeyboardMouseShortcutListener(root, settings, () => true,
                action => { held = action == "hold.quick"; return Task.CompletedTask; }, () => held = false);
            window.Show();
            try
            {
                root.Focus();
                window.KeyPress(Key.LeftCtrl, RawInputModifiers.Control, PhysicalKey.ControlLeft, null);
                window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, null);
                await Task.Delay(220);
                Assert.That(held, Is.True);
                window.KeyRelease(Key.K, RawInputModifiers.Control, PhysicalKey.K, null);
                Assert.That(held, Is.False);
                window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null);
            }
            finally { window.Close(); }
        });
    }

    [Test]
    public async Task DispatcherBlocksStoryActionsWhileChoiceOrSkipPromptIsOpen()
    {
        using var settings = Settings();
        var game = new GamePageViewModel(null!);
        var dispatcher = new GameActionDispatcher(settings, () => game, new Session());
        game.IsChoiceVisible = true;
        await dispatcher.DispatchAsync("toggle.ui");
        Assert.That(game.IsUiHidden, Is.False);
        game.IsChoiceVisible = false;
        game.IsSkipConfirmationOpen = true;
        await dispatcher.DispatchAsync("toggle.ui");
        Assert.That(game.IsUiHidden, Is.False);
        game.IsSkipConfirmationOpen = false;
        await dispatcher.DispatchAsync("toggle.ui");
        Assert.That(game.IsUiHidden, Is.True);
    }

    [TestCase("Classic")]
    [TestCase("Minimal")]
    [TestCase("Paper")]
    public async Task GameplaySurfaceDispatchesKeyboardMouseAndUpdatedBindingsInEveryTheme(string theme)
    {
        await Ui(async () =>
        {
            using var settings = Settings();
            var game = new GamePageViewModel(null!) { Input = settings };
            var page = new GalNet.Avalonia.GameView.Page.GamePage { DataContext = game };
            var root = new Border { Classes = { "galnet-player" }, Child = page };
            root.Styles.Add(theme switch
            {
                "Minimal" => new MinimalTheme().CreateStyles(),
                "Paper" => new PaperTheme().CreateStyles(),
                _ => new ClassicTheme().CreateStyles()
            });
            var window = new Window { Content = root, Width = 1280, Height = 720 };
            var advances = 0;
            var screenshots = 0;
            game.AdvanceRequested += () => advances++;
            game.ScreenshotRequested += () => screenshots++;
            using var router = new PlayerInputRouter(root, settings, () => game, new Session(), () => { });
            window.Show();
            try
            {
                window.Focus();
                Stroke(window, Key.Space, PhysicalKey.Space);
                Stroke(window, Key.F12, PhysicalKey.F12);
                Assert.That(advances, Is.EqualTo(1));
                Assert.That(screenshots, Is.EqualTo(1));
                Stroke(window, Key.H, PhysicalKey.H);
                Assert.That(game.IsUiHidden, Is.True);
                Stroke(window, Key.Space, PhysicalKey.Space);
                Assert.That(game.IsUiHidden, Is.False);
                Assert.That(advances, Is.EqualTo(1), "Restoring hidden UI must not also advance dialogue.");
                Assert.That(await settings.SetBindingAsync("toggle.ui", PlayerInputDevice.KeyboardMouse, 0, "Control+K"), Is.True);
                Stroke(window, Key.H, PhysicalKey.H);
                Assert.That(game.IsUiHidden, Is.False);
                window.KeyPress(Key.K, RawInputModifiers.Control, PhysicalKey.K, null);
                window.KeyRelease(Key.K, RawInputModifiers.Control, PhysicalKey.K, null);
                Assert.That(game.IsUiHidden, Is.True);
                window.UpdateLayout();
                window.MouseMove(new(640, 200));
                window.MouseDown(new(640, 200), MouseButton.Middle);
                window.MouseUp(new(640, 200), MouseButton.Middle);
                Assert.That(game.IsUiHidden, Is.False);
            }
            finally { window.Close(); }
        });
    }

    private static void Stroke(Window window, Key key, PhysicalKey physicalKey)
    {
        window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
        window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
    }

    private static Task Ui(Func<Task> action) => HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly)
        .Dispatch<bool>(async () => { await action(); Dispatcher.UIThread.RunJobs(); return true; }, TestContext.CurrentContext.CancellationToken);

    [Test]
    public async Task TouchGesturesDoNotAlsoDispatchTheSingleTap()
    {
        await Ui(async () =>
        {
            var settings = Settings();
            await settings.SetBindingAsync("toggle.auto", PlayerInputDevice.Touch, 0, "DoubleTap");
            var actions = new List<string>();
            var surface = new Border();
            var window = new Window { Width = 700, Height = 400, Content = surface };
            using var touch = new TouchInputRecognizer(surface, settings, () => true, action => { actions.Add(action); return Task.CompletedTask; });
            window.Show();
            try
            {
                var first = new Pointer(1, PointerType.Touch, true);
                var second = new Pointer(2, PointerType.Touch, false);
                Press(surface, first, new(100, 100));
                Release(surface, first, new(100, 100));
                Press(surface, second, new(100, 100));
                Release(surface, second, new(100, 100));
                Assert.That(actions, Is.EqualTo(new[] { "toggle.auto" }));
                actions.Clear();
                Press(surface, first, new(100, 100));
                Press(surface, second, new(120, 100));
                Release(surface, first, new(100, 100));
                Release(surface, second, new(120, 100));
                Assert.That(actions, Is.EqualTo(new[] { "toggle.ui" }));
            }
            finally { window.Close(); }
        });
    }
    private static void Press(Control root, Pointer pointer, global::Avalonia.Point point) => root.RaiseEvent(new PointerPressedEventArgs(
        root, pointer, root, point, 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
    private static void Release(Control root, Pointer pointer, global::Avalonia.Point point) => root.RaiseEvent(new PointerReleasedEventArgs(
        root, pointer, root, point, 0, new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased), KeyModifiers.None, MouseButton.Left));

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
