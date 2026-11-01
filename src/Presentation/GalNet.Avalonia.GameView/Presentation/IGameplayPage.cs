namespace GalNet.Avalonia.GameView.Presentation;

/// <summary>Connects an application page to its borrowed runtime presentation state.</summary>
public interface IGameplayPage
{
    GamePresentationState State { get; }
}
