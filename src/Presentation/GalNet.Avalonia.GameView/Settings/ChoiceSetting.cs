using GalNet.Core.Parameters;
using GalNet.Game.Controls.Forms;

namespace GalNet.Avalonia.GameView.Settings;

public sealed record SettingChoice<T>(string Id, string NameKey, T Value, object? Label = null);
public sealed class ChoiceSetting<T> : SettingItem<T>
{
    private IReadOnlyList<SettingChoice<T>> _choices = [];
    public ChoiceSetting(string id, string nameKey, T defaultValue, IEnumerable<SettingChoice<T>> choices,
        Func<T> read, Func<T, CancellationToken, Task> apply, object? label = null, Func<T>? forcedValue = null)
        : base(id, nameKey, defaultValue, read, apply, label, forcedValue)
    {
        SetOptions(choices);
    }
    public IReadOnlyList<SettingChoice<T>> Choices => _choices;
    protected override EditorHints GetEditorHints() => Editor ?? new(ValueEditorIds.Choice);
    protected override IReadOnlyList<FieldEditorOption> FieldOptions => Array.AsReadOnly(_choices.Select(choice =>
        new FieldEditorOption(choice.Id, choice.Label ?? choice.NameKey, ValueCodec.Serialize(ValueContract, choice.Value))).ToArray());
    public Task<bool> UpdateOptionsAsync(IEnumerable<SettingChoice<T>> choices, CancellationToken cancellationToken = default)
    {
        var snapshot = choices.ToArray();
        return Coordinator.EnqueueAsync(async () =>
        {
            SetOptions(snapshot);
            RefreshAvailability();
            Refresh();
            if (!_choices.Any(choice => EqualityComparer<T>.Default.Equals(choice.Value, DefaultValue)))
            {
                ErrorKey = "Settings.InvalidDefault";
                return false;
            }
            if (_choices.Any(choice => EqualityComparer<T>.Default.Equals(choice.Value, Value)))
            {
                ErrorKey = null;
                return true;
            }
            return await ApplyValueAsync(DefaultValue, cancellationToken, systemChange: true);
        }, cancellationToken);
    }
    private void SetOptions(IEnumerable<SettingChoice<T>> choices)
    {
        var snapshot = choices.ToArray();
        if (snapshot.Any(choice => string.IsNullOrWhiteSpace(choice.Id)) || snapshot.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("Choice IDs must be non-empty and unique.", nameof(choices));
        if (snapshot.Select(choice => choice.Value).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Choice values must be unique.", nameof(choices));
        _choices = Array.AsReadOnly(snapshot);
        OnPropertyChanged(nameof(Choices));
    }
    protected override SettingAvailability EvaluateState()
    {
        var availability = base.EvaluateState();
        return !_choices.Any(choice => EqualityComparer<T>.Default.Equals(choice.Value, DefaultValue))
            ? availability with { IsEnabled = false, ReasonKey = "Settings.InvalidDefault" } : availability;
    }
    protected override string? Validate(T value) => !_choices.Any(choice => EqualityComparer<T>.Default.Equals(choice.Value, value))
        ? "Settings.InvalidChoice" : base.Validate(value);
}
