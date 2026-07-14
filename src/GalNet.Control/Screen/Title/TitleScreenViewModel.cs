using System.Collections.ObjectModel;
using GalNet.Core.Services;

namespace GalNet.Control.Screen.BuiltIn;

/// <summary>
/// Button item for title screen menu.
/// </summary>
public sealed class TitleMenuButton
{
    public string Text { get; init; } = "";
    public Action? Action { get; init; }
}

/// <summary>
/// ViewModel for TitleScreenView — injected with INavigationHost.
/// </summary>
public sealed class TitleScreenViewModel
{
    private readonly INavigationHost _nav;

    public string GameTitle { get; private set; } = "GalNet Demo";
    public ObservableCollection<TitleMenuButton> Buttons { get; } = [];

    public TitleScreenViewModel(INavigationHost nav)
    {
        _nav = nav;
    }

    public void Initialize(string gameTitle, TitleMenuButton[] buttons)
    {
        GameTitle = gameTitle;
        Buttons.Clear();
        foreach (var btn in buttons)
            Buttons.Add(btn);
    }
}
