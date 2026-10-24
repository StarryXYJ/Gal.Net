using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Editor.Themes;

namespace GeneralTest.Editor;

public sealed partial class ProjectWorkspaceLifecycleTests
{
    [Test]
    public async Task UiuxMotionUsesOneSurfaceAndRespondsOnTheFirstFrames()
    {
        await using var ui = HeadlessUnitTestSession.StartNew(typeof(EditorFormTestApplication));
        await ui.Dispatch<bool>(() =>
        {
            PrepareGroupTheme();
            var app = Application.Current!; var origin = new Uri("avares://GalNet.Editor/");
            app.Resources.MergedDictionaries.Add(new ResourceInclude(origin) { Source = new Uri("avares://GalNet.Editor/Themes/EditorButtons.axaml") });
            app.Styles.Add(new StyleInclude(origin) { Source = new Uri("avares://GalNet.Editor/Themes/ThemeTransitions.axaml") });
            app.Styles.Add(new StyleInclude(origin) { Source = new Uri("avares://GalNet.Editor/Themes/EditorControlStyles.axaml") });
            foreach (var (variant, name) in new[] { (EditorThemeVariant.Violet, "Violet"), (EditorThemeVariant.Aurora, "Aurora") })
            {
                var resources = new ResourceDictionary();
                resources.MergedDictionaries.Add(new ResourceInclude(origin) { Source = new Uri("avares://Semi.Avalonia/Themes/Dark/_index.axaml") });
                resources.MergedDictionaries.Add(new ResourceInclude(origin) { Source = new Uri($"avares://GalNet.Editor/Themes/{name}Theme.axaml") });
                app.Resources.ThemeDictionaries[variant] = resources;
            }
            foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark, EditorThemeVariant.Violet, EditorThemeVariant.Aurora })
            {
                app.RequestedThemeVariant = variant;
                var button = new Button { Content = "button" }; var toggle = new ToggleButton { Content = "entry type" };
                var icon = new ToggleButton { Content = "icon", Theme = (ControlTheme)app.FindResource("EditorIconToggleButton")! };
                var input = new TextBox { Text = "reference" }; var menu = new MenuItem { Header = "menu" };
                var panel = new StackPanel { Children = { button, toggle, icon, input, menu } };
                var window = new Window { Content = panel, RequestedThemeVariant = variant, Width = 600, Height = 400 };
                var clock = new MotionTestClock(); clock.Attach(window); window.Show(); TimelinePump(window);
                foreach (var visual in window.GetVisualDescendants().OfType<Control>()) clock.Attach(visual);
                void Step(int milliseconds) { Dispatcher.UIThread.RunJobs(); clock.Step(milliseconds); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
                IBrush Resource(string key) => (IBrush)button.FindResource(variant, key)!;
                try
                {
                    Step(0); Step(1000);
                    var surface = button.GetVisualDescendants().OfType<ContentPresenter>().Single(item => item.Name == "PART_ContentPresenter");
                    var toggleSurface = toggle.GetVisualDescendants().OfType<ContentPresenter>().Single(item => item.Name == "PART_ContentPresenter");
                    var iconSurface = icon.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "ButtonSurface");
                    var iconPresenter = icon.GetVisualDescendants().OfType<ContentPresenter>().Single();
                    var inputSurface = input.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "PART_ContentPresenterBorder");
                    var menuSurface = menu.GetVisualDescendants().OfType<Border>().Single(item => item.Name == "PART_LayoutRoot");
                    foreach (var painted in new Control[] { surface, toggleSurface, iconSurface, inputSurface, menuSurface })
                        foreach (var transition in painted.Transitions!.OfType<LinearBrushTransition>())
                        {
                            Assert.That(transition.Duration, Is.EqualTo(EditorMotion.HoverDuration));
                            Assert.That(transition.Delay, Is.EqualTo(TimeSpan.Zero));
                            Assert.That(transition.Easing, Is.TypeOf<Avalonia.Animation.Easings.LinearEasing>());
                        }
                    Assert.That(button.Transitions?.Count ?? 0, Is.Zero, "The binding source must not animate as well as the painted surface.");
                    Assert.That(toggle.Transitions?.Count ?? 0, Is.Zero);
                    Assert.That(iconPresenter.Transitions?.Count ?? 0, Is.Zero);
                    var before = MotionColor(surface.Background);
                    ((IPseudoClasses)button.Classes).Set(":pointerover", true); Step(0); Step(32);
                    var mid = MotionColor(surface.Background); var target = MotionColor(Resource("SemiColorFill1"));
                    Assert.That(mid, Is.Not.EqualTo(before), $"{variant}: hover must respond before the transition ends.");
                    Assert.That(mid, Is.Not.EqualTo(target));
                    ((IPseudoClasses)button.Classes).Set(":pointerover", false); Step(0);
                    Assert.That(MotionColor(surface.Background), Is.EqualTo(mid), "Reversing hover starts from the rendered color.");
                    Step(40);
                    Assert.That(MotionDistance(MotionColor(surface.Background), before), Is.LessThan(MotionDistance(mid, before)),
                        $"{variant}: before={before}, mid={mid}, reverse={MotionColor(surface.Background)}, target={target}, source={MotionColor(button.Background)}");
                    Step(180); Assert.That(MotionColor(surface.Background), Is.EqualTo(before));

                    var inputBefore = MotionColor(inputSurface.BorderBrush); var inputBackground = MotionColor(inputSurface.Background);
                    ((IPseudoClasses)input.Classes).Set(":pointerover", true); Step(0); Step(32);
                    Assert.That(MotionDistance(MotionColor(inputSurface.BorderBrush), inputBefore) + MotionDistance(MotionColor(inputSurface.Background), inputBackground), Is.GreaterThan(0));
                    Step(180);

                    toggle.IsChecked = true; Step(0); Step(180);
                    Assert.That(MotionColor(toggleSurface.Background), Is.EqualTo(MotionColor(toggle.Background)), "Checked state reaches its target after one duration.");
                    icon.IsChecked = true; Step(0); Step(180);
                    Assert.That(MotionColor(iconSurface.Background), Is.EqualTo(MotionColor(Resource("SemiColorPrimaryLight"))), variant.ToString());
                    ((IPseudoClasses)icon.Classes).Set(":pointerover", true); Step(0); Step(180);
                    Assert.That(MotionColor(iconSurface.Background), Is.EqualTo(MotionColor(Resource("SemiColorFill1"))));
                    ((IPseudoClasses)icon.Classes).Set(":pressed", true); Step(0); Step(180);
                    Assert.That(MotionColor(iconSurface.Background), Is.EqualTo(MotionColor(Resource("SemiColorFill2"))), "Press wins over checked-hover.");
                    ((IPseudoClasses)icon.Classes).Set(":pressed", false); ((IPseudoClasses)icon.Classes).Set(":pointerover", false); Step(0); Step(180);
                    Assert.That(MotionColor(iconSurface.Background), Is.EqualTo(MotionColor(Resource("SemiColorPrimaryLight"))));
                    Assert.That(MotionColor(iconSurface.BorderBrush), Is.EqualTo(MotionColor(Resource("SemiColorPrimary"))));
                    icon.IsEnabled = false; ((IPseudoClasses)icon.Classes).Set(":pointerover", true); ((IPseudoClasses)icon.Classes).Set(":pressed", true); Step(0); Step(180);
                    Assert.That(MotionColor(iconSurface.Background), Is.EqualTo(MotionColor(Resource("SemiColorPrimaryLight"))));
                    Assert.That(icon.Opacity, Is.EqualTo(0.35));
                }
                finally { window.Close(); }
            }
            return true;
        }, CancellationToken.None);
    }

    private static Color MotionColor(IBrush? brush) => brush is ISolidColorBrush solid
        ? Color.FromArgb((byte)Math.Round(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B)
        : Colors.Transparent;
    private static int MotionDistance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) + Math.Abs(a.A - b.A);

    // Avalonia 12 keeps its clock API internal. Pulse the installed framework clock so
    // this verifies actual template transitions without wall-clock sleeps or final-frame screenshots.
    private sealed class MotionTestClock
    {
        private static readonly Type ClockType = typeof(Color).Assembly.GetType("Avalonia.Animation.Clock")!;
        private static readonly PropertyInfo ClockProperty = typeof(Avalonia.Animation.Animatable).GetProperty("Clock", BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly MethodInfo Pulse = ClockType.GetMethod("Pulse", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        private readonly object _clock = Activator.CreateInstance(ClockType, nonPublic: true)!;
        private int _milliseconds;
        public void Attach(Control control) => ClockProperty.SetValue(control, _clock);
        public void Step(int milliseconds) { _milliseconds += milliseconds; Pulse.Invoke(_clock, [TimeSpan.FromMilliseconds(_milliseconds)]); }
    }
}
