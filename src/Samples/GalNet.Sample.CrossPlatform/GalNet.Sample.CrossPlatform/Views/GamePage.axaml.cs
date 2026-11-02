using Avalonia.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using GalNet.Avalonia.GameView.Presentation;

namespace GalNet.Sample.CrossPlatform.Views;

public partial class GamePage : GalNet.Avalonia.GameView.Page.GamePage
{
    private Border? _toolbar;
    private WrapPanel? _fullToolbar;
    private StackPanel? _compactToolbar;
    private ContentPresenter? _dialogue;
    private MenuFlyout? _more;
    private Border? _historySlot;
    private Button? _historyButton;
    private MenuItem? _historyMenu;
    public GamePage(GamePresentationState state)
    {
        Presentation = state;
        InitializeComponent();
        Theme = (ControlTheme)Resources["SampleGameLayout"]!;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_more is not null) _more.Opened -= MoreOpened;
        base.OnApplyTemplate(e);
        _toolbar = e.NameScope.Find<Border>("ToolbarFrame");
        _fullToolbar = e.NameScope.Find<WrapPanel>("FullToolbar");
        _compactToolbar = e.NameScope.Find<StackPanel>("CompactToolbar");
        _dialogue = e.NameScope.Find<ContentPresenter>("PART_Dialogue");
        _more = e.NameScope.Find<Button>("MoreButton")?.Flyout as MenuFlyout;
        _historySlot = e.NameScope.Find<Border>("CompactHistorySlot");
        _historyButton = e.NameScope.Find<Button>("CompactHistory");
        _historyMenu = _more?.Items.OfType<MenuItem>().FirstOrDefault(item => item.Name == "CompactHistoryMenu");
        if (_more is not null) _more.Opened += MoreOpened;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_toolbar is not null && _fullToolbar is not null && _compactToolbar is not null &&
            double.IsFinite(availableSize.Width) && double.IsFinite(availableSize.Height))
        {
            var mobile = PlayerLayout.GetIsMobile(this);
            var scale = Math.Min(availableSize.Width / 1920, availableSize.Height / 1080);
            var actionSize = (double?)this.FindResource("GalNet.Theme.ActionFontSize") ?? 18;
            var mobileFont = Math.Clamp(actionSize * Math.Max(scale, .5), 12, 18);
            foreach (var button in this.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("player-link")))
            {
                var wasMobile = button.Classes.Contains("mobile-target");
                button.Classes.Set("mobile-target", mobile);
                if (mobile)
                {
                    button.SetCurrentValue(TemplatedControl.FontSizeProperty, mobileFont);
                    button.Margin = new Thickness(4, 0);
                }
                else if (wasMobile)
                {
                    button.ClearValue(TemplatedControl.FontSizeProperty);
                    button.Margin = new Thickness(4);
                }
            }
            if (mobile && _more is not null)
                foreach (var item in _more.Items.OfType<MenuItem>())
                    item.FontSize = Math.Max(14, mobileFont);
            if (mobile)
            {
                var width = Math.Max(1, availableSize.Width - 16);
                var fullWidth = MeasureButtons(_fullToolbar);
                var compact = fullWidth > width;
                _fullToolbar.IsVisible = !compact;
                _compactToolbar.IsVisible = compact;
                if (!compact || !_toolbar.IsVisible) CloseToolbarMenu();
                var requiredWidth = fullWidth;
                if (compact)
                {
                    requiredWidth = MeasureButtons(_compactToolbar);
                    if (_historySlot is not null && _historyButton is not null)
                    {
                        _historyButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        var historyWidth = _historyButton.DesiredSize.Width;
                        var expandedWidth = requiredWidth - (_historySlot.IsVisible ? _historySlot.DesiredSize.Width : 0) + historyWidth;
                        var showHistory = expandedWidth <= width;
                        _historySlot.IsVisible = showHistory;
                        if (_historyMenu is not null) _historyMenu.IsVisible = !showHistory && Presentation?.HasHistory == true;
                        requiredWidth = showHistory ? expandedWidth : expandedWidth - historyWidth;
                    }
                }
                _toolbar.Margin = new Thickness(8, 0);
                _toolbar.Width = Math.Min(width, Math.Max(Math.Min(320, 1920 * scale), requiredWidth));
                Panel activeToolbar = compact ? _compactToolbar : _fullToolbar;
                var toolbarHeight = activeToolbar.Children.Where(child => child.IsVisible)
                    .Select(child => child.DesiredSize.Height).DefaultIfEmpty(32).Max();
                var sceneBottom = (availableSize.Height + 1080 * scale) / 2;
                var clearance = _toolbar.IsVisible ? sceneBottom - (availableSize.Height - toolbarHeight) + 4 : 0;
                if (_dialogue is not null && scale > 0)
                    _dialogue.Margin = new Thickness(64, 0, 64, Math.Max(70, clearance / scale));
            }
            else
            {
                _fullToolbar.IsVisible = true;
                _compactToolbar.IsVisible = false;
                _toolbar.Margin = new Thickness(20);
                _toolbar.Width = double.NaN;
                if (_dialogue is not null) _dialogue.Margin = new Thickness(64, 0, 64, 70);
            }
        }
        return base.MeasureOverride(availableSize);
    }

    private static double MeasureButtons(Panel panel)
    {
        var width = 0d;
        foreach (var child in panel.Children.Where(child => child.IsVisible))
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width += child.DesiredSize.Width;
        }
        return width;
    }

    private void MoreOpened(object? sender, EventArgs e) => Presentation?.CancelPlayback();

    public bool CloseToolbarMenu()
    {
        if (_more?.IsOpen != true) return false;
        _more.Hide();
        return true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CloseToolbarMenu();
        base.OnDetachedFromVisualTree(e);
    }
}
