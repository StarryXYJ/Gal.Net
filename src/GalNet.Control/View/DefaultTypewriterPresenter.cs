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

        // 注册当前任务，供 WaitForClickAsync 等待
        // 传入 _typewriterCts.Token 而非原始 ct，使 Skip() 能真正取消打字循环
        var task = StartCoreAsync(widgetInstanceId, speaker, text, _typewriterCts.Token);
        _currentTask = task;

        try
        {
            await task;
        }
        finally
        {
            _currentTask = null;
        }
    }

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
            var dlg = _activeDialogue;
            if (dlg == null)
                return;

            dlg.SetSpeaker(speaker);
            dlg.SetContent("");

            // 缓存剔除 \d{} 后的纯文本，供取消时直接显示
            _lastCleanText = GetCleanText(text);
            var charDelay = _settings.TextSpeed > 0 ? (int)(1000.0 / _settings.TextSpeed) : 30;
            var i = 0;
            var current = "";

            while (i < text.Length && !ct.IsCancellationRequested)
            {
                // 解析 \d{ms} 延迟指令
                if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == 'd')
                {
                    var end = text.IndexOf('}', i + 2);
                    if (end > i + 2)
                    {
                        var numStr = text.Substring(i + 3, end - i - 3);
                        if (numStr == "-")
                        {
                            dlg.SetContent(text[(end + 1)..]);
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

                current += text[i];
                dlg.SetContent(current);
                i++;

                if (charDelay > 0)
                {
                    try { await Task.Delay(charDelay, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }

            if (ct.IsCancellationRequested)
                dlg.SetContent(_lastCleanText);
        });
    }

    /// <summary>剔除 \d{...} 延迟指令，保留纯文本。</summary>
    private static string GetCleanText(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == 'd')
            {
                var end = text.IndexOf('}', i + 2);
                if (end > i + 2)
                {
                    i = end; // for 循环末尾会 ++，跳过整个 \d{...}
                    continue;
                }
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
