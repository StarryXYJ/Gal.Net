namespace GalNet.Sample.CrossPlatform.Views;

public partial class ScreenshotDialog : GalNet.Avalonia.GameView.Page.ScreenshotDialog
{
    public ScreenshotDialog() : this(null) { }

    private readonly GalNet.Avalonia.GameHost.AvaloniaPlayerSettingsService? _settings;
    private double _uiScale = 1;

    public ScreenshotDialog(GalNet.Avalonia.GameHost.AvaloniaPlayerSettingsService? settings)
    {
        _settings = settings;
        Styles.Add(SampleCustomization.CreateStyles());
        InitializeComponent();
        PlayerTypography.Apply(Resources, settings?.RuntimeSettings.FontSize ?? PlayerTypography.Default);
        InitializeDialog();
    }
    protected override global::Avalonia.Size MeasureOverride(global::Avalonia.Size availableSize)
    {
        var viewport = global::Avalonia.Controls.TopLevel.GetTopLevel(this)?.ClientSize ?? availableSize;
        var scale = PlayerLayout.UiScale(viewport);
        if (_uiScale != scale)
        {
            _uiScale = scale;
            PlayerTypography.Apply(Resources, _settings?.RuntimeSettings.FontSize ?? PlayerTypography.Default, scale);
        }
        return base.MeasureOverride(availableSize);
    }
}
