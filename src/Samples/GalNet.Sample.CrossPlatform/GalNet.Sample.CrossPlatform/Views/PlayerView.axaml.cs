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
    private bool? _compactLayout;
    private FontSizePreset _fontSize = PlayerTypography.Default;
    private double _uiScale = 1;
    private readonly List<(string Key, bool Exists, object? Value)> _layoutResources = [];

    public void AttachResponsiveShell(GameShell shell)
    {
        var canvas = this.FindControl<GameCanvasHost>("GameCanvas")!;
        var content = this.FindControl<Grid>("PlayerContent")!;
        canvas.GameContent = null;
        _responsiveShell = shell;
        content.Children.Insert(1, shell);
        _shellVisibility = shell.Bind(IsVisibleProperty, new Binding(nameof(IsVisible)) { Source = canvas });
    }

    public void DetachResponsiveShell()
    {
        _shellVisibility?.Dispose();
        _shellVisibility = null;
        if (_responsiveShell is not { } shell) return;
        this.FindControl<Grid>("PlayerContent")!.Children.Remove(shell);
        shell.Classes.Remove("compact-player");
        RestoreLayoutResources(shell);
        Classes.Remove("compact-player");
        _compactLayout = null;
        _responsiveShell = null;
    }

    private void RestoreLayoutResources(GameShell shell)
    {
        foreach (var (key, exists, value) in _layoutResources)
        {
            if (exists) shell.Resources[key] = value;
            else shell.Resources.Remove(key);
        }
        _layoutResources.Clear();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_responsiveShell is { } shell && double.IsFinite(availableSize.Width) && double.IsFinite(availableSize.Height))
        {
            var scale = PlayerLayout.UiScale(availableSize);
            if (_uiScale != scale)
            {
                _uiScale = scale;
                ApplyTypography(_fontSize);
            }
            var compact = PlayerLayout.IsCompact(availableSize);
            if (_compactLayout != compact)
            {
                _compactLayout = compact;
                Classes.Set("compact-player", compact);
                shell.Classes.Set("compact-player", compact);
                RestoreLayoutResources(shell);
                if (compact)
                {
                    SetLayoutResource(shell, "PageHeaderMinHeight", 52d);
                    SetLayoutResource(shell, "PageHeaderSpacing", 12d);
                    SetLayoutResource(shell, "ContentMargin", new Thickness(0, 12, 0, 0));
                    SetLayoutResource(shell, "RuleMargin", new Thickness(0, 8, 0, 0));
                }
            }
        }
        return base.MeasureOverride(availableSize);
    }

    public void ApplyTypography(FontSizePreset preset)
    {
        _fontSize = preset;
        PlayerTypography.Apply(Resources, preset, _uiScale);
        if (_responsiveShell is { } shell) PlayerTypography.Apply(shell.Resources, preset, _uiScale);
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
