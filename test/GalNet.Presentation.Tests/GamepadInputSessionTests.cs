using GalNet.Avalonia.GameView.Input;
using GalNet.Presentation.Abstractions.Input;

namespace GeneralTest.Presentation;

public sealed class GamepadInputSessionTests
{
    private static StandardGamepadSnapshot Pad(string id = "one", StandardGamepadButtons buttons = StandardGamepadButtons.None) =>
        new(id, id, GamepadLayout.Standard, GamepadFaceLabels.Unknown, buttons);

    [Test]
    public void PublishesPressAndReleaseOnceAndDoesNotMergeAnotherController()
    {
        using var input = new PlayerInputSettings();
        var session = new GamepadInputSession(input);
        var changes = new List<string>();
        input.GamepadChanged += buttons => changes.Add(string.Join('+', buttons.Order()));
        session.Update([Pad(), Pad("two", StandardGamepadButtons.East)], true);
        session.Update([Pad(buttons: StandardGamepadButtons.South), Pad("two", StandardGamepadButtons.East)], true);
        session.Update([Pad(buttons: StandardGamepadButtons.South)], true);
        session.Update([Pad()], true);
        Assert.That(changes, Is.EqualTo(new[] { "South", "" }));
        Assert.That(input.LastDevice, Is.EqualTo(PlayerInputDevice.Gamepad));
    }

    [Test]
    public void FocusLossCancelsHeldInputAndRegainRequiresNeutralBeforeNewPress()
    {
        using var input = new PlayerInputSettings();
        var session = new GamepadInputSession(input);
        var cancelled = 0;
        var changes = new List<string>();
        input.GamepadCancelled += () => cancelled++;
        input.GamepadChanged += buttons => changes.Add(string.Join('+', buttons));
        session.Update([Pad()], true);
        session.Update([Pad(buttons: StandardGamepadButtons.West)], true);
        var before = cancelled;
        session.Update([Pad(buttons: StandardGamepadButtons.West)], false);
        session.Update([Pad(buttons: StandardGamepadButtons.West)], false);
        session.Update([Pad(buttons: StandardGamepadButtons.West)], true);
        Assert.That(cancelled, Is.EqualTo(before + 1));
        Assert.That(input.GamepadButtons, Is.Empty);
        Assert.That(changes, Is.EqualTo(new[] { "West" }));
        session.Update([Pad()], true);
        session.Update([Pad(buttons: StandardGamepadButtons.South)], true);
        Assert.That(changes, Is.EqualTo(new[] { "West", "South" }));
    }

    [Test]
    public void SelectionSurvivesEnumerationReorderAndDisconnectCancelsWithoutSyntheticRelease()
    {
        using var input = new PlayerInputSettings();
        var session = new GamepadInputSession(input);
        var changes = new List<string>();
        input.GamepadChanged += buttons => changes.Add(string.Join('+', buttons));
        session.Update([Pad(), Pad("two")], true);
        session.Update([Pad("two", StandardGamepadButtons.East), Pad(buttons: StandardGamepadButtons.West)], true);
        Assert.That(input.GamepadButtons, Is.EquivalentTo(new[] { "West" }));
        session.Update([Pad("two", StandardGamepadButtons.East)], true);
        Assert.That(input.GamepadButtons, Is.Empty);
        Assert.That(changes, Is.EqualTo(new[] { "West" }));
        session.Update([Pad("two")], true);
        session.Update([Pad("two", StandardGamepadButtons.South)], true);
        Assert.That(input.GamepadButtons, Is.EquivalentTo(new[] { "South" }));
    }

    [Test]
    public void NewlyConnectedHeldDeviceMustReturnToNeutral()
    {
        using var input = new PlayerInputSettings();
        var session = new GamepadInputSession(input);
        session.Update([Pad(buttons: StandardGamepadButtons.South)], true);
        session.Update([Pad(buttons: StandardGamepadButtons.South)], true);
        Assert.That(input.GamepadButtons, Is.Empty);
        session.Update([Pad()], true);
        session.Update([Pad(buttons: StandardGamepadButtons.South)], true);
        Assert.That(input.GamepadButtons, Is.EquivalentTo(new[] { "South" }));
    }

    [Test]
    public void StickAndTriggerHysteresisSuppressNoiseAndUseDownwardY()
    {
        using var input = new PlayerInputSettings();
        var session = new GamepadInputSession(input);
        var pad = Pad();
        session.Update([pad], true);
        session.Update([pad with { LeftX = .49f, LeftTrigger = .49f }], true);
        Assert.That(input.GamepadButtons, Is.Empty);
        session.Update([pad with { LeftX = .6f, LeftY = -.6f, LeftTrigger = .6f }], true);
        Assert.That(input.GamepadButtons, Is.EquivalentTo(new[] { "Right", "Up", "LT" }));
        session.Update([pad with { LeftX = .4f, LeftY = -.4f, LeftTrigger = .45f }], true);
        Assert.That(input.GamepadButtons, Is.EquivalentTo(new[] { "Right", "Up", "LT" }));
        session.Update([pad with { LeftX = .3f, LeftY = -.3f, LeftTrigger = .3f }], true);
        Assert.That(input.GamepadButtons, Is.Empty);
        session.Update([pad with { LeftY = .8f, RightTrigger = .8f }], true);
        Assert.That(input.GamepadButtons, Is.EquivalentTo(new[] { "Down", "RT" }));
    }

    [Test]
    public void NonFiniteStickCannotArmAConnection()
    {
        using var input = new PlayerInputSettings();
        var session = new GamepadInputSession(input);
        session.Update([Pad() with { LeftX = float.NaN }], true);
        session.Update([Pad(buttons: StandardGamepadButtons.South)], true);
        Assert.That(input.GamepadButtons, Is.Empty);
        session.Update([Pad()], true);
        session.Update([Pad(buttons: StandardGamepadButtons.South)], true);
        Assert.That(input.GamepadButtons, Is.EquivalentTo(new[] { "South" }));
    }

    [TestCase(GamepadButtonLabel.A, "A (South)")]
    [TestCase(GamepadButtonLabel.B, "B (South)")]
    [TestCase(GamepadButtonLabel.Cross, "Cross (South)")]
    public void DeviceLabelsDoNotRewritePersistedPositionalBindings(GamepadButtonLabel label, string text)
    {
        using var input = new PlayerInputSettings();
        var before = input.Serialize();
        var session = new GamepadInputSession(input);
        session.Update([Pad() with { Labels = GamepadFaceLabels.Unknown with { South = label } }], true);
        Assert.That(input.FormatGamepadGesture("South"), Is.EqualTo(text));
        Assert.That(input.Serialize(), Is.EqualTo(before));
        session.Reset();
        Assert.That(input.GamepadDeviceName, Is.Null);
        Assert.That(input.FormatGamepadGesture("South"), Is.EqualTo("South"));
    }

    [Test]
    public void GuideAndRightStickAxesDoNotBecomeStoryButtons()
    {
        using var input = new PlayerInputSettings();
        var session = new GamepadInputSession(input);
        session.Update([Pad()], true);
        session.Update([Pad(buttons: StandardGamepadButtons.Guide) with { RightX = 1, RightY = -1 }], true);
        Assert.That(input.GamepadButtons, Is.Empty);
    }
}
