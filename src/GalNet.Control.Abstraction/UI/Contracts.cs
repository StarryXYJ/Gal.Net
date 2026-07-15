using System.ComponentModel;

namespace GalNet.Control.Abstraction.UI;

/// <summary>Bindable navigation state for GalNet's fixed built-in screens.</summary>
public interface IGameScreenNavigator : INotifyPropertyChanged
{
    object? Current { get; }
    bool CanGoBack { get; }
    Task NavigateAsync(string screen, object? parameter = null, CancellationToken cancellationToken = default);
    Task GoBackAsync(CancellationToken cancellationToken = default);
}
