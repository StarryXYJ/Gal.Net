using Avalonia;
using Avalonia.Controls;
using GalNet.Core.Text;

namespace GalNet.Game.Controls;

/// <summary>TextBlock that renders the portable GalNet typewriter directives over time.</summary>
public sealed class TypewriterTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> SourceTextProperty =
        AvaloniaProperty.Register<TypewriterTextBlock, string?>(nameof(SourceText));

    public static readonly StyledProperty<double> CharactersPerSecondProperty =
        AvaloniaProperty.Register<TypewriterTextBlock, double>(nameof(CharactersPerSecond), 30d);

    public static readonly DirectProperty<TypewriterTextBlock, bool> IsRunningProperty =
        AvaloniaProperty.RegisterDirect<TypewriterTextBlock, bool>(nameof(IsRunning), control => control.IsRunning);

    public static readonly DirectProperty<TypewriterTextBlock, bool> IsCompletedProperty =
        AvaloniaProperty.RegisterDirect<TypewriterTextBlock, bool>(nameof(IsCompleted), control => control.IsCompleted);

    private CancellationTokenSource? _runCancellation;
    private TaskCompletionSource? _completion;
    private TaskCompletionSource? _skipSignal;
    private bool _isRunning;
    private bool _isCompleted = true;
    private bool _skipRequested;

    static TypewriterTextBlock()
    {
        SourceTextProperty.Changed.AddClassHandler<TypewriterTextBlock>((control, _) => control.Restart());
        CharactersPerSecondProperty.Changed.AddClassHandler<TypewriterTextBlock>((control, _) => control.Restart());
    }

    /// <summary>Raw game text, including <c>\d</c> and <c>\n</c> directives.</summary>
    public string? SourceText
    {
        get => GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    /// <summary>Visible characters per second. Zero or less renders immediately.</summary>
    public double CharactersPerSecond
    {
        get => GetValue(CharactersPerSecondProperty);
        set => SetValue(CharactersPerSecondProperty, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetAndRaise(IsRunningProperty, ref _isRunning, value);
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        private set => SetAndRaise(IsCompletedProperty, ref _isCompleted, value);
    }

    /// <summary>Completes when the current text has fully rendered or been skipped.</summary>
    public Task Completion => _completion?.Task ?? Task.CompletedTask;

    public event EventHandler? Completed;

    /// <summary>Begins a new render using the current <see cref="SourceText"/>.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _runCancellation?.Cancel();
        _runCancellation?.Dispose();
        _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _skipSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _skipRequested = false;
        IsRunning = true;
        IsCompleted = false;
        Text = string.Empty;
        _ = RenderAsync(SourceText, _runCancellation.Token, _completion, _skipSignal);
        return _completion.Task;
    }

    /// <summary>Reveals the remaining text immediately without treating it as cancellation.</summary>
    public void Skip()
    {
        _skipRequested = true;
        _skipSignal?.TrySetResult();
    }

    private void Restart() => _ = StartAsync();

    private async Task RenderAsync(
        string? source,
        CancellationToken cancellationToken,
        TaskCompletionSource completion,
        TaskCompletionSource skipSignal)
    {
        try
        {
            var visible = new System.Text.StringBuilder();
            foreach (var token in TypewriterTextParser.Parse(source))
            {
                switch (token.Kind)
                {
                    case TypewriterTokenKind.Text:
                        foreach (var character in token.Text)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            visible.Append(character);
                            Text = visible.ToString();
                            if (CharactersPerSecond > 0)
                                await DelayOrSkipAsync(TimeSpan.FromSeconds(1d / CharactersPerSecond), cancellationToken, skipSignal);
                        }
                        break;
                    case TypewriterTokenKind.Delay:
                        await DelayOrSkipAsync(TimeSpan.FromMilliseconds(token.DelayMilliseconds), cancellationToken, skipSignal);
                        break;
                    case TypewriterTokenKind.Instant:
                        _skipRequested = true;
                        skipSignal.TrySetResult();
                        break;
                }
            }

            IsRunning = false;
            IsCompleted = true;
            completion.TrySetResult();
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            IsRunning = false;
            completion.TrySetCanceled(cancellationToken);
        }
        catch (Exception exception)
        {
            IsRunning = false;
            completion.TrySetException(exception);
        }
    }

    private async Task DelayOrSkipAsync(TimeSpan duration, CancellationToken cancellationToken, TaskCompletionSource skipSignal)
    {
        if (_skipRequested || CharactersPerSecond <= 0 || duration <= TimeSpan.Zero)
            return;

        await Task.WhenAny(Task.Delay(duration, cancellationToken), skipSignal.Task);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
