using Ursa.Controls;

namespace GalNet.Avalonia.Debugging.Views;

/// <summary>Optional desktop window around the same embeddable log view.</summary>
public partial class DebugLogWindow : UrsaWindow
{
    public DebugLogWindow(DebugLogView view)
    {
        InitializeComponent();
        Content = view;
        view.CloseRequested += Close;
    }
}
