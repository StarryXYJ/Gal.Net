using System.ComponentModel;
using System.Runtime.CompilerServices;
using GalNet.Control.Abstraction.UI;

namespace GalNet.Control.UI;

/// <summary>Routes only between the fixed built-in game screens.</summary>
public sealed class GameScreenNavigator(Func<string, object?, object> build) : IGameScreenNavigator
{
    private readonly Stack<object> _backStack = [];
    private object? _current;
    public event PropertyChangedEventHandler? PropertyChanged;
    public object? Current { get => _current; private set { _current = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanGoBack)); } }
    public bool CanGoBack => _backStack.Count > 0;

    public Task NavigateAsync(string screen, object? parameter = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_current is not null) _backStack.Push(_current);
        Current = build(screen, parameter);
        return Task.CompletedTask;
    }

    public Task GoBackAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_backStack.TryPop(out var previous)) Current = previous;
        return Task.CompletedTask;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
