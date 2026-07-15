using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using GalNet.Control.Screen.BuiltIn;
using GalNet.Control.ViewModels;
using GalNet.Core.Settings;

namespace GalNet.Control.View;

internal sealed class DefaultTypewriterPresenter
{
    private readonly GameSettings _settings;
    private readonly GameScreenView _gameScreen;
    private readonly GameScreenViewModel _screen;
    private CancellationTokenSource? _cts;
    private Task? _currentTask;
    public Task? CurrentTask => _currentTask;
    public DefaultTypewriterPresenter(GameSettings settings, GameScreenView gameScreen, GameScreenViewModel screen) => (_settings, _gameScreen, _screen) = (settings, gameScreen, screen);

    public async Task StartAsync(string id, string speaker, string text, CancellationToken ct)
    {
        _cts?.Cancel(); _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _currentTask = RunAsync(speaker, text, _cts.Token);
        try { await _currentTask; } catch (OperationCanceledException) { } finally { _currentTask = null; }
    }

    private async Task RunAsync(string speaker, string text, CancellationToken ct)
    {
        TextBlock content = null!;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var c = _screen.Configuration;
            var speakerBlock = new TextBlock { Text = speaker, FontSize = c.DialogueFontSize + 4, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(c.SpeakerTextColor) };
            content = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = c.DialogueFontSize, Foreground = new SolidColorBrush(c.DialogueTextColor) };
            var box = new Border { Background = new SolidColorBrush(c.DialogueBackgroundColor), BorderBrush = Brush.Parse("#665F6075"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(c.DialogueCornerRadius), MinHeight = c.DialogueHeight, Margin = new Thickness(c.DialogueMargin), Padding = new Thickness(20, 16), BoxShadow = new BoxShadows(new BoxShadow { Blur = 24, OffsetY = 8, Color = Color.Parse("#66000000") }), Child = new StackPanel { Spacing = 6, Children = { speakerBlock, content } } };
            _screen.DialogueView = box; _screen.IsDialogueVisible = true; _gameScreen.ScreenOverlay.IsVisible = false;
        });
        var delay = _settings.TextSpeed > 0 ? (int)(1000.0 / _settings.TextSpeed) : 30;
        for (var count = 1; count <= text.Length; count++)
        {
            ct.ThrowIfCancellationRequested();
            var visible = text[..count];
            await Dispatcher.UIThread.InvokeAsync(() => content.Text = visible.Replace("\\n", "\n"));
            if (delay > 0) await Task.Delay(delay, ct);
        }
    }
    public void Skip(string id) => _cts?.Cancel();
    public void Cancel() => _cts?.Cancel();
    public void SetVoice(string id) { }
}
