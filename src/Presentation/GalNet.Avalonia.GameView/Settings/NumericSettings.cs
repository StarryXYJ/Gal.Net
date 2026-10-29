using GalNet.Core.Parameters;
using GalNet.Core.Serialization;

namespace GalNet.Avalonia.GameView.Settings;

public sealed class IntSetting : SettingItem<int>
{
    public IntSetting(string id, string nameKey, int defaultValue, int minimum, int maximum, int step,
        Func<int> read, Func<int, CancellationToken, Task> apply, object? label = null, Func<int>? forcedValue = null)
        : base(id, nameKey, defaultValue, read, apply, label, forcedValue)
    {
        ValueContract = new(typeof(int), constraints: JsonCodec.SerializeToElement(new Dictionary<string, int>
        { ["minimum"] = minimum, ["maximum"] = maximum }));
        if (ValueValidation.ValidateValue(ValueContract, defaultValue) is not null || step <= 0)
            throw new ArgumentException("Invalid integer setting bounds/default/step.");
        Minimum = minimum; Maximum = maximum; Step = step;
    }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public NumericEditorKind EditorKind { get; init; }
    public string? Format { get; init; }
    public string? UnitKey { get; init; }
    protected override EditorHints GetEditorHints() => Editor ?? new(
        EditorKind == NumericEditorKind.Slider ? ValueEditorIds.Slider : ValueEditorIds.Numeric, Step, Format, UnitKey);
    protected override string? Validate(int value) => ValueValidation.ValidateValue(ValueContract, value) is not null ? "Settings.OutOfRange" : base.Validate(value);
}

public sealed class DoubleSetting : SettingItem<double>
{
    public DoubleSetting(string id, string nameKey, double defaultValue, double minimum, double maximum, double step,
        Func<double> read, Func<double, CancellationToken, Task> apply, object? label = null, Func<double>? forcedValue = null)
        : base(id, nameKey, defaultValue, read, apply, label, forcedValue)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || !double.IsFinite(step) || step <= 0)
            throw new ArgumentException("Invalid numeric setting bounds/default/step.");
        ValueContract = new(typeof(double), constraints: JsonCodec.SerializeToElement(new Dictionary<string, double>
        { ["minimum"] = minimum, ["maximum"] = maximum }));
        if (ValueValidation.ValidateValue(ValueContract, defaultValue) is not null)
            throw new ArgumentException("Invalid numeric setting default.");
        Minimum = minimum; Maximum = maximum; Step = step;
    }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public NumericEditorKind EditorKind { get; init; }
    public string? Format { get; init; }
    public string? UnitKey { get; init; }
    protected override EditorHints GetEditorHints() => Editor ?? new(
        EditorKind == NumericEditorKind.Slider ? ValueEditorIds.Slider : ValueEditorIds.Numeric, Step, Format, UnitKey);
    protected override string? Validate(double value) => ValueValidation.ValidateValue(ValueContract, value) is not null
        ? "Settings.OutOfRange" : base.Validate(value);
}

public sealed class BoolSetting(string id, string nameKey, bool defaultValue, Func<bool> read,
    Func<bool, CancellationToken, Task> apply, object? label = null, Func<bool>? forcedValue = null)
    : SettingItem<bool>(id, nameKey, defaultValue, read, apply, label, forcedValue);

public sealed class StringSetting(string id, string nameKey, string defaultValue, Func<string> read,
    Func<string, CancellationToken, Task> apply, object? label = null, Func<string>? forcedValue = null)
    : SettingItem<string>(id, nameKey, defaultValue, read, apply, label, forcedValue);
