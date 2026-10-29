using Avalonia;
using Avalonia.Controls.Primitives;

namespace GalNet.Game.Controls;

/// <summary>Templatable ADV dialogue surface with a named typewriter part.</summary>
public class DialoguePresenter : TemplatedControl
{
    public static readonly StyledProperty<bool> CanReplayVoiceProperty = AvaloniaProperty.Register<DialoguePresenter, bool>(nameof(CanReplayVoice));
    public static readonly StyledProperty<System.Windows.Input.ICommand?> ReplayVoiceCommandProperty = AvaloniaProperty.Register<DialoguePresenter, System.Windows.Input.ICommand?>(nameof(ReplayVoiceCommand));
    public static readonly StyledProperty<string> ReplayIconProperty = AvaloniaProperty.Register<DialoguePresenter, string>(nameof(ReplayIcon), "🔊");
    public static readonly StyledProperty<string> ReplayLabelProperty = AvaloniaProperty.Register<DialoguePresenter, string>(nameof(ReplayLabel), "Replay voice");
    public bool CanReplayVoice { get => GetValue(CanReplayVoiceProperty); set => SetValue(CanReplayVoiceProperty, value); }
    public System.Windows.Input.ICommand? ReplayVoiceCommand { get => GetValue(ReplayVoiceCommandProperty); set => SetValue(ReplayVoiceCommandProperty, value); }
    public string ReplayIcon { get => GetValue(ReplayIconProperty); set => SetValue(ReplayIconProperty, value); }
    public string ReplayLabel { get => GetValue(ReplayLabelProperty); set => SetValue(ReplayLabelProperty, value); }
    public static readonly StyledProperty<string?> SpeakerProperty =
        AvaloniaProperty.Register<DialoguePresenter, string?>(nameof(Speaker));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<DialoguePresenter, string?>(nameof(Text));

    public static readonly StyledProperty<double> CharactersPerSecondProperty =
        AvaloniaProperty.Register<DialoguePresenter, double>(nameof(CharactersPerSecond), 30d);

    private TypewriterTextBlock? _typewriter;

    public string? Speaker
    {
        get => GetValue(SpeakerProperty);
        set => SetValue(SpeakerProperty, value);
    }

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
        // The first dialogue is dispatched immediately after navigating to GamePage.
        // Ensure the visual template has created its typewriter part before treating
        // the line as presented; silently returning a completed task would make the
        // blocking dialogue instance finish and let the engine run to the ending.
        ApplyTemplate();
        return (_typewriter ?? throw new InvalidOperationException(
            "DialoguePresenter requires the PART_Typewriter template part."))
            .StartAsync(cancellationToken);
    }

    public void Skip() => _typewriter?.Skip();
    public void RevealAll() => _typewriter?.RevealAll();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _typewriter = e.NameScope.Find("PART_Typewriter") as TypewriterTextBlock;
    }
}
