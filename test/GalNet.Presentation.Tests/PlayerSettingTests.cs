using GalNet.Avalonia.GameView.Settings;

namespace GeneralTest.Presentation;

public class PlayerSettingTests
{
    [Test]
    public async Task FlushRetriesPendingPersistenceWithoutReapplyingTheValueAndReportsFailure()
    {
        var value = false;
        var applies = 0;
        var saves = 0;
        var canSave = false;
        var setting = new BoolSetting("bool", "Bool", false, () => value,
            (next, _) => { applies++; value = next; return Task.CompletedTask; });
        var coordinator = new SettingsCoordinator
        {
            SaveAsync = _ => { saves++; return canSave ? Task.CompletedTask : Task.FromException(new IOException("disk")); }
        };
        coordinator.Attach([new("game", "Game", [setting])]);
        Assert.That(await setting.TrySetValueAsync(true), Is.False);
        Assert.ThrowsAsync<IOException>(async () => await coordinator.FlushAsync());
        canSave = true;
        await coordinator.FlushAsync();
        await coordinator.FlushAsync();
        Assert.That(applies, Is.EqualTo(1));
        Assert.That(saves, Is.EqualTo(3));
        Assert.That(value, Is.True);
    }

    [Test]
    public async Task RetryingAnAppliedValueRetriesSaveWithoutReapplyingIt()
    {
        var value = false;
        var applies = 0;
        var saves = 0;
        var setting = new BoolSetting("bool", "Bool", false, () => value,
            (next, _) => { applies++; value = next; return Task.CompletedTask; });
        var coordinator = new SettingsCoordinator
        {
            SaveAsync = _ => ++saves == 1 ? Task.FromException(new IOException("disk")) : Task.CompletedTask
        };
        coordinator.Attach([new("game", "Game", [setting])]);
        Assert.That(await setting.TrySetValueAsync(true), Is.False);
        Assert.That(await setting.TrySetValueAsync(true), Is.True);
        Assert.That(applies, Is.EqualTo(1));
        Assert.That(saves, Is.EqualTo(2));
        Assert.That(setting.ErrorKey, Is.Null);
    }

    [Test]
    public async Task ChoiceSystemFallbackAppliesEvenWhenPlayerEditorIsHiddenOrDisabled()
    {
        var value = "removed";
        var setting = new ChoiceSetting<string>("choice", "Choice", "default",
            [new("default", "Default", "default"), new("old", "Old", "removed")],
            () => value, (next, _) => { value = next; return Task.CompletedTask; })
        { EvaluateAvailability = () => new(false, false, "Settings.Unavailable") };
        Assert.That(await setting.UpdateOptionsAsync([new("default", "Default", "default")]), Is.True);
        Assert.That(value, Is.EqualTo("default"));
        Assert.That(setting.IsVisible, Is.False);
        Assert.That(setting.IsEnabled, Is.False);
    }

    [Test]
    public async Task ApplyFailureLeavesActualValueAndDoesNotPoisonSubsequentEdits()
    {
        var value = 0;
        var saves = 0;
        var setting = new IntSetting("number", "Number", 0, 0, 10, 1, () => value,
            (next, _) =>
            {
                if (next == 1) throw new InvalidOperationException("unsupported");
                value = next;
                return Task.CompletedTask;
            });
        var coordinator = new SettingsCoordinator { SaveAsync = _ => { saves++; return Task.CompletedTask; } };
        coordinator.Attach([new("game", "Game", [setting])]);
        Assert.That(await setting.TrySetValueAsync(1), Is.False);
        Assert.That(value, Is.Zero);
        Assert.That(setting.ErrorKey, Is.EqualTo("Settings.ApplyFailed"));
        Assert.That(await setting.TrySetValueAsync(2), Is.True);
        Assert.That(value, Is.EqualTo(2));
        Assert.That(saves, Is.EqualTo(1));
    }

    [Test]
    public async Task ActionUsesTheSameAvailabilityAndErrorContract()
    {
        var available = false;
        var calls = 0;
        var setting = new ActionSetting("action", "Action", _ => { calls++; return Task.CompletedTask; })
        { EvaluateAvailability = () => new(true, available, "Settings.Unavailable") };
        Assert.That(await setting.ExecuteAsync(), Is.False);
        available = true;
        Assert.That(await setting.ExecuteAsync(), Is.True);
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public async Task StepDoesNotRestrictLegalNumericValues()
    {
        var value = 0d;
        var setting = new DoubleSetting("interval", "Settings.Interval", 0, 0, 10, .5,
            () => value, (next, _) => { value = next; return Task.CompletedTask; });
        Assert.That(await setting.TrySetValueAsync(.13), Is.True);
        Assert.That(value, Is.EqualTo(.13));
        Assert.That(await setting.TrySetValueAsync(double.NaN), Is.False);
        Assert.That(value, Is.EqualTo(.13));
    }

    [Test]
    public async Task ChoiceFallsBackToValidDefaultAndSavesIt()
    {
        var value = "removed";
        var saves = 0;
        var setting = new ChoiceSetting<string>("choice", "Settings.Choice", "default",
            [new("default", "Default", "default"), new("old", "Old", "removed")],
            () => value, (next, _) => { value = next; return Task.CompletedTask; });
        var coordinator = new SettingsCoordinator { SaveAsync = _ => { saves++; return Task.CompletedTask; } };
        coordinator.Attach([new("game", "Settings.Game", [setting])]);
        Assert.That(await setting.UpdateOptionsAsync([new("default", "Default", "default")]), Is.True);
        Assert.That(value, Is.EqualTo("default"));
        Assert.That(saves, Is.EqualTo(1));
        Assert.That(await setting.UpdateOptionsAsync([new("other", "Other", "other")]), Is.False);
        Assert.That(setting.IsEnabled, Is.False);
        Assert.That(value, Is.EqualTo("default"), "Invalid default does not pick the first candidate.");
    }

    [Test]
    public async Task SuccessfulEditRefreshesDependentAvailability()
    {
        var enabled = false;
        var value = 0;
        var toggle = new BoolSetting("toggle", "Toggle", false, () => enabled,
            (next, _) => { enabled = next; return Task.CompletedTask; });
        var dependent = new IntSetting("dependent", "Dependent", 0, 0, 10, 3, () => value,
            (next, _) => { value = next; return Task.CompletedTask; })
        { EvaluateAvailability = () => new(true, enabled, enabled ? null : "Settings.ToggleRequired") };
        var coordinator = new SettingsCoordinator();
        coordinator.Attach([new("game", "Game", [toggle, dependent])]);
        Assert.That(await dependent.TrySetValueAsync(4), Is.False);
        Assert.That(await toggle.TrySetValueAsync(true), Is.True);
        Assert.That(dependent.IsEnabled, Is.True);
        Assert.That(await dependent.TrySetValueAsync(4), Is.True, "Step=3 does not invalidate integer value 4.");
        Assert.That(value, Is.EqualTo(4));
    }

    [Test]
    public async Task SaveFailureDoesNotPretendTheAppliedValueWasRolledBack()
    {
        var value = false;
        var setting = new BoolSetting("bool", "Bool", false, () => value,
            (next, _) => { value = next; return Task.CompletedTask; });
        var coordinator = new SettingsCoordinator { SaveAsync = _ => Task.FromException(new IOException("disk")) };
        coordinator.Attach([new("game", "Game", [setting])]);
        Assert.That(await setting.TrySetValueAsync(true), Is.False);
        Assert.That(value, Is.True);
        Assert.That(setting.ErrorKey, Is.EqualTo("Settings.SaveFailed"));
        Assert.That(setting.IsApplying, Is.False);
    }

    [Test]
    public async Task ForcedValueIsIndependentOfHidingTheSetting()
    {
        var setting = new BoolSetting("bool", "Bool", false, () => false,
            (_, _) => Task.CompletedTask, forcedValue: () => true);
        Assert.That(setting.Value, Is.True);
        Assert.That(setting.IsEnabled, Is.False);
        Assert.That(await setting.TrySetValueAsync(false), Is.False);
    }
}
