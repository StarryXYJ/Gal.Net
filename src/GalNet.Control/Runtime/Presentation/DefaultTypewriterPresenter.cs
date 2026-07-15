using System.Text;
using Avalonia.Threading;
using GalNet.Control.Abstraction.UI;
using GalNet.Control.Screen.BuiltIn;
using GalNet.Control.UI;
using GalNet.Control.Widget;
using GalNet.Control.ViewModels;
using GalNet.Core.Settings;
using Serilog;

namespace GalNet.Control.View;

internal sealed class DefaultTypewriterPresenter
{
    private readonly GameSettings _settings;
    private readonly GameScreenView _gameScreen;
    private readonly DefaultGameViewRegistry _registry;
    private readonly IWidgetFactory _factory;
    private readonly WidgetBuildContext _context;
    private readonly GameScreenViewModel _screen;
    private DialogueWidgetViewModel? _activeDialogue;
    private CancellationTokenSource? _typewriterCts;
    private Task? _currentTask;
    public Task? CurrentTask => _currentTask;

    public DefaultTypewriterPresenter(GameSettings settings, GameScreenView gameScreen, DefaultGameViewRegistry registry, IWidgetFactory factory, WidgetBuildContext context, GameScreenViewModel screen)
    {
        _settings = settings; _gameScreen = gameScreen; _registry = registry; _factory = factory; _context = context; _screen = screen;
    }

    public async Task StartAsync(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        _typewriterCts?.Cancel();
        _typewriterCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var task = StartCoreAsync(widgetInstanceId, speaker, text, _typewriterCts.Token);
        _currentTask = task;
        try { await task; }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error(ex, "Typewriter task faulted"); }
        finally { _currentTask = null; }
    }

    private async Task StartCoreAsync(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var host = new WidgetHostViewModel(_factory, _context, widgetInstanceId, "dialogue");
            _activeDialogue = host.RequireWidget<DialogueWidgetViewModel>();
            _registry.RegisterWidget(widgetInstanceId, host.View!);
            _screen.DialogueHost = host;
            _screen.IsDialogueVisible = true;
            _gameScreen.ScreenOverlay.IsVisible = false;
            _activeDialogue.Speaker = speaker;
            _activeDialogue.Segments.Clear();
            foreach (var segment in ParseSegments(text)) _activeDialogue.Segments.Add(segment);
        });

        var dialogue = _activeDialogue!;
        var total = dialogue.Segments.Where(x => x.Kind == RichTextSegmentKind.Text).Sum(x => x.FullText.Length);
        var delay = _settings.TextSpeed > 0 ? (int)(1000.0 / _settings.TextSpeed) : 30;
        try
        {
            for (var revealed = 1; revealed <= total; revealed++)
            {
                ct.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() => Reveal(dialogue, revealed));
                if (delay > 0) await Task.Delay(delay, ct);
            }
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => Reveal(dialogue, total));
        }
    }

    private static IReadOnlyList<RichTextSegmentViewModel> ParseSegments(string raw)
    {
        var result = new List<RichTextSegmentViewModel>();
        var buffer = new StringBuilder();
        var bold = false; var italic = false;
        void Flush()
        {
            if (buffer.Length == 0) return;
            result.Add(new() { Kind = RichTextSegmentKind.Text, Bold = bold, Italic = italic, FullText = buffer.ToString() });
            buffer.Clear();
        }
        for (var i = 0; i < raw.Length;)
        {
            if (raw.AsSpan(i).StartsWith("<b>")) { Flush(); bold = true; i += 3; continue; }
            if (raw.AsSpan(i).StartsWith("</b>")) { Flush(); bold = false; i += 4; continue; }
            if (raw.AsSpan(i).StartsWith("<i>")) { Flush(); italic = true; i += 3; continue; }
            if (raw.AsSpan(i).StartsWith("</i>")) { Flush(); italic = false; i += 4; continue; }
            if (raw.AsSpan(i).StartsWith("\\n")) { Flush(); result.Add(new() { Kind = RichTextSegmentKind.LineBreak }); i += 2; continue; }
            if (raw.AsSpan(i).StartsWith("\\d{"))
            {
                var end = raw.IndexOf('}', i + 3);
                if (end >= 0) { i = end + 1; continue; }
            }
            buffer.Append(raw[i++]);
        }
        Flush();
        return result;
    }

    private static void Reveal(DialogueWidgetViewModel dialogue, int count)
    {
        var remaining = count;
        foreach (var segment in dialogue.Segments)
        {
            if (segment.Kind != RichTextSegmentKind.Text) continue;
            var take = Math.Min(remaining, segment.FullText.Length);
            segment.VisibleText = segment.FullText[..take];
            remaining -= take;
        }
        dialogue.Content = string.Concat(dialogue.Segments.Select(x => x.Kind == RichTextSegmentKind.LineBreak ? "\n" : x.VisibleText));
    }

    public void Skip(string widgetInstanceId) => _typewriterCts?.Cancel();
    public void Cancel() => _typewriterCts?.Cancel();
    public void SetVoice(string assetId) { }
}
