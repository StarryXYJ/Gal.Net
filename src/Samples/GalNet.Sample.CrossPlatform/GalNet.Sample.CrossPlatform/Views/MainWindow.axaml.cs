using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost;
using Ursa.Controls;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class MainWindow : UrsaWindow
{
    private bool _fullscreen;
    private bool _windowedCanResize;
    private bool _windowedTitleBar;
    private WindowDecorations _windowedDecorations;
    private double _windowedTitleHeight;
    private bool _windowedExtendClientArea;

    public MainWindow()
    {
        InitializeComponent();
        WindowDecorationProperties.SetElementRole(FitAspectButton, WindowDecorationsElementRole.User);
        Resources.MergedDictionaries.Add(SampleCustomization.PlayerPalette.CreateResources());
        if (OperatingSystem.IsWindows()) Win32Properties.AddWndProcHookCallback(this, FilterFullscreenMessages);
        Closed += (_, _) =>
        {
            if (OperatingSystem.IsWindows()) Win32Properties.RemoveWndProcHookCallback(this, FilterFullscreenMessages);
            DetachDebugBindings();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty) return;
        var fullscreen = WindowState == WindowState.FullScreen;
        if (fullscreen == _fullscreen) return;
        _fullscreen = fullscreen;
        if (fullscreen)
        {
            _windowedCanResize = CanResize;
            _windowedTitleBar = IsTitleBarVisible;
            _windowedDecorations = WindowDecorations;
            _windowedTitleHeight = ExtendClientAreaTitleBarHeightHint;
            _windowedExtendClientArea = ExtendClientAreaToDecorationsHint;
            CanResize = false;
            IsTitleBarVisible = false;
            WindowDecorations = WindowDecorations.None;
            ExtendClientAreaTitleBarHeightHint = 0;
            ExtendClientAreaToDecorationsHint = false;
        }
        else
        {
            CanResize = _windowedCanResize;
            IsTitleBarVisible = _windowedTitleBar;
            WindowDecorations = _windowedDecorations;
            ExtendClientAreaTitleBarHeightHint = _windowedTitleHeight;
            ExtendClientAreaToDecorationsHint = _windowedExtendClientArea;
        }
        if (this.FindControl<Button>("FitAspectButton") is { } fit) fit.IsVisible = !fullscreen;
    }

    private void OnFitAspect(object? sender, RoutedEventArgs args)
    {
        if (WindowState == WindowState.FullScreen) return;
        WindowState = WindowState.Normal;
        // Restore from maximized first, then measure the actual content below the title bar.
        Dispatcher.UIThread.Post(FitAspect, DispatcherPriority.Loaded);
    }

    private void FitAspect()
    {
        if (WindowState != WindowState.Normal || !IsVisible) return;
        UpdateLayout();
        var content = Content as Control;
        if (content is null || content.Bounds.Width <= 0 || content.Bounds.Height <= 0) return;
        var canvas = content.GetVisualDescendants().OfType<GalNet.Game.Controls.GameCanvasHost>().FirstOrDefault();
        var ratio = canvas is { DesignWidth: > 0, DesignHeight: > 0 } ? (double)canvas.DesignWidth / canvas.DesignHeight : 16d / 9;
        var chrome = new Size(Math.Max(0, ClientSize.Width - content.Bounds.Width), Math.Max(0, ClientSize.Height - content.Bounds.Height));
        var screen = Screens.ScreenFromWindow(this);
        var frame = FrameSize ?? ClientSize;
        var frameWidth = Math.Max(0, frame.Width - ClientSize.Width);
        var frameHeight = Math.Max(0, frame.Height - ClientSize.Height);
        var available = screen is null ? ClientSize : new Size(
            Math.Max(1, screen.WorkingArea.Width / RenderScaling - frameWidth),
            Math.Max(1, screen.WorkingArea.Height / RenderScaling - frameHeight));
        var size = WindowAspectRatio.Fit(content.Bounds.Size, chrome, new Size(MinWidth, MinHeight), available, ratio);
        Width = size.Width;
        Height = size.Height;
        if (screen is not null)
        {
            var area = screen.WorkingArea;
            Position = new PixelPoint(Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling((size.Width + frameWidth) * RenderScaling))),
                Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling((size.Height + frameHeight) * RenderScaling))));
        }
    }

    partial void DetachDebugBindings();
}
