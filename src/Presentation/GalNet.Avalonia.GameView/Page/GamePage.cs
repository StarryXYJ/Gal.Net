using System.Linq.Expressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.ViewModels;
using GalNet.Core.Primitives;
using GalNet.Game.Controls;
using GalNet.Rendering.Scene;

namespace GalNet.Avalonia.GameView.Page;

/// <summary>Stable game surface. Themes arrange its owned content using four required presenters.</summary>
public class GamePage : TemplatedControl
{
    protected override Type StyleKeyOverride => typeof(GamePage);

    public static readonly DirectProperty<GamePage, Control> SceneProperty =
        AvaloniaProperty.RegisterDirect<GamePage, Control>(nameof(Scene), page => page.Scene);
    public static readonly DirectProperty<GamePage, DialoguePresenter> DialogueProperty =
        AvaloniaProperty.RegisterDirect<GamePage, DialoguePresenter>(nameof(Dialogue), page => page.Dialogue);
    public static readonly DirectProperty<GamePage, NvlPresenter> NvlProperty =
        AvaloniaProperty.RegisterDirect<GamePage, NvlPresenter>(nameof(Nvl), page => page.Nvl);
    public static readonly DirectProperty<GamePage, ChoiceList> ChoiceProperty =
        AvaloniaProperty.RegisterDirect<GamePage, ChoiceList>(nameof(Choice), page => page.Choice);
    public static readonly DirectProperty<GamePage, Control> HistoryProperty =
        AvaloniaProperty.RegisterDirect<GamePage, Control>(nameof(History), page => page.History);

    /// <summary>Marks custom interactive regions that must not advance the story.</summary>
    public static readonly AttachedProperty<bool> IsPlayerInteractionProperty =
        AvaloniaProperty.RegisterAttached<GamePage, Control, bool>("IsPlayerInteraction");
    public static readonly AttachedProperty<PlaybackMode?> PlaybackToggleProperty =
        AvaloniaProperty.RegisterAttached<GamePage, Control, PlaybackMode?>("PlaybackToggle");
    public static PlaybackMode? GetPlaybackToggle(Control control) => control.GetValue(PlaybackToggleProperty);
    public static void SetPlaybackToggle(Control control, PlaybackMode? value) => control.SetValue(PlaybackToggleProperty, value);

    public Control Scene { get; }
    public SceneLayerHost SceneHost { get; } = new();
    public DialoguePresenter Dialogue { get; } = new();
    public NvlPresenter Nvl { get; } = new();
    public ChoiceList Choice { get; } = new();
    public Control History { get; } = new DialogueHistoryOverlay();

    public GamePage()
    {
        Scene = new Grid { ClipToBounds = true, Children = { SceneHost } };
        BindContent(SceneHost, SceneLayerHost.ItemsSourceProperty, page => ((GamePageViewModel)page.DataContext!).Layers);
        BindContent(SceneHost, SceneLayerHost.EffectsSourceProperty, page => ((GamePageViewModel)page.DataContext!).TextureEffects);
        BindContent(SceneHost, SceneLayerHost.RenderablesSourceProperty, page => ((GamePageViewModel)page.DataContext!).SceneRenderables);
        BindContent(Dialogue, IsVisibleProperty, page => ((GamePageViewModel)page.DataContext!).IsDialogueOverlayVisible);
        BindContent(Dialogue, ReadTextStyle.ForegroundProperty, page => ((GamePageViewModel)page.DataContext!).ReadTextBrush);
        BindContent(Nvl, ReadTextStyle.ForegroundProperty, page => ((GamePageViewModel)page.DataContext!).ReadTextBrush);
        BindContent(History, ReadTextStyle.ForegroundProperty, page => ((GamePageViewModel)page.DataContext!).ReadTextBrush);
        BindContent(Dialogue, DialoguePresenter.CanReplayVoiceProperty, page => ((GamePageViewModel)page.DataContext!).CanReplayDialogue);
        BindContent(Dialogue, DialoguePresenter.ReplayVoiceCommandProperty, page => ((GamePageViewModel)page.DataContext!).ReplayDialogueCommand);
        BindContent(Dialogue, DialoguePresenter.ReplayIconProperty, page => ((GamePageViewModel)page.DataContext!).ReplayIcon);
        if (MarkupExtensions.PlayerTextExtension.CreateBinding("History.ReplayVoice", "Replay voice") is ReflectionBinding replayLabel)
            Dialogue.Bind(DialoguePresenter.ReplayLabelProperty, replayLabel);
        BindContent(Nvl, IsVisibleProperty, page => ((GamePageViewModel)page.DataContext!).IsNvlOverlayVisible);
        BindContent(Choice, IsVisibleProperty, page => ((GamePageViewModel)page.DataContext!).IsChoiceOverlayVisible);
        BindContent(Choice, ItemsControl.ItemsSourceProperty, page => ((GamePageViewModel)page.DataContext!).Choices);
        BindContent(Choice, ChoiceList.SelectionCommandProperty, page => ((GamePageViewModel)page.DataContext!).CompleteChoiceCommand);
        AddHandler(PointerPressedEvent, OnPagePointerPressed, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, args) =>
        {
            if (DataContext is not GamePageViewModel game) return;
            game.ClearPlaybackPointer();
            if (game.IsHistoryOpen && args.Key == Key.Escape) { game.CloseHistoryCommand.Execute(null); args.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }

    public static bool GetIsPlayerInteraction(Control control) => control.GetValue(IsPlayerInteractionProperty);
    public static void SetIsPlayerInteraction(Control control, bool value) => control.SetValue(IsPlayerInteractionProperty, value);

    public byte[] CaptureScenePng() => SceneHost.CapturePng();

    /// <summary>Fail explicitly before dispatching a blocking presentation to an incomplete layout.</summary>
    public void EnsureLayout()
    {
        ApplyTemplate();
        if (Template is null)
            throw new InvalidOperationException("GamePage requires a layout theme. Apply the selected game layout theme to the player root.");
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        RequirePresenter(e, "PART_Scene", Scene);
        RequirePresenter(e, "PART_Dialogue", Dialogue);
        RequirePresenter(e, "PART_Nvl", Nvl);
        RequirePresenter(e, "PART_Choice", Choice);
        if (e.NameScope.Find<ContentPresenter>("PART_History") is { } history && !ReferenceEquals(history.Content, History))
            throw new InvalidOperationException("PART_History must present the owned History content.");
    }

    private static void RequirePresenter(TemplateAppliedEventArgs e, string name, Control content)
    {
        var presenter = e.NameScope.Find<ContentPresenter>(name)
            ?? throw new InvalidOperationException($"GamePage requires the {name} ContentPresenter template part.");
        if (!ReferenceEquals(presenter.Content, content))
            throw new InvalidOperationException($"GamePage {name} must present the corresponding owned content using TemplateBinding.");
    }

    private void BindContent<T>(AvaloniaObject target, AvaloniaProperty property, Expression<Func<GamePage, T>> path) =>
        target.Bind(property, CompiledBinding.Create(path, source: this));

    private void OnPagePointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (DataContext is not GamePageViewModel viewModel ||
            eventArgs.Pointer.Type == PointerType.Touch && viewModel.Input is not null ||
            eventArgs.Handled ||
            !eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var toggle = (eventArgs.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
            .Select(GetPlaybackToggle).FirstOrDefault(mode => mode is not null);
        viewModel.CancelPlaybackFromPointer(toggle);
        if (viewModel.IsSkipConfirmationOpen || viewModel.IsHistoryOpen || IsPlayerControl(eventArgs.Source)) return;

        viewModel.ObserveAdvancePointerPressed();
        if (viewModel.AdvanceCommand.CanExecute(null))
            viewModel.AdvanceCommand.Execute(null);
        eventArgs.Handled = true;
    }

    private static bool IsPlayerControl(object? source)
    {
        for (var current = source as Visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button or SelectingItemsControl or TextBox or RangeBase ||
                current is Control control && GetIsPlayerInteraction(control))
                return true;
        }
        return false;
    }
}
