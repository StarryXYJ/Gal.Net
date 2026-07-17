using Avalonia.Media;

namespace GalNet.Control.UI;

public sealed class TitleUiConfiguration
{
    public Color BackgroundColor { get; set; } = Color.Parse("#FF111118"); public string? BackgroundImage { get; set; }
    public string BackgroundStretch { get; set; } = "uniformToFill"; public double ContentPadding { get; set; }
    public Color TitleColor { get; set; } = Colors.White; public double TitleFontSize { get; set; } = 48;
    public Color MenuTextColor { get; set; } = Colors.White; public double MenuFontSize { get; set; } = 20;
    public double TitleMenuGap { get; set; } = 14; public Color ButtonColor { get; set; } = Color.Parse("#FF8ED8FF");
    public Color ButtonTextColor { get; set; } = Color.Parse("#FF111118"); public Color ButtonHoverColor { get; set; } = Color.Parse("#FFB5E7FF");
    public double ButtonWidth { get; set; } = 260; public double ButtonHeight { get; set; } = 50; public double MenuSpacing { get; set; } = 12;
    public bool ShowGallery { get; set; } = true; public bool ShowAbout { get; set; } = true; public Color MenuHoverTextColor { get; set; } = Color.Parse("#FF8ED8FF");
}
public sealed class GameUiConfiguration
{
    public Color DialogueBackgroundColor { get; set; } = Color.Parse("#CC292933"); public string? DialogueBackgroundImage { get; set; }
    public double DialogueBackgroundImageOpacity { get; set; } = 1; public Color DialogueTextColor { get; set; } = Colors.White; public Color SpeakerTextColor { get; set; } = Color.Parse("#FF8ED8FF");
    public double DialogueHeight { get; set; } = 160; public double DialogueMargin { get; set; } = 20; public double DialogueCornerRadius { get; set; } = 8; public double DialogueFontSize { get; set; } = 16;
    public string ChoiceLayout { get; set; } = "vertical"; public Color ChoiceButtonColor { get; set; } = Color.Parse("#FF292933"); public Color ChoiceButtonTextColor { get; set; } = Colors.White;
    public double ChoiceButtonWidth { get; set; } = 240; public double ChoiceButtonHeight { get; set; } = 44; public double ChoiceSpacing { get; set; } = 8; public bool CommandBarVisible { get; set; } = true;
    public Color CommandTextColor { get; set; } = Color.Parse("#FFC8C8D0"); public Color CommandHoverTextColor { get; set; } = Color.Parse("#FF8ED8FF"); public Color CommandSelectedTextColor { get; set; } = Color.Parse("#FF8ED8FF");
}
public class SettingsUiConfiguration
{
    public Color BackgroundColor { get; set; } = Color.Parse("#FF111118"); public Color PanelColor { get; set; } = Color.Parse("#FF292933"); public Color TextColor { get; set; } = Colors.White;
    public Color ButtonColor { get; set; } = Color.Parse("#FF292933"); public Color ButtonTextColor { get; set; } = Colors.White; public Color BackButtonForegroundColor { get; set; } = Colors.White;
    public Color SliderTrackColor { get; set; } = Color.Parse("#665F6075"); public Color SliderFillColor { get; set; } = Color.Parse("#FF8ED8FF"); public Color SliderThumbColor { get; set; } = Colors.White;
    public Color SliderThumbBorderColor { get; set; } = Color.Parse("#665F6075"); public Color CheckBoxBorderColor { get; set; } = Color.Parse("#FF989AAF"); public Color CheckBoxFillColor { get; set; } = Color.Parse("#FF8ED8FF"); public Color CheckBoxCheckColor { get; set; } = Color.Parse("#FF111118");
}
public sealed class SaveLoadUiConfiguration : SettingsUiConfiguration { }
public sealed class GalleryUiConfiguration : SettingsUiConfiguration { }
public sealed class AboutUiConfiguration
{
    public string? ContentAsset { get; set; } public Color BackgroundColor { get; set; } = Color.Parse("#FF111118"); public Color PanelColor { get; set; } = Color.Parse("#FF292933");
    public double ContentPadding { get; set; } = 20; public double FontSize { get; set; } = 16; public Color TextColor { get; set; } = Colors.White; public Color HeadingColor { get; set; } = Colors.White;
    public Color SelectionColor { get; set; } = Color.Parse("#668ED8FF"); public Color LinkColor { get; set; } = Color.Parse("#FF8ED8FF"); public Color LinkHoverColor { get; set; } = Color.Parse("#FFB5E7FF"); public Color LinkVisitedColor { get; set; } = Color.Parse("#FFC8C8D0");
    public Color BlockquoteBackgroundColor { get; set; } = Color.Parse("#FF292933"); public Color BlockquoteBorderColor { get; set; } = Color.Parse("#FF8ED8FF"); public Color CodeBackgroundColor { get; set; } = Color.Parse("#FF292933"); public Color CodeBorderColor { get; set; } = Color.Parse("#FF8ED8FF"); public Color CodeTextColor { get; set; } = Colors.White; public double CodeFontSize { get; set; } = 16; public Color RuleColor { get; set; } = Color.Parse("#FF989AAF"); public Color BackButtonForegroundColor { get; set; } = Colors.White;
}
