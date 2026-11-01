using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Ursa.Controls;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class MainWindow
{
    private TitleBar? _captionTitleBar;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _captionTitleBar = e.NameScope.Find<TitleBar>("PART_TitleBar");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Ursa 2.1 reserves caption space only in OnLoaded, after the first frame.
        // Measure the actual themed buttons before measuring the client title bar instead.
        ReserveCaptionSpace();
        return base.MeasureOverride(availableSize);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Keep the measured inset if Ursa's one-time bounds-based calculation overwrites it.
        ReserveCaptionSpace();
    }

    private void ReserveCaptionSpace()
    {
        if (_captionTitleBar is null) return;
        var decorations = this.GetLogicalDescendants().OfType<WindowDrawnDecorations>().FirstOrDefault();
        if (decorations?.Content?.Overlay is not Control overlay) return; // Native/headless hosts may not use drawn decorations.
        var inset = 0d;
        foreach (var button in overlay.GetVisualDescendants().OfType<Button>())
        {
            if (!button.IsEffectivelyVisible || WindowDecorationProperties.GetElementRole(button) is not
                (WindowDecorationsElementRole.MinimizeButton or WindowDecorationsElementRole.MaximizeButton or
                 WindowDecorationsElementRole.CloseButton or WindowDecorationsElementRole.FullScreenButton)) continue;
            button.Measure(Size.Infinity);
            inset += button.DesiredSize.Width;
        }
        var margin = TitleBarMargin;
        _captionTitleBar.Margin = new Thickness(margin.Left, margin.Top, margin.Right + inset, margin.Bottom);
        _captionTitleBar.MinHeight = decorations.TitleBarHeight;
    }
}
