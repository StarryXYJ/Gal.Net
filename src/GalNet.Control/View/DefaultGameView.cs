using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GalNet.Core.Settings;
using GalNet.Core.View;
using GalNet.Core.Widget;
using GalNet.Control.Screen.BuiltIn;
using GalNet.Control.Widget.BuiltIn;
using Serilog;

namespace GalNet.Control.View;

/// <summary>
/// Default game view — full IGameView implementation based on Category interfaces.
/// No factory/registry overhead. Widgets and Screens are created directly via constructors.
/// </summary>
public class DefaultGameView : UserControl, IGameView
{
    private readonly GameSettings _settings;

    // layout
    private readonly GameScreenView _gameScreen;

    // TCS for async interaction waits
    private TaskCompletionSource<int>? _clickTcs;

    // layer tracking
    private readonly Dictionary<string, Image> _layers = new(StringComparer.OrdinalIgnoreCase);

    // named widget instances (id → control)
    private readonly Dictionary<string, Avalonia.Controls.Control> _widgets = new(StringComparer.OrdinalIgnoreCase);

    // current dialogue widget for typewriter
    private IDialogueWidget? _activeDialogue;
    private IChoicePanel? _activeChoicePanel;

    // typewriter state
    private CancellationTokenSource? _typewriterCts;

    // ── Constructor ──

    public DefaultGameView(GameSettings? settings = null)
    {
        _settings = settings ?? new GameSettings();

        _gameScreen = new GameScreenView();
        Content = _gameScreen;

        Log.Debug("DefaultGameView constructed, size={W}x{H}", _gameScreen.Width, _gameScreen.Height);

        // click to advance
        PointerPressed += (_, e) =>
        {
            Log.Debug("PointerPressed at {X},{Y}", e.GetPosition(this).X, e.GetPosition(this).Y);
            if (_clickTcs is { Task.IsCompleted: false })
            {
                var tcs = _clickTcs;
                _clickTcs = null;
                tcs.TrySetResult(0);
                Log.Debug("Click TCS resolved");
            }
        };
    }

    // ═══════════════════════════════════════════════════════════════
    //  Layer management
    // ═══════════════════════════════════════════════════════════════

    public void ShowLayer(string id, string assetId, float x, float y, float z = 0)
    {
        Log.Debug("ShowLayer: id={Id} asset={Asset} pos=({X},{Y}) z={Z}", id, assetId, x, y, z);
        Dispatcher.UIThread.Post(() =>
        {
            if (_layers.TryGetValue(id, out var existing))
            {
                existing.SetValue(Avalonia.Controls.Canvas.LeftProperty, (double)x);
                existing.SetValue(Avalonia.Controls.Canvas.TopProperty, (double)y);
                existing.SetValue(Avalonia.Controls.Canvas.ZIndexProperty, (int)z);
                return;
            }
            var img = new Image { Width = 200, Height = 200, Opacity = 1 };
            img.SetValue(Avalonia.Controls.Canvas.LeftProperty, (double)x);
            img.SetValue(Avalonia.Controls.Canvas.TopProperty, (double)y);
            img.SetValue(Avalonia.Controls.Canvas.ZIndexProperty, (int)z);
            _layers[id] = img;
            _gameScreen.LayerCanvas.Children.Add(img);
        });
    }

    public void HideLayer(string id)
    {
        Log.Debug("HideLayer: id={Id}", id);
        Dispatcher.UIThread.Post(() =>
        {
            if (_layers.Remove(id, out var img))
                _gameScreen.LayerCanvas.Children.Remove(img);
        });
    }

    public void MoveLayer(string id, float x, float y, float z, float durationSec)
    {
        Log.Debug("MoveLayer: id={Id} pos=({X},{Y}) z={Z} dur={Dur}s", id, x, y, z, durationSec);
        Dispatcher.UIThread.Post(() =>
        {
            if (!_layers.TryGetValue(id, out var img)) return;
            img.SetValue(Avalonia.Controls.Canvas.LeftProperty, (double)x);
            img.SetValue(Avalonia.Controls.Canvas.TopProperty, (double)y);
            img.SetValue(Avalonia.Controls.Canvas.ZIndexProperty, (int)z);
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  Widget instance management
    // ═══════════════════════════════════════════════════════════════

    public void ShowControl(string instanceId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_widgets.TryGetValue(instanceId, out var ctrl))
                ctrl.IsVisible = true;
        });
    }

    public void HideControl(string instanceId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_widgets.TryGetValue(instanceId, out var ctrl))
                ctrl.IsVisible = false;
        });
    }

    public void SetControlProperty(string instanceId, string property, string value)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_widgets.TryGetValue(instanceId, out var ctrl)) return;
            var prop = ctrl.GetType().GetProperty(property);
            if (prop?.CanWrite == true)
            {
                var converted = ConvertValue(prop.PropertyType, value);
                if (converted != null) prop.SetValue(ctrl, converted);
            }
        });
    }

    /// <summary>Register a widget control by name for later lookup.</summary>
    public void RegisterWidget(string id, Avalonia.Controls.Control control)
    {
        _widgets[id] = control;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Page switching (delegated to GameHostView)
    // ═══════════════════════════════════════════════════════════════

    public async Task<string> ShowPageAsync(string screenInstanceId, CancellationToken ct)
    {
        Log.Debug("ShowPageAsync: screen={Id} (no-op, navigation owned by GameHostView)", screenInstanceId);
        return screenInstanceId;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Typewriter
    // ═══════════════════════════════════════════════════════════════

    public async Task StartTypewriter(string widgetInstanceId, string speaker, string text, CancellationToken ct)
    {
        _typewriterCts?.Cancel();
        _typewriterCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        Log.Debug("StartTypewriter: widget={Id} speaker={Speaker} textLen={Len}",
            widgetInstanceId, speaker, text.Length);

        // create dialogue widget if not exists
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
                    _widgets[widgetInstanceId] = dlg;

                    _gameScreen.DialogueHost.Content = dlg;
                    _gameScreen.DialogueHost.IsVisible = true;
                    _gameScreen.ScreenOverlay.IsVisible = false;

                    Log.Debug("Dialogue widget created: id={Id}", widgetInstanceId);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to create dialogue widget");
                }
            });
        }

        // run typewriter loop on UI thread
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dlg = _activeDialogue;
            if (dlg == null) return;

            dlg.SetSpeaker(speaker);
            dlg.SetContent("");

            var charDelay = _settings.TextSpeed > 0 ? (int)(1000.0 / _settings.TextSpeed) : 30;
            Log.Debug("Typewriter: charDelay={Delay}ms textLen={Len}", charDelay, text.Length);

            int i = 0;
            var current = "";
            while (i < text.Length && !ct.IsCancellationRequested)
            {
                // \d{ms} delay directive
                if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == 'd')
                {
                    int end = text.IndexOf('}', i + 2);
                    if (end > i + 2)
                    {
                        var numStr = text.Substring(i + 3, end - i - 3);
                        if (numStr == "-")
                        {
                            dlg.SetContent(text[(end + 1)..]);
                            break;
                        }
                        if (int.TryParse(numStr, out var delayMs) && delayMs > 0)
                            await Task.Delay(delayMs, ct);
                        i = end + 1;
                        continue;
                    }
                }

                current += text[i];
                dlg.SetContent(current);
                i++;

                if (charDelay > 0)
                    await Task.Delay(charDelay, ct);
            }

            if (ct.IsCancellationRequested)
                dlg.SetContent(text);

            Log.Debug("Typewriter done: {Count} chars, cancelled={Cancelled}", i, ct.IsCancellationRequested);
        });
    }

    public void SkipTypewriter(string widgetInstanceId)
    {
        Log.Debug("SkipTypewriter: widget={Id}", widgetInstanceId);
        _typewriterCts?.Cancel();
    }

    public void SetVoice(string assetId) { /* deferred */ }

    // ═══════════════════════════════════════════════════════════════
    //  Interactive waits (click / choice)
    // ═══════════════════════════════════════════════════════════════

    public Task WaitForClickAsync(CancellationToken ct)
    {
        Log.Debug("WaitForClickAsync");
        Dispatcher.UIThread.Post(() => _gameScreen.ClickIndicator.IsVisible = true);

        var tcs = new TaskCompletionSource<int>();
        _clickTcs = tcs;
        ct.Register(() => { _clickTcs = null; tcs.TrySetCanceled(); });
        return tcs.Task;
    }

    public Task<int> WaitForChoiceAsync(string widgetInstanceId, string[] options, CancellationToken ct)
    {
        Log.Debug("WaitForChoiceAsync: widget={Id} options={Count}", widgetInstanceId, options.Length);

        var tcs = new TaskCompletionSource<int>();

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var panel = new DefaultChoiceTemplate();
                _activeChoicePanel = panel;
                _widgets[widgetInstanceId] = panel;

                panel.ChoiceSelected += (index) =>
                {
                    _gameScreen.ChoiceHost.IsVisible = false;
                    Log.Debug("Choice selected: {Index}", index);
                    tcs.TrySetResult(index);
                };
                panel.SetChoices(options);

                _gameScreen.ChoiceHost.Content = panel;
                _gameScreen.ChoiceHost.IsVisible = true;
                _gameScreen.ClickIndicator.IsVisible = false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to build choice panel");
                tcs.TrySetResult(0);
            }
        });

        ct.Register(() => tcs.TrySetCanceled());
        return tcs.Task;
    }

    // ═══════════════════════════════════════════════════════════════
    //  Audio / Video (stubs)
    // ═══════════════════════════════════════════════════════════════

    public void PlayAudio(string channel, string assetId, float volume, string mode, int times) { }
    public void StopAudio(string channel) { }
    public void PauseAudio(string channel) { }
    public void ResumeAudio(string channel) { }
    public void EnqueueAudio(string channel, string assetId, int times) { }
    public void ConfigureAudioQueue(string channel, string onEnd, string onEmpty) { }
    public void PlayVideo(string assetId) { }
    public void StopVideo() { }
    public void ApplyTransition(string type, float durationSec) { }
    public void ApplyEffect(string effectType, IReadOnlyDictionary<string, object> parameters) { }
    public void StopEffect(string effectId) { }

    // ═══════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════

    private static object? ConvertValue(Type targetType, string value)
    {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(int) && int.TryParse(value, out var i)) return i;
        if (targetType == typeof(double) && double.TryParse(value, out var d)) return d;
        if (targetType == typeof(float) && float.TryParse(value, out var f)) return f;
        if (targetType == typeof(bool) && bool.TryParse(value, out var b)) return b;
        return null;
    }
}
