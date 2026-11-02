using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameHost.Settings;
using GalNet.Avalonia.GameView.Page;
using GalNet.Game.Controls;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class PlayerView : GalNet.Avalonia.GameHost.Views.PlayerSurface
{
    public PlayerView() { InitializeComponent(); InitializeSurface(); }

    private GameShell? _responsiveShell;
    private IDisposable? _shellVisibility;
    private bool _proportionalPages = true;

    public void AttachResponsiveShell(GameShell shell)
    {
        var canvas = this.FindControl<GameCanvasHost>("GameCanvas")!;
        canvas.GameContent = null;
        _responsiveShell = shell;
        PageDesign.Children.Add(shell);
        _shellVisibility = shell.Bind(IsVisibleProperty, new Binding(nameof(IsVisible)) { Source = canvas });
    }

    public void DetachResponsiveShell()
    {
        _shellVisibility?.Dispose();
        _shellVisibility = null;
        if (_responsiveShell is not { } shell) return;
        PageDesign.Children.Remove(shell);
        _responsiveShell = null;
    }

    public void UseProportionalPages(bool enabled)
    {
        if (_proportionalPages == enabled) return;
        _proportionalPages = enabled;
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_responsiveShell is not null && double.IsFinite(availableSize.Width) && double.IsFinite(availableSize.Height))
        {
            var mobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
            var scale = _proportionalPages ? PlayerLayout.UiScale(availableSize, mobile, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1) : 1;
            PageDesign.Width = availableSize.Width / scale;
            PageDesign.Height = availableSize.Height / scale;
        }
        return base.MeasureOverride(availableSize);
    }

    public void ApplyTypography(FontSizePreset preset)
    {
        PlayerTypography.Apply(Resources, preset);
        if (_responsiveShell is { } shell)
            PlayerTypography.Apply(shell.Resources, preset);
    }

    public bool CloseToolbarMenu() => this.GetVisualDescendants().OfType<GamePage>().Any(page => page.CloseToolbarMenu());
}
