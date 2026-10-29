using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>Optional numeric controls. Author text remains the source of truth, independent of UI numeric values.</summary>
public static class NumericFieldEditors
{
    public static FieldEditorLease CreateUpDown(FieldEditorContext context)
    {
        var type = Nullable.GetUnderlyingType(context.Field.Value.ValueType) ?? context.Field.Value.ValueType;
        if (!DefaultFieldEditors.NumericTypes.Contains(type)) throw new NotSupportedException("A numeric field is required.");
        // Never funnel high precision values through a UI decimal/double conversion.
        if (type == typeof(long) || type == typeof(ulong) || type == typeof(decimal) || context.Field.Value.IsStringEncoded ||
            context.EffectiveValue is { ValueKind: JsonValueKind.String })
            return DefaultFieldEditors.CreateNumeric(context);
        if (context.EffectiveValue is { ValueKind: JsonValueKind.Number } current && !current.TryGetDecimal(out _))
        {
            var exact = DefaultFieldEditors.CreateNumeric(context);
            return new(exact.Control, exact.Dispose) { Diagnostic = new("editor.numericFallback", "This value exceeds the step control's decimal range.") };
        }
        var fractional = type == typeof(float) || type == typeof(double);
        var step = context.Field.Editor?.Step ?? (fractional ? .1 : 1);
        if (!double.IsFinite(step) || step <= 0 || !fractional && step != Math.Truncate(step) || step > (double)decimal.MaxValue)
            return ExactFallback(context, "The step must be positive, finite and compatible with the numeric type.");
        if (!decimal.TryParse(step.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var increment) || increment <= 0)
            return ExactFallback(context, "The step cannot be represented by the step control.");
        var control = new DraftNumericUpDown { Increment = increment, NumberFormat = CultureInfo.InvariantCulture.NumberFormat };
        void Spin(bool increase)
        {
            if (!context.IsEnabled || context.IsSubmitting || context.DraftState?.CanCommit == false) return;
            try
            {
                var json = context.Text.Length == 0 ? context.EffectiveValue : ValueCodec.ParseEditorText(context.Field.Value, context.Text);
                if (json is { } value && ValueValidation.Validate(context.Field.Value, value) is { } issue) { context.ReportIssue(issue); return; }
                var current = json is { ValueKind: JsonValueKind.Number } number ? number.GetDecimal() : 0;
                var next = checked(current + (increase ? control.Increment : -control.Increment));
                if (context.Field.Value.TryGetConstraint("minimum", out var min) && min.TryGetDecimal(out var minimum)) next = Math.Max(minimum, next);
                if (context.Field.Value.TryGetConstraint("maximum", out var max) && max.TryGetDecimal(out var maximum)) next = Math.Min(maximum, next);
                context.Text = next.ToString(CultureInfo.InvariantCulture);
                _ = context.CommitTextAsync();
            }
            catch (Exception error) when (error is JsonException or ArgumentException or OverflowException or FormatException)
            { context.ReportIssue(new("value.spin", "This value cannot be adjusted with the step buttons. Enter an exact number instead.")); }
        }
        control.StepRequested = Spin;
        control.TextEdited = text => context.Text = text;
        var lease = DefaultFieldEditors.Bind(control, context, () => control.SetDraftText(context.Text), _ => { }, commitText: true);
        return new(control, () => { control.ReleaseEditor(); lease.Dispose(); });
    }

    private static FieldEditorLease ExactFallback(FieldEditorContext context, string reason)
    {
        var exact = DefaultFieldEditors.CreateNumeric(context);
        return new(exact.Control, exact.Dispose) { Diagnostic = new("editor.numericFallback", reason) };
    }

    /// <summary>Editor path with exact typed input beside the bounded coarse adjustment.</summary>
    public static FieldEditorLease CreateSliderWithInput(FieldEditorContext context)
    {
        if (context.EffectiveValue is { } value && ValueValidation.Validate(context.Field.Value, value) is not null)
            return CreateUpDown(context);
        FieldEditorLease sliderLease;
        try { sliderLease = DefaultFieldEditors.CreateSlider(context); }
        catch (Exception error) when (error is NotSupportedException or ArgumentException or OverflowException or InvalidOperationException)
        {
            var input = DefaultFieldEditors.CreateNumeric(context);
            return new(input.Control, input.Dispose) { Diagnostic = new("editor.numericFallback", error.Message) };
        }
        var slider = (Slider)sliderLease.Control;
        var number = CreateUpDown(context);
        var panel = new Grid { ColumnDefinitions = new("*,140"), ColumnSpacing = 8 };
        panel.Children.Add(slider); Grid.SetColumn(number.Control, 1); panel.Children.Add(number.Control);
        return new(panel, () => { sliderLease.Dispose(); number.Dispose(); });
    }

    // Native NumericUpDown normally reparses/reformats on every edit and on blur. Keep the
    // native spinner/template/keyboard behavior, but let the field contract own text and commits.
    private sealed class DraftNumericUpDown : NumericUpDown
    {
        private TextBox? _input;
        private ButtonSpinner? _spinner;
        private string _draftText = "";
        private bool _syncing;
        protected override Type StyleKeyOverride => typeof(NumericUpDown);
        public Action<bool>? StepRequested { get; set; }
        public Action<string>? TextEdited { get; set; }
        public DraftNumericUpDown() => AddHandler(KeyDownEvent, StepKey, RoutingStrategies.Tunnel);
        public void SetDraftText(string text)
        {
            _syncing = true;
            try { _draftText = text; Text = text; if (_input is not null && _input.Text != text) _input.Text = text; }
            finally { _syncing = false; }
        }
        protected override void OnTextChanged(string? oldValue, string? newValue) { }
        protected override void OnValueChanged(decimal? oldValue, decimal? newValue) { }
        protected override void OnLostFocus(FocusChangedEventArgs e) { }
        protected override void OnKeyDown(KeyEventArgs e) { }
        private void StepKey(object? sender, KeyEventArgs e)
        {
            if (!IsEnabled || IsReadOnly || !AllowSpin || e.Key is not (Key.Up or Key.Down) || e.KeyModifiers != KeyModifiers.None) return;
            StepRequested?.Invoke(e.Key == Key.Up); e.Handled = true;
        }
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            if (!IsEnabled || IsReadOnly || !AllowSpin || _input?.IsFocused != true || e.Delta.Y == 0) return;
            StepRequested?.Invoke(e.Delta.Y > 0); e.Handled = true;
        }
        protected override void OnSpin(SpinEventArgs e)
        {
            if (IsEnabled && !IsReadOnly && AllowSpin) StepRequested?.Invoke(e.Direction == SpinDirection.Increase);
            e.Handled = true;
        }
        protected override void OnGotFocus(FocusChangedEventArgs e) { if (ReferenceEquals(e.Source, this)) _input?.Focus(); }
        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            // Deliberately own the template parts instead of attaching NumericUpDown's eager
            // Value parser: its initial/null conversions must not create author drafts.
            DisconnectParts();
            _input = e.NameScope.Find<TextBox>("PART_TextBox");
            _spinner = e.NameScope.Find<ButtonSpinner>("PART_Spinner");
            SetDraftText(_draftText);
            if (_input is not null) _input.PropertyChanged += Edited;
            if (_spinner is not null)
            {
                _spinner.ValidSpinDirection = ValidSpinDirections.Increase | ValidSpinDirections.Decrease;
                _spinner.Spin += Spin;
                _spinner.ApplyTemplate();
                foreach (var button in _spinner.GetVisualDescendants().OfType<RepeatButton>()) { button.Delay = 250; button.Interval = 30; }
            }
        }
        private void Edited(object? sender, Avalonia.AvaloniaPropertyChangedEventArgs e)
        { if (!_syncing && e.Property == TextBox.TextProperty && _input is not null) TextEdited?.Invoke(_input.Text ?? ""); }
        private void Spin(object? sender, SpinEventArgs e) => OnSpin(e);
        private void DisconnectParts()
        {
            if (_input is not null) _input.PropertyChanged -= Edited;
            if (_spinner is not null) _spinner.Spin -= Spin;
            _input = null; _spinner = null;
        }
        public void ReleaseEditor() { DisconnectParts(); StepRequested = null; TextEdited = null; RemoveHandler(KeyDownEvent, StepKey); }
    }
}
