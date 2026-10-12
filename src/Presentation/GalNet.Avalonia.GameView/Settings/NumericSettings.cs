namespace GalNet.Avalonia.GameView.Settings;

public interface INumericSetting
{
    double Minimum { get; }
    double Maximum { get; }
    double Step { get; }
    NumericEditorKind EditorKind { get; }
    bool IsSlider { get; }
    bool IsInteger { get; }
    double EditorValue { get; set; }
    decimal NumericValue { get; set; }
    string? Format { get; }
    string? UnitKey { get; }
}

public sealed class IntSetting : SettingItem<int>, INumericSetting
{
    public IntSetting(string id, string nameKey, int defaultValue, int minimum, int maximum, int step,
        Func<int> read, Func<int, CancellationToken, Task> apply, object? label = null, Func<int>? forcedValue = null)
        : base(id, nameKey, defaultValue, read, apply, label, forcedValue)
    {
        if (minimum > maximum || defaultValue < minimum || defaultValue > maximum || step <= 0)
            throw new ArgumentException("Invalid integer setting bounds/default/step.");
        Minimum = minimum; Maximum = maximum; Step = step;
    }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public NumericEditorKind EditorKind { get; init; }
    public bool IsSlider => EditorKind == NumericEditorKind.Slider;
    public bool IsInteger => true;
    public string? Format { get; init; }
    public string? UnitKey { get; init; }
    public double EditorValue
    {
        get => Value;
        set
        {
            if (!double.IsFinite(value) || value < Minimum || value > Maximum || value != Math.Truncate(value))
            { ErrorKey = "Settings.InvalidInteger"; Refresh(); return; }
            Value = checked((int)value);
        }
    }
    public decimal NumericValue { get => Value; set => EditorValue = (double)value; }
    protected override string? Validate(int value) => value < Minimum || value > Maximum ? "Settings.OutOfRange" : base.Validate(value);
    public override void Refresh() { base.Refresh(); OnPropertyChanged(nameof(EditorValue)); OnPropertyChanged(nameof(NumericValue)); }
}

public sealed class DoubleSetting : SettingItem<double>, INumericSetting
{
    public DoubleSetting(string id, string nameKey, double defaultValue, double minimum, double maximum, double step,
        Func<double> read, Func<double, CancellationToken, Task> apply, object? label = null, Func<double>? forcedValue = null)
        : base(id, nameKey, defaultValue, read, apply, label, forcedValue)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum ||
            !double.IsFinite(defaultValue) || defaultValue < minimum || defaultValue > maximum || !double.IsFinite(step) || step <= 0)
            throw new ArgumentException("Invalid numeric setting bounds/default/step.");
        // NumericUpDown's decimal domain must be representable, regardless of selected editor.
        _ = checked((decimal)minimum); _ = checked((decimal)maximum); _ = checked((decimal)step);
        Minimum = minimum; Maximum = maximum; Step = step;
    }
    public double Minimum { get; }
    public double Maximum { get; }
    public double Step { get; }
    public NumericEditorKind EditorKind { get; init; }
    public bool IsSlider => EditorKind == NumericEditorKind.Slider;
    public bool IsInteger => false;
    public string? Format { get; init; }
    public string? UnitKey { get; init; }
    public double EditorValue { get => Value; set => Value = value; }
    public decimal NumericValue { get => checked((decimal)Value); set => Value = (double)value; }
    protected override string? Validate(double value) => !double.IsFinite(value) || value < Minimum || value > Maximum
        ? "Settings.OutOfRange" : base.Validate(value);
    public override void Refresh() { base.Refresh(); OnPropertyChanged(nameof(EditorValue)); OnPropertyChanged(nameof(NumericValue)); }
}

public sealed class BoolSetting(string id, string nameKey, bool defaultValue, Func<bool> read,
    Func<bool, CancellationToken, Task> apply, object? label = null, Func<bool>? forcedValue = null)
    : SettingItem<bool>(id, nameKey, defaultValue, read, apply, label, forcedValue);

public sealed class StringSetting(string id, string nameKey, string defaultValue, Func<string> read,
    Func<string, CancellationToken, Task> apply, object? label = null, Func<string>? forcedValue = null)
    : SettingItem<string>(id, nameKey, defaultValue, read, apply, label, forcedValue);
