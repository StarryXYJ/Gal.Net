using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
    /// <summary>Minimum height of the dialog box.</summary>
    public double MinHeight { get; set; } = 140;
}

/// <summary>
/// Standard dialogue box widget — implements IDialogueWidget.
/// Supports rich text via &lt;b&gt; and &lt;i&gt; tags.
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

        MinHeight = cfg.MinHeight;
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

    public void SetContent(string text)
    {
        _textBlock.Inlines = ParseRichText(text);
    }

    /// <summary>直接设置 InlineCollection（用于打字机预建 Run 逐步填充）。</summary>
    public void SetInlines(InlineCollection inlines)
    {
        _textBlock.Inlines = inlines;
    }

    /// <summary>Parse &lt;b&gt;/&lt;i&gt; tags and &apos;\n&apos; newlines into InlineCollection.</summary>
    private static InlineCollection ParseRichText(string text)
    {
        var inlines = new InlineCollection();
        var i = 0;
        while (i < text.Length)
        {
            // <b>...</b>
            if (i + 3 <= text.Length && text[i] == '<' && text[i + 1] == 'b' && text[i + 2] == '>')
            {
                var end = text.IndexOf("</b>", i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    AddStyledRuns(inlines, text[(i + 3)..end], FontWeight.Bold, null);
                    i = end + 4;
                    continue;
                }
            }

            // <i>...</i>
            if (i + 3 <= text.Length && text[i] == '<' && text[i + 1] == 'i' && text[i + 2] == '>')
            {
                var end = text.IndexOf("</i>", i + 3, StringComparison.Ordinal);
                if (end >= 0)
                {
                    AddStyledRuns(inlines, text[(i + 3)..end], null, FontStyle.Italic);
                    i = end + 4;
                    continue;
                }
            }

            // Plain text until next tag or end
            var nextTag = text.IndexOf('<', i + 1);
            if (nextTag < 0) nextTag = text.Length;
            AddStyledRuns(inlines, text[i..nextTag], null, null);
            i = nextTag;
        }
        return inlines;
    }

    /// <summary>Add text content as runs, splitting on '\n' with LineBreak elements.</summary>
    private static void AddStyledRuns(InlineCollection inlines, string text, FontWeight? fontWeight, FontStyle? fontStyle)
    {
        var parts = text.Split('\n');
        for (var p = 0; p < parts.Length; p++)
        {
            if (p > 0)
                inlines.Add(new LineBreak());

            if (parts[p].Length == 0)
                continue; // skip empty segments (leading/trailing/consecutive \n)

            var run = new Run { Text = parts[p] };
            if (fontWeight.HasValue) run.FontWeight = fontWeight.Value;
            if (fontStyle.HasValue) run.FontStyle = fontStyle.Value;
            inlines.Add(run);
        }
    }

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
