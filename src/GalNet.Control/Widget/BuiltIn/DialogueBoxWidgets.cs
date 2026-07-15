using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.Widget;

namespace GalNet.Control.Widget.BuiltIn;

public sealed class DefaultDialogueConfig : PresentationConfig
{
    public double BackgroundOpacity { get; set; } = 0.8;
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;
    public DefaultDialogueConfig() { FontSize = 16; MinHeight = 140; }
}

/// <summary>Dialogue view. All state is supplied by <see cref="DialogueWidgetViewModel"/>.</summary>
public sealed class DefaultDialogueTemplate : Border
{
    public DefaultDialogueTemplate(DefaultDialogueConfig? config = null)
    {
        var cfg = config ?? new DefaultDialogueConfig();
        var fontSize = cfg.FontSize ?? 16;
        var speaker = new TextBlock { FontSize = fontSize + 4, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 0, 0, 4) };
        speaker.Bind(TextBlock.TextProperty, new Binding(nameof(DialogueWidgetViewModel.Speaker)));
        speaker.Bind(TextBlock.ForegroundProperty, PaletteBinding.Create(speaker, "FontColor0"));

        var content = new RichTextRenderer
        {
            FontSize = fontSize,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = cfg.HorizontalAlignment,
            MaxWidth = 800,
        };
        content.Bind(RichTextRenderer.SegmentsProperty, new Binding(nameof(DialogueWidgetViewModel.Segments)));
        content.Bind(TextBlock.ForegroundProperty, PaletteBinding.Create(content, "FontColor0"));

        Bind(BackgroundProperty, PaletteBinding.Create(this, "Background1"));
        CornerRadius = new CornerRadius(8);
        Margin = new Thickness(20);
        Child = new StackPanel { Orientation = Orientation.Vertical, Children = { speaker, content }, Margin = new Thickness(16) };
    }
}

/// <summary>Rendering-only adapter from UI-neutral rich-text segments to Avalonia inlines.</summary>
public sealed class RichTextRenderer : TextBlock
{
    public static readonly StyledProperty<IEnumerable<RichTextSegmentViewModel>?> SegmentsProperty =
        AvaloniaProperty.Register<RichTextRenderer, IEnumerable<RichTextSegmentViewModel>?>(nameof(Segments));

    private INotifyCollectionChanged? _collection;
    private IEnumerable<RichTextSegmentViewModel>? _segments;
    public IEnumerable<RichTextSegmentViewModel>? Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    static RichTextRenderer() => SegmentsProperty.Changed.AddClassHandler<RichTextRenderer>((renderer, change) =>
        renderer.Attach(change.NewValue as IEnumerable<RichTextSegmentViewModel>));

    private void Attach(IEnumerable<RichTextSegmentViewModel>? segments)
    {
        if (_collection is not null) _collection.CollectionChanged -= OnCollectionChanged;
        if (_segments is not null) foreach (var segment in _segments) segment.PropertyChanged -= OnSegmentChanged;
        _segments = segments;
        _collection = segments as INotifyCollectionChanged;
        if (_collection is not null) _collection.CollectionChanged += OnCollectionChanged;
        if (segments is not null) foreach (var segment in segments) segment.PropertyChanged += OnSegmentChanged;
        Render();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (RichTextSegmentViewModel segment in e.OldItems) segment.PropertyChanged -= OnSegmentChanged;
        if (e.NewItems is not null) foreach (RichTextSegmentViewModel segment in e.NewItems) segment.PropertyChanged += OnSegmentChanged;
        Render();
    }

    private void OnSegmentChanged(object? sender, PropertyChangedEventArgs e) => Render();

    private void Render()
    {
        var inlines = new InlineCollection();
        foreach (var segment in Segments ?? [])
        {
            if (segment.Kind == RichTextSegmentKind.LineBreak) { inlines.Add(new LineBreak()); continue; }
            inlines.Add(new Run
            {
                Text = segment.VisibleText,
                FontWeight = segment.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = segment.Italic ? FontStyle.Italic : FontStyle.Normal,
            });
        }
        Inlines = inlines;
    }
}

public sealed class NvlDialogueConfig : PresentationConfig
{
    public double BackgroundOpacity { get; set; } = 0.85;
    public int MaxLines { get; set; } = 10;
    public NvlDialogueConfig() => FontSize = 15;
}

public sealed class NvlDialogueTemplate : Border
{
    public NvlDialogueTemplate(NvlDialogueConfig? config = null)
    {
        var cfg = config ?? new NvlDialogueConfig();
        var text = new TextBlock
        {
            FontSize = cfg.FontSize ?? 15,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = cfg.MaxLines,
            LineHeight = (cfg.FontSize ?? 15) * 1.6,
            Margin = new Thickness(16),
        };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(DialogueWidgetViewModel.Content)));
        Bind(BackgroundProperty, PaletteBinding.Create(this, "Background1"));
        text.Bind(TextBlock.ForegroundProperty, PaletteBinding.Create(text, "FontColor0"));
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Bottom;
        Padding = new Thickness(20);
        Child = text;
    }
}
