using Avalonia.Controls;
using GalNet.Core.Screen;
using GalNet.Core.UI;
using Serilog;

namespace GalNet.Control.Screen.BuiltIn;

/// <summary>
/// Title screen — implements ITitleScreen, supports optional ViewModel binding.
/// </summary>
public partial class TitleScreenView : UserControl, ITitleScreen
{
    private IColorPalette? _palette;
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
                Width = 340,
                Height = 64,
                FontSize = 24,
                Foreground = Avalonia.Media.Brushes.White,
                Background = Avalonia.Media.SolidColorBrush.Parse("#334"),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new Avalonia.CornerRadius(4),
                Margin = new Avalonia.Thickness(0, 8),
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

    /// <summary>Uses palette keys instead of copying colours so an open preview updates in place.</summary>
    public void SetPalette(IColorPalette palette)
    {
        if (_palette is not null) _palette.ColorChanged -= OnPaletteChanged;
        _palette = palette;
        _palette.ColorChanged += OnPaletteChanged;
        ApplyPalette();
    }

    private void OnPaletteChanged(string _) => ApplyPalette();
    private void ApplyPalette()
    {
        if (_palette is null) return;
        Background = Avalonia.Media.SolidColorBrush.Parse(_palette.Resolve("surface", "#CC0A0A1E"));
        foreach (var button in ButtonPanel.Children.OfType<Button>())
        {
            button.Background = Avalonia.Media.SolidColorBrush.Parse(_palette.Resolve("accent", "#334"));
            button.Foreground = Avalonia.Media.SolidColorBrush.Parse(_palette.Resolve("text", "#FFFFFFFF"));
        }
        TitleBlock.Foreground = Avalonia.Media.SolidColorBrush.Parse(_palette.Resolve("text", "#FFFFFFFF"));
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (_palette is not null) _palette.ColorChanged -= OnPaletteChanged;
        base.OnDetachedFromVisualTree(e);
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
                Width = 340,
                Height = 64,
                FontSize = 24,
                Foreground = Avalonia.Media.Brushes.White,
                Background = Avalonia.Media.SolidColorBrush.Parse("#334"),
                BorderThickness = new Avalonia.Thickness(0),
                CornerRadius = new Avalonia.CornerRadius(4),
                Margin = new Avalonia.Thickness(0, 8),
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
