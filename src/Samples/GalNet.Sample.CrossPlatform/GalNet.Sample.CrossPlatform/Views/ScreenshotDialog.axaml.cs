namespace GalNet.Sample.CrossPlatform.Views;

public partial class ScreenshotDialog : GalNet.Avalonia.GameView.Page.ScreenshotDialog
{
    public ScreenshotDialog() { Styles.Add(SampleCustomization.CreateStyles()); InitializeComponent(); InitializeDialog(); }
}
