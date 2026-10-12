using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace GalNet.Avalonia.GameView.MarkupExtensions;

/// <summary>Resolves a changing localization key through the same host adapter as PlayerText.</summary>
public sealed class PlayerTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> KeyProperty =
        AvaloniaProperty.Register<PlayerTextBlock, string?>(nameof(Key));
    private IDisposable? _binding;
    public string? Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KeyProperty) UpdateText();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateText();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _binding?.Dispose();
        _binding = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateText()
    {
        _binding?.Dispose();
        _binding = null;
        IsVisible = !string.IsNullOrWhiteSpace(Key);
        Text = Key;
        if (IsVisible && PlayerTextExtension.Resolve is { } resolve)
            _binding = this.Bind(TextProperty, new ReflectionBinding("Value") { Source = resolve(Key!), Mode = BindingMode.OneWay });
    }
}
