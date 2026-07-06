using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using GalNet.Control.Screen.BuiltIn;
using GalNet.Control.Widget.BuiltIn;
using GalNet.Core.Settings;
using GalNet.Core.Widget;
using Serilog;
using AvaloniaControl = Avalonia.Controls.Control;

namespace GalNet.Control.View;

internal sealed class DefaultTypewriterPresenter
{
    private readonly GameSettings _settings;
    private readonly GameScreenView _gameScreen;
    private readonly DefaultGameViewRegistry _registry;
    private IDialogueWidget? _activeDialogue;
    private CancellationTokenSource? _typewriterCts;
    private Task? _currentTask;
    private string _lastCleanText = "";

    /// <summary>当前正在运行的类型任务，完成后为 null。用于 WaitForClickAsync 等待。</summary>
    public Task? CurrentTask => _currentTask;

    public DefaultTypewriterPresenter(GameSettings settings, GameScreenView gameScreen, DefaultGameViewRegistry registry)
    {
        _settings = settings;
        _gameScreen = gameScreen;
        _registry = registry;
    }

    public async Task StartAsync(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        _typewriterCts?.Cancel();
        _typewriterCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var task = StartCoreAsync(widgetInstanceId, speaker, text, _typewriterCts.Token);
        _currentTask = task;

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // expected on skip
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Typewriter task faulted");
        }
        finally
        {
            _currentTask = null;
        }
    }

    // ── Rich text segment model ──────────────────────────────────────────

    private sealed class RichSegment
    {
        public bool IsLineBreak { get; init; }
        public FontWeight? Weight { get; init; }
        public FontStyle? Style { get; init; }
        public string Text { get; init; } = "";
        public Run? Run { get; set; }
    }

    /// <summary>Parse raw text (with &lt;b&gt; &lt;i&gt; \n \d{} markers) into styled segments.</summary>
    private static List<RichSegment> ParseSegments(string rawText)
    {
        var segments = new List<RichSegment>();
        FontWeight? curWeight = null;
        FontStyle? curStyle = null;
        var sb = new System.Text.StringBuilder();

        void Flush()
        {
            if (sb.Length > 0)
            {
                segments.Add(new RichSegment
                {
                    Text = sb.ToString(),
                    Weight = curWeight,
                    Style = curStyle,
                });
                sb.Clear();
            }
        }

        var i = 0;
        while (i < rawText.Length)
        {
            // \d{...} delay — skip entirely (handled separately in the typewriter loop)
            if (rawText[i] == '\\' && i + 1 < rawText.Length && rawText[i + 1] == 'd')
            {
                var end = rawText.IndexOf('}', i + 2);
                if (end > i + 2) { i = end + 1; continue; }
            }

            // <b> → flush, set bold
            if (i + 3 <= rawText.Length && rawText[i] == '<' && rawText[i + 1] == 'b' && rawText[i + 2] == '>')
            {
                Flush();
                curWeight = FontWeight.Bold;
                i += 3;
                continue;
            }

            // </b> → flush, clear bold
            if (i + 4 <= rawText.Length && rawText[i] == '<' && rawText[i + 1] == '/' && rawText[i + 2] == 'b' && rawText[i + 3] == '>')
            {
                Flush();
                curWeight = null;
                i += 4;
                continue;
            }

            // <i> → flush, set italic
            if (i + 3 <= rawText.Length && rawText[i] == '<' && rawText[i + 1] == 'i' && rawText[i + 2] == '>')
            {
                Flush();
                curStyle = FontStyle.Italic;
                i += 3;
                continue;
            }

            // </i> → flush, clear italic
            if (i + 4 <= rawText.Length && rawText[i] == '<' && rawText[i + 1] == '/' && rawText[i + 2] == 'i' && rawText[i + 3] == '>')
            {
                Flush();
                curStyle = null;
                i += 4;
                continue;
            }

            // \n → flush + line break segment
            if (rawText[i] == '\\' && i + 1 < rawText.Length && rawText[i + 1] == 'n')
            {
                Flush();
                segments.Add(new RichSegment { IsLineBreak = true });
                i += 2;
                continue;
            }

            // regular visible character
            sb.Append(rawText[i]);
            i++;
        }

        Flush();
        return segments;
    }

    /// <summary>Build InlineCollection from segments, storing Run references for later updates.</summary>
    private static InlineCollection BuildInlineCollection(List<RichSegment> segments)
    {
        var inlines = new InlineCollection();
        foreach (var seg in segments)
        {
            if (seg.IsLineBreak)
            {
                inlines.Add(new LineBreak());
            }
            else
            {
                var run = new Run
                {
                    Text = "",
                    FontWeight = seg.Weight ?? FontWeight.Normal,
                    FontStyle = seg.Style ?? FontStyle.Normal,
                };
                seg.Run = run;
                inlines.Add(run);
            }
        }
        return inlines;
    }

    /// <summary>Update Run.Text for each segment based on number of revealed visible characters.</summary>
    private static void UpdateRuns(List<RichSegment> segments, int revealed)
    {
        var remaining = revealed;
        foreach (var seg in segments)
        {
            if (seg.IsLineBreak) continue;
            var take = Math.Min(remaining, seg.Text.Length);
            seg.Run!.Text = seg.Text.Substring(0, take);
            remaining -= take;
            if (remaining <= 0) break;
        }
    }

    /// <summary>Reveal all text in every segment.</summary>
    private static void RevealAll(List<RichSegment> segments)
    {
        foreach (var seg in segments)
        {
            if (seg.IsLineBreak) continue;
            seg.Run!.Text = seg.Text;
        }
    }

    // ── Core typewriter logic ────────────────────────────────────────────

    private async Task StartCoreAsync(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        if (_activeDialogue == null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    var dlg = new DefaultDialogueTemplate(new DefaultDialogueConfig
                    {
                        FontSize = 16,
                        BackgroundOpacity = 0.8,
                    });
                    dlg.SetSpeaker(speaker);

                    _activeDialogue = dlg;
                    _registry.RegisterWidget(widgetInstanceId, (AvaloniaControl)dlg);

                    _gameScreen.DialogueHost.Content = dlg;
                    _gameScreen.DialogueHost.IsVisible = true;
                    _gameScreen.ScreenOverlay.IsVisible = false;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to create dialogue widget");
                }
            });
        }

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dlg = _activeDialogue as DefaultDialogueTemplate;
            if (dlg == null)
                return;

            try
            {
                dlg.SetSpeaker(speaker);

                // ── Pre-parse segments & pre-build InlineCollection ──
                var segments = ParseSegments(text);
                var inlines = BuildInlineCollection(segments);
                dlg.SetInlines(inlines);

                _lastCleanText = GetCleanText(text);
                var charDelay = _settings.TextSpeed > 0 ? (int)(1000.0 / _settings.TextSpeed) : 30;
                var totalChars = 0;
                foreach (var seg in segments)
                    if (!seg.IsLineBreak)
                        totalChars += seg.Text.Length;

                var revealed = 0;
                var i = 0;

                while (revealed < totalChars && !ct.IsCancellationRequested)
                {
                    // \d{ms} delay marker
                    if (i < text.Length && text[i] == '\\' && i + 1 < text.Length && text[i + 1] == 'd')
                    {
                        var end = text.IndexOf('}', i + 2);
                        if (end > i + 2)
                        {
                            var numStr = text.Substring(i + 3, end - i - 3);
                            if (numStr == "-")
                            {
                                // Reveal everything immediately
                                RevealAll(segments);
                                break;
                            }

                            if (int.TryParse(numStr, out var delayMs) && delayMs > 0)
                            {
                                try { await Task.Delay(delayMs, ct); }
                                catch (OperationCanceledException) { break; }
                            }

                            i = end + 1;
                            continue;
                        }
                    }

                    // Skip <tags> in raw text position tracking
                    if (i < text.Length && text[i] == '<')
                    {
                        var closeTag = text.IndexOf('>', i + 1);
                        if (closeTag > i) { i = closeTag + 1; continue; }
                    }

                    // Skip \n in raw text position tracking (already in segments as IsLineBreak)
                    if (i < text.Length && text[i] == '\\' && i + 1 < text.Length && text[i + 1] == 'n')
                    {
                        i += 2;
                        continue;
                    }

                    // Reveal next visible character
                    if (i < text.Length)
                    {
                        revealed++;
                        UpdateRuns(segments, revealed);
                        i++;
                    }
                    else
                    {
                        break;
                    }

                    if (charDelay > 0)
                    {
                        try { await Task.Delay(charDelay, ct); }
                        catch (OperationCanceledException) { break; }
                    }
                }

                // Always reveal full text at end — even if user clicked to skip
                RevealAll(segments);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Typewriter error — fallback to plain text");
                dlg.SetContent(GetCleanText(text));
            }
        });
    }

    /// <summary>剔除 \d{} / \n / 富文本标签，保留纯文本。</summary>
    private static string GetCleanText(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                if (text[i + 1] == 'd')
                {
                    var end = text.IndexOf('}', i + 2);
                    if (end > i + 2)
                    {
                        i = end;
                        continue;
                    }
                }
                else if (text[i + 1] == 'n')
                {
                    sb.Append('\n');
                    i++;
                    continue;
                }
            }

            if (text[i] == '<')
            {
                var end = text.IndexOf('>', i + 1);
                if (end > i) { i = end; continue; }
            }

            sb.Append(text[i]);
        }
        return sb.ToString();
    }

    public void Skip(string widgetInstanceId)
    {
        _typewriterCts?.Cancel();
    }

    public void SetVoice(string assetId) { }
}
