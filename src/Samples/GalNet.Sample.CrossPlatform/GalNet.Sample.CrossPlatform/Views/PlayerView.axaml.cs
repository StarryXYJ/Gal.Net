using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Page;
using GalNet.Game.Controls;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class PlayerView : GalNet.Avalonia.GameHost.Views.PlayerSurface
{
    public PlayerView() { InitializeComponent(); InitializeSurface(); }

    private GameShell? _responsiveShell;
    private IDisposable? _shellVisibility;
    private readonly List<(Button Button, double Height)> _modalTargets = [];
    private readonly List<(string Key, bool Exists, object? Value)> _layoutResources = [];

    public void AttachResponsiveShell(GameShell shell)
    {
        if (!PlayerLayout.GetIsMobile(this)) return;
        var canvas = this.FindControl<GameCanvasHost>("GameCanvas")!;
        var content = this.FindControl<Grid>("PlayerContent")!;
        canvas.GameContent = null;
        _responsiveShell = shell;
        Classes.Add("mobile-player");
        shell.Classes.Add("mobile-player");
        SetLayoutResource(shell, "PageHeaderMinHeight", 52d);
        SetLayoutResource(shell, "PageHeaderSpacing", 12d);
        SetLayoutResource(shell, "ContentMargin", new Thickness(0, 12, 0, 0));
        SetLayoutResource(shell, "RuleMargin", new Thickness(0, 8, 0, 0));
        content.Children.Insert(1, shell);
        _shellVisibility = shell.Bind(IsVisibleProperty, new Binding(nameof(IsVisible)) { Source = canvas });
        foreach (var name in new[] { "CancelExitButton", "ConfirmExitButton", "ConfirmReturnToTitleButton" })
        {
            var button = this.FindControl<Button>(name)!;
            _modalTargets.Add((button, button.MinHeight));
            button.MinHeight = Math.Max(48, button.MinHeight);
        }
    }

    public void DetachResponsiveShell()
    {
        _shellVisibility?.Dispose();
        _shellVisibility = null;
        foreach (var (button, height) in _modalTargets) button.MinHeight = height;
        _modalTargets.Clear();
        if (_responsiveShell is not { } shell) return;
        this.FindControl<Grid>("PlayerContent")!.Children.Remove(shell);
        shell.Classes.Remove("mobile-player");
        foreach (var (key, exists, value) in _layoutResources)
        {
            if (exists) shell.Resources[key] = value;
            else shell.Resources.Remove(key);
        }
        _layoutResources.Clear();
        Classes.Remove("mobile-player");
        _responsiveShell = null;
    }

    private void SetLayoutResource(GameShell shell, string key, object value)
    {
        key = "GalNet.Theme." + key;
        var exists = shell.Resources.TryGetValue(key, out var previous);
        _layoutResources.Add((key, exists, previous));
        shell.Resources[key] = value;
    }

    public bool CloseToolbarMenu() => this.GetVisualDescendants().OfType<GamePage>().Any(page => page.CloseToolbarMenu());
}
