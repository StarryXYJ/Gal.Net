using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using GalNet.Core.Parameters;

namespace GalNet.Game.Controls.Forms;

/// <summary>Behavior-only controls. Host themes supply their appearance and may replace any factory.</summary>
public static class DefaultFieldEditors
{
    internal static readonly Type[] NumericTypes = [typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal)];

    public static FieldEditorLease CreateText(FieldEditorContext context)
    {
        Require(context, typeof(string));
        return TextInput(context);
    }

    // Text preserves incomplete input and large integers without passing through double/decimal UI values.
    public static FieldEditorLease CreateNumeric(FieldEditorContext context)
    {
        Require(context, NumericTypes);
        return TextInput(context);
    }

    public static FieldEditorLease CreateJson(FieldEditorContext context)
    {
        context.UseJsonText();
        var lease = TextInput(context);
        var input = (TextBox)lease.Control;
        input.Tag = "schema.json";
        input.AcceptsReturn = true;
        input.TextWrapping = global::Avalonia.Media.TextWrapping.Wrap;
        input.MinHeight = 90;
        input.PlaceholderText = "JSON";
        ToolTip.SetTip(input, "JSON · " + context.Field.Value.ValueType.Name);
        return lease;
    }

    private static FieldEditorLease TextInput(FieldEditorContext context)
    {
        var control = new TextBox();
        return Bind(control, context, () => control.Text = context.Text,
            change => { if (change.Property == TextBox.TextProperty) context.Text = control.Text ?? ""; }, commitText: true);
    }

    public static FieldEditorLease CreateToggle(FieldEditorContext context)
    {
        Require(context, typeof(bool));
        if (context.EffectiveValue is { } json && ValueValidation.Validate(context.Field.Value, json) is not null) return CreateJson(context);
        var control = new CheckBox { IsThreeState = context.Field.Value.AllowNull };
        return Bind(control, context,
            () => control.IsChecked = (bool?)context.ReadValue(),
            change =>
            {
                if (change.Property == CheckBox.IsCheckedProperty)
                    _ = context.CommitValueAsync(control.IsChecked);
            });
    }

    public static FieldEditorLease CreateChoice(FieldEditorContext context)
    {
        var control = new ComboBox
        {
            ItemTemplate = new FuncDataTemplate<FieldEditorOption>((option, _) => new ContentControl { Content = option?.Label })
        };
        return Bind(control, context, () =>
        {
            control.ItemsSource = context.Options;
            control.SelectedItem = context.EffectiveValue is { } value
                ? context.Options.FirstOrDefault(option => JsonElement.DeepEquals(option.Value, value)) : null;
        }, change =>
        {
            if (change.Property == ComboBox.SelectedItemProperty && control.SelectedItem is FieldEditorOption option)
                _ = context.CommitAsync(new(option.Value));
        });
    }

    public static FieldEditorLease CreateSlider(FieldEditorContext context)
    {
        Require(context, NumericTypes.Where(type => type != typeof(decimal)).ToArray());
        if (context.EffectiveValue is { } json && ValueValidation.Validate(context.Field.Value, json) is not null) return CreateJson(context);
        var value = context.Field.Value;
        if (!value.TryGetConstraint("minimum", out var min) || !value.TryGetConstraint("maximum", out var max))
            throw new NotSupportedException("Slider requires explicit finite minimum and maximum.");
        var minimum = min.GetDouble();
        var maximum = max.GetDouble();
        var type = Nullable.GetUnderlyingType(value.ValueType) ?? value.ValueType;
        var integer = type != typeof(float) && type != typeof(double);
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) ||
            integer && (minimum < -9007199254740991d || maximum > 9007199254740991d))
            throw new NotSupportedException("Slider bounds cannot be represented exactly; use the numeric text editor.");
        var step = context.Field.Editor?.Step ?? (integer ? 1 : .01);
        if (!double.IsFinite(step) || step <= 0) throw new ArgumentException("Slider step must be finite and positive.");
        if (integer && (minimum != Math.Truncate(minimum) || maximum != Math.Truncate(maximum) || step != Math.Truncate(step)))
            throw new NotSupportedException("Integer sliders require integer bounds and step.");
        try { _ = Convert.ChangeType(minimum, type, CultureInfo.InvariantCulture); _ = Convert.ChangeType(maximum, type, CultureInfo.InvariantCulture); }
        catch (OverflowException exception) { throw new NotSupportedException("Slider bounds exceed the CLR value range.", exception); }
        var control = new Slider { Minimum = minimum, Maximum = maximum, TickFrequency = step, IsSnapToTickEnabled = true };
        return Bind(control, context,
            () => control.Value = context.ReadValue() is { } current ? Convert.ToDouble(current, CultureInfo.InvariantCulture) : minimum,
            change =>
            {
                if (change.Property == Slider.ValueProperty)
                {
                    if (integer && control.Value != Math.Truncate(control.Value))
                    {
                        context.Text = control.Value.ToString("R", CultureInfo.InvariantCulture);
                        _ = context.CommitTextAsync();
                    }
                    else _ = context.CommitSliderValueAsync(Convert.ChangeType(control.Value, type, CultureInfo.InvariantCulture));
                }
            });
    }

    private static void Require(FieldEditorContext context, params Type[] types)
    {
        var type = Nullable.GetUnderlyingType(context.Field.Value.ValueType) ?? context.Field.Value.ValueType;
        if (!types.Contains(type)) throw new NotSupportedException($"Editor does not support '{type.Name}'.");
    }

    private static FieldEditorLease Bind(Control control, FieldEditorContext context, Action update,
        Action<AvaloniaPropertyChangedEventArgs> edit, bool commitText = false)
    {
        var syncing = false;
        void Refresh()
        {
            syncing = true;
            try { update(); control.IsEnabled = context.IsEnabled; control.IsVisible = context.IsVisible; }
            finally { syncing = false; }
        }
        void Changed(object? sender, PropertyChangedEventArgs e) => Refresh();
        void Edited(object? sender, AvaloniaPropertyChangedEventArgs e) { if (!syncing) edit(e); }
        void LostFocus(object? sender, RoutedEventArgs e) { _ = context.CommitTextAsync(); }
        void KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { context.RevertDraft(); e.Handled = true; }
            else if (e.Key == Key.Enter && (control is not TextBox { AcceptsReturn: true } || e.KeyModifiers.HasFlag(KeyModifiers.Control))) { _ = context.CommitTextAsync(); e.Handled = true; }
        }
        Refresh();
        control.DataContext = context;
        context.PropertyChanged += Changed;
        control.PropertyChanged += Edited;
        if (commitText) { control.LostFocus += LostFocus; control.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel); }
        return new(control, () =>
        {
            context.PropertyChanged -= Changed;
            control.PropertyChanged -= Edited;
            if (commitText) { control.LostFocus -= LostFocus; control.RemoveHandler(InputElement.KeyDownEvent, KeyDown); }
        });
    }
}
