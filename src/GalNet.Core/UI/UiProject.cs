using Avalonia.Media;

namespace GalNet.Core.UI;

/// <summary>Serializable configuration for GalNet's fixed built-in game screens.</summary>
public sealed class UiProject
{
    public int Version { get; set; } = 1;
    public TitleUiConfiguration Title { get; set; } = new();
    public GameUiConfiguration Game { get; set; } = new();
    public SettingsUiConfiguration Settings { get; set; } = new();
    public SaveLoadUiConfiguration SaveLoad { get; set; } = new();
    public GalleryUiConfiguration Gallery { get; set; } = new();
}

public sealed class TitleUiConfiguration
{
    public Color BackgroundColor { get; set; } = Color.Parse("#FF111118");
    public string? BackgroundImage { get; set; }
    public Color TitleColor { get; set; } = Colors.White;
    public double TitleFontSize { get; set; } = 48;
    public Color ButtonColor { get; set; } = Color.Parse("#FF8ED8FF");
    public Color ButtonTextColor { get; set; } = Color.Parse("#FF111118");
    public Color ButtonHoverColor { get; set; } = Color.Parse("#FFB5E7FF");
    public double ButtonWidth { get; set; } = 260;
    public double ButtonHeight { get; set; } = 50;
    public double MenuSpacing { get; set; } = 12;
    public bool ShowGallery { get; set; } = true;
}

public sealed class GameUiConfiguration
{
    public Color DialogueBackgroundColor { get; set; } = Color.Parse("#CC292933");
    public Color DialogueTextColor { get; set; } = Colors.White;
    public Color SpeakerTextColor { get; set; } = Color.Parse("#FF8ED8FF");
    public double DialogueHeight { get; set; } = 160;
    public double DialogueMargin { get; set; } = 20;
    public double DialogueCornerRadius { get; set; } = 8;
    public double DialogueFontSize { get; set; } = 16;
    public string ChoiceLayout { get; set; } = "vertical";
    public Color ChoiceButtonColor { get; set; } = Color.Parse("#FF292933");
    public Color ChoiceButtonTextColor { get; set; } = Colors.White;
    public double ChoiceButtonWidth { get; set; } = 240;
    public double ChoiceButtonHeight { get; set; } = 44;
    public double ChoiceSpacing { get; set; } = 8;
    public bool CommandBarVisible { get; set; } = true;
    public Color CommandTextColor { get; set; } = Color.Parse("#FFC8C8D0");
    public Color CommandHoverTextColor { get; set; } = Color.Parse("#FF8ED8FF");
}

public class SettingsUiConfiguration
{
    public Color BackgroundColor { get; set; } = Color.Parse("#FF111118");
    public Color PanelColor { get; set; } = Color.Parse("#FF292933");
    public Color TextColor { get; set; } = Colors.White;
    public Color ButtonColor { get; set; } = Color.Parse("#FF292933");
    public Color ButtonTextColor { get; set; } = Colors.White;
}

public sealed class SaveLoadUiConfiguration : SettingsUiConfiguration { }
public sealed class GalleryUiConfiguration : SettingsUiConfiguration { }
