namespace GalNet.Sample.CrossPlatform.Views;

public partial class ScreenshotDialog : GalNet.Avalonia.GameView.Page.ScreenshotDialog
{
    public ScreenshotDialog() : this(null) { }


    public ScreenshotDialog(GalNet.Avalonia.GameHost.AvaloniaPlayerSettingsService? settings)
    {
        Styles.Add(SampleCustomization.CreateStyles());
        InitializeComponent();
        PlayerTypography.Apply(Resources, settings?.RuntimeSettings.FontSize ?? PlayerTypography.Default);
        InitializeDialog();
    }
}
