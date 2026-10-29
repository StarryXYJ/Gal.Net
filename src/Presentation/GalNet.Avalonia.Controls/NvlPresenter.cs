using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace GalNet.Game.Controls;

/// <summary>Scrollable NVL dialogue history with an independently typewritten line for each append.</summary>
public sealed class NvlPresenter : TemplatedControl
{
    private StackPanel? _lines;
    private ScrollViewer? _scrollViewer;
    private NvlLinePresenter? _latestLine;
    public bool NextLineWasRead { get; set; }

    /// <summary>Adds a line and completes after its rich text/typewriter presentation finishes.</summary>
    public Task AppendAsync(string text, double charactersPerSecond, CancellationToken cancellationToken = default)
    {
        ApplyTemplate();
        var lines = _lines ?? throw new InvalidOperationException("NvlPresenter requires the PART_Lines template part.");
        var line = new NvlLinePresenter
        {
            Text = text,
            CharactersPerSecond = charactersPerSecond
        };
        ReadTextStyle.SetWasRead(line, NextLineWasRead);
        lines.Children.Add(line);
        _latestLine = line;
        line.ApplyTemplate();
        _scrollViewer?.ScrollToEnd();
        return line.StartAsync(cancellationToken);
    }

    /// <summary>Skips the currently appended line through its next typewriter skip boundary.</summary>
    public void SkipText() => _latestLine?.Skip();
    public void RevealAllText() => _latestLine?.RevealAll();

    /// <summary>Removes all accumulated lines.</summary>
    public void Clear()
    {
        _lines?.Children.Clear();
        _latestLine = null;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _lines = e.NameScope.Find<StackPanel>("PART_Lines");
        _scrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
    }
}

/// <summary>Templatable rich typewriter line used by <see cref="NvlPresenter"/>.</summary>
public sealed class NvlLinePresenter : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<NvlLinePresenter, string?>(nameof(Text));

    public static readonly StyledProperty<double> CharactersPerSecondProperty =
        AvaloniaProperty.Register<NvlLinePresenter, double>(nameof(CharactersPerSecond), 30d);

    private TypewriterTextBlock? _typewriter;

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double CharactersPerSecond
    {
        get => GetValue(CharactersPerSecondProperty);
        set => SetValue(CharactersPerSecondProperty, value);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ApplyTemplate();
        return (_typewriter ?? throw new InvalidOperationException(
            "NvlLinePresenter requires the PART_Typewriter template part."))
            .StartAsync(cancellationToken);
    }

    public void Skip() => _typewriter?.Skip();
    public void RevealAll() => _typewriter?.RevealAll();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _typewriter = e.NameScope.Find<TypewriterTextBlock>("PART_Typewriter");
    }
}
