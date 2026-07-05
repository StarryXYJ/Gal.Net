using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Core.Widget;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultDialogueConfig
{
    /// <summary>Background opacity (0~1).</summary>
    public double BackgroundOpacity { get; set; } = 0.8;
    /// <summary>Font size.</summary>
    public double FontSize { get; set; } = 16;
    /// <summary>Horizontal alignment.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;
}

/// <summary>
/// Standard dialogue box widget — implements IDialogueWidget.
/// Usage: var dlg = new DefaultDialogueTemplate(config); dlg.SetSpeaker("Alice"); dlg.SetContent("Hello");
/// </summary>
public sealed class DefaultDialogueTemplate : Border, IDialogueWidget
{
    public string Category => "DialogueBox";

    private readonly TextBlock _speakerBlock;
    private readonly TextBlock _textBlock;

    public DefaultDialogueTemplate(DefaultDialogueConfig? config = null)
    {
        var cfg = config ?? new DefaultDialogueConfig();

        _speakerBlock = new TextBlock
        {
            FontSize = cfg.FontSize + 4,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            Margin = new Avalonia.Thickness(0, 0, 0, 4),
        };

        _textBlock = new TextBlock
        {
            FontSize = cfg.FontSize,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            HorizontalAlignment = cfg.HorizontalAlignment,
            MaxWidth = 800,
        };

        Background = new SolidColorBrush(
            Color.FromArgb((byte)(cfg.BackgroundOpacity * 255), 0, 0, 0));
        CornerRadius = new Avalonia.CornerRadius(8);
        Margin = new Avalonia.Thickness(20);

        Child = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Children = { _speakerBlock, _textBlock },
            Margin = new Avalonia.Thickness(16),
        };
    }

    public void SetSpeaker(string? speaker)
    {
        _speakerBlock.Text = speaker ?? "";
        _speakerBlock.IsVisible = !string.IsNullOrEmpty(speaker);
    }

    public void SetContent(string text) => _textBlock.Text = text;

    public void SetAvatarVisible(bool visible)
    {
        // Future: avatar area left of dialogue
    }

    public void SetAvatar(string? avatarId)
    {
        // Future: load avatar by asset ID
    }
}

// ── NvlDialogue ──────────────────────────────────────────────

public sealed class NvlDialogueConfig
{
    public double BackgroundOpacity { get; set; } = 0.85;
    public double FontSize { get; set; } = 15;
    public int MaxLines { get; set; } = 10;
}

/// <summary>
/// NVL-style dialogue box implemented as an IDialogueWidget.
/// </summary>
public sealed class NvlDialogueTemplate : Border, IDialogueWidget
{
    public string Category => "DialogueBox";

    private readonly TextBlock _textBlock;

    public NvlDialogueTemplate(NvlDialogueConfig? config = null)
    {
        var cfg = config ?? new NvlDialogueConfig();

        _textBlock = new TextBlock
        {
            FontSize = cfg.FontSize,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            MaxLines = cfg.MaxLines,
            LineHeight = cfg.FontSize * 1.6,
            Margin = new Avalonia.Thickness(16),
        };

        Background = new SolidColorBrush(
            Color.FromArgb((byte)(cfg.BackgroundOpacity * 255), 0, 0, 0));
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Bottom;
        Padding = new Avalonia.Thickness(20);

        Child = _textBlock;
    }

    public void SetSpeaker(string? speaker) { /* NVL infers speaker from inline text */ }

    public void SetContent(string text) => _textBlock.Text = text;

    public void SetAvatarVisible(bool visible) { }

    public void SetAvatar(string? avatarId) { }
}
