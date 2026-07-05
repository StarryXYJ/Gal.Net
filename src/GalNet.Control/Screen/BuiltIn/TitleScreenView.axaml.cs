using Avalonia.Controls;
using GalNet.Core.Screen;
using Serilog;

namespace GalNet.Control.Screen.BuiltIn;

/// <summary>
/// Title screen — implements ITitleScreen, supports optional ViewModel binding.
/// </summary>
public partial class TitleScreenView : UserControl, ITitleScreen
{
    public string Category => "Title";

    public event Action<int>? ButtonClicked;

    public TitleScreenView()
    {
        InitializeComponent();
        Log.Debug("TitleScreenView constructed");
    }

    /// <summary>Set title text.</summary>
    public void SetTitle(string title)
    {
        TitleBlock.Text = title;
        Log.Debug("TitleScreenView.SetTitle: {Title}", title);
    }

    /// <summary>Set buttons.</summary>
    public void SetButtons(string[] buttonTexts)
    {
        Log.Debug("TitleScreenView.SetButtons: count={Count}, texts=[{Texts}]",
            buttonTexts.Length, string.Join(", ", buttonTexts));

        ButtonPanel.Children.Clear();
        for (int i = 0; i < buttonTexts.Length; i++)
        {
            var index = i;
            var btn = new Button
            {
                Content = buttonTexts[i],
                Width = 260,
                Height = 50,
                FontSize = 20,
                Foreground = Avalonia.Media.Brushes.White,
                Background = Avalonia.Media.SolidColorBrush.Parse("#334"),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new Avalonia.CornerRadius(4),
                Margin = new Avalonia.Thickness(0, 6),
            };
            btn.Click += (_, _) =>
            {
                Log.Debug("TitleScreen button clicked: index={Index}", index);
                ButtonClicked?.Invoke(index);
            };
            ButtonPanel.Children.Add(btn);
            Log.Debug("  added button[{Index}]: '{Text}'", i, buttonTexts[i]);
        }
        Log.Debug("SetButtons done, panel children={Count}", ButtonPanel.Children.Count);
    }

    /// <summary>Initialize with ViewModel for data binding.</summary>
    public void BindToViewModel(TitleScreenViewModel vm)
    {
        DataContext = vm;

        TitleBlock.Bind(TextBlock.TextProperty,
            new Avalonia.Data.Binding(nameof(vm.GameTitle)));

        ButtonPanel.Children.Clear();
        foreach (var btnVm in vm.Buttons)
        {
            var btn = new Button
            {
                Content = btnVm.Text,
                Width = 260,
                Height = 50,
                FontSize = 20,
                Foreground = Avalonia.Media.Brushes.White,
                Background = Avalonia.Media.SolidColorBrush.Parse("#334"),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new Avalonia.CornerRadius(4),
                Margin = new Avalonia.Thickness(0, 6),
            };
            btn.Click += (_, _) => btnVm.Action?.Invoke();
            ButtonPanel.Children.Add(btn);
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Log.Debug("TitleScreenView attached to visual tree: Bounds={Bounds}, Buttons={Count}",
            Bounds, ButtonPanel.Children.Count);
    }
}
