using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Avalonia.Themes;
using GalNet.Avalonia.Themes.Common;
using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GeneralTest.Presentation;

[NonParallelizable]
public sealed class SettingSchemaFormTests
{
    [Test]
    public async Task NumericDraftValidatesThenUsesTheApplyAndRetrySaveQueue()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var value = 50;
            var applies = 0;
            var saves = 0;
            var saveFails = true;
            var setting = new IntSetting("interval", "Interval", 50, 0, 5000, 10, () => value,
                (next, _) => { applies++; value = next; return Task.CompletedTask; });
            var categories = new[] { new SettingCategory("game", "Game", [setting]) };
            var coordinator = new SettingsCoordinator
            {
                SaveAsync = _ =>
            {
                saves++;
                return saveFails ? Task.FromException(new IOException("disk")) : Task.CompletedTask;
            }
            };
            coordinator.Attach(categories);
            var view = new SettingsCategories { Categories = categories, Editors = EditorRegistry.CreateDefault() };
            var window = Mount(view);
            try
            {
                var form = view.GetVisualDescendants().OfType<SchemaForm>().Single();
                var input = form.Editors[0].Control as TextBox;
                Assert.That(input, Is.Not.Null);
                var context = form.Contexts.Single();
                input!.Text = "1.5";
                input.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
                Assert.That(context.Issue, Is.Not.Null);
                Assert.That(value, Is.EqualTo(50));
                Assert.That(applies, Is.Zero);
                input.Text = "123";
                Assert.That(await context.CommitTextAsync(), Is.False);
                Assert.That(value, Is.EqualTo(123));
                Assert.That(setting.ErrorKey, Is.EqualTo("Settings.SaveFailed"));
                saveFails = false;
                Assert.That(await context.CommitTextAsync(), Is.True);
                Assert.That(applies, Is.EqualTo(1));
                Assert.That(saves, Is.EqualTo(2));
                Assert.That(setting.ErrorKey, Is.Null);
                value = 234;
                setting.Refresh();
                Assert.That(input.Text, Is.EqualTo("234"));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task TypedChoicesRefreshWithoutRecreationAndForcedValuesCannotBeEdited()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var value = DayOfWeek.Monday;
            var choice = new ChoiceSetting<DayOfWeek>("day", "Day", DayOfWeek.Monday,
                [new("mon", "Monday", DayOfWeek.Monday), new("tue", "Tuesday", DayOfWeek.Tuesday)], () => value,
                (next, _) => { value = next; return Task.CompletedTask; });
            var forced = new BoolSetting("flag", "Flag", false, () => false, (_, _) => throw new InvalidOperationException(), forcedValue: () => true);
            var categories = new[] { new SettingCategory("game", "Game", [choice, forced]) };
            var coordinator = new SettingsCoordinator();
            coordinator.Attach(categories);
            var view = new SettingsCategories { Categories = categories, Editors = EditorRegistry.CreateDefault() };
            var window = Mount(view);
            try
            {
                var forms = view.GetVisualDescendants().OfType<SchemaForm>().ToArray();
                var form = forms.Single(item => item.Contexts.Single().Field.Id == "day");
                var combo = (ComboBox)form.Editors[0].Control;
                combo.SelectedItem = form.Contexts[0].Options[1];
                await coordinator.FlushAsync();
                Assert.That(value, Is.EqualTo(DayOfWeek.Tuesday));
                Assert.That(await choice.UpdateOptionsAsync([new("mon", "Renamed", DayOfWeek.Monday), new("wed", "Wednesday", DayOfWeek.Wednesday)]), Is.True);
                Assert.That(value, Is.EqualTo(DayOfWeek.Monday));
                Assert.That(form.Editors[0].Control, Is.SameAs(combo));
                Assert.That(form.Contexts[0].Options[0].Label, Is.EqualTo("Renamed"));
                var check = (CheckBox)forms.Single(item => item.Contexts.Single().Field.Id == "flag").Editors[0].Control;
                Assert.That(check.IsChecked, Is.True);
                Assert.That(check.IsEnabled, Is.False);
                Assert.That(await ((IFieldBinding)forced).CommitAsync(new(JsonSerializer.SerializeToElement(false)), CancellationToken.None), Is.Not.Null);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task DetachCancelsPendingApplicationAndDoesNotReuseTheOldBinding()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ThemeTestApplication).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            var started = new TaskCompletionSource();
            var value = "before";
            CancellationToken token = default;
            var setting = new StringSetting("text", "Text", "before", () => value, async (next, cancellation) =>
            {
                token = cancellation;
                started.SetResult();
                await Task.Delay(Timeout.Infinite, cancellation);
                value = next;
            });
            var view = new SettingsCategories { Editors = EditorRegistry.CreateDefault(), Categories = [new("game", "Game", [setting])] };
            var window = Mount(view);
            var context = view.GetVisualDescendants().OfType<SchemaForm>().Single().Contexts.Single();
            context.Text = "pending";
            var edit = context.CommitTextAsync();
            await started.Task;
            window.Content = null;
            Assert.That(await edit, Is.False);
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(value, Is.EqualTo("before"));
            setting.Refresh();
            Assert.That(await context.CommitTextAsync(), Is.False);
            window.Close();
            return true;
        }, CancellationToken.None);
    }

    private static Window Mount(SettingsCategories view)
    {
        var root = new Border { Classes = { "galnet-player" }, Child = view };
        root.Styles.Add(new ClassicTheme().CreateStyles());
        var window = new Window { Width = 900, Height = 600, Content = root };
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
