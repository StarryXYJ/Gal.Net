using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace GalNet.Avalonia.GameView.MarkupExtensions;

/// <summary>Resolves a changing localization key through the same host adapter as PlayerText.</summary>
public sealed class PlayerTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> KeyProperty =
        AvaloniaProperty.Register<PlayerTextBlock, string?>(nameof(Key));
    public static readonly StyledProperty<string?> FallbackProperty =
        AvaloniaProperty.Register<PlayerTextBlock, string?>(nameof(Fallback));
    private IDisposable? _binding;
    public string? Key { get => GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public string? Fallback { get => GetValue(FallbackProperty); set => SetValue(FallbackProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KeyProperty || change.Property == FallbackProperty) UpdateText();
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
        Text = Fallback ?? Key;
        if (IsVisible && PlayerTextExtension.Resolve is { } resolve)
            _binding = this.Bind(TextProperty, new ReflectionBinding("Value") { Source = resolve(Key!), Mode = BindingMode.OneWay });
    }
}
