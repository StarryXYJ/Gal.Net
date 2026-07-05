namespace GalNet.Core.Screen;

/// <summary>
/// Base marker interface for all screen categories.
/// </summary>
public interface IScreenCategory
{
    /// <summary>Category identifier (matches ScreenCategory.Name).</summary>
    string Category { get; }
}

/// <summary>
/// Title screen — game logo, menu buttons.
/// </summary>
public interface ITitleScreen : IScreenCategory
{
    /// <summary>Set game title text.</summary>
    void SetTitle(string title);

    /// <summary>Set button texts in order (New Game, Continue, Settings, Quit).</summary>
    void SetButtons(string[] buttonTexts);

    /// <summary>Fired when a menu button is clicked (returns button index).</summary>
    event Action<int>? ButtonClicked;
}

/// <summary>
/// Settings screen — volume, text speed, fullscreen.
/// </summary>
public interface ISettingsScreen : IScreenCategory
{
    /// <summary>BGM volume.</summary>
    double BgmVolume { get; set; }

    /// <summary>SFX volume.</summary>
    double SfxVolume { get; set; }

    /// <summary>Voice volume.</summary>
    double VoiceVolume { get; set; }

    /// <summary>Text speed.</summary>
    double TextSpeed { get; set; }

    /// <summary>Fullscreen toggle.</summary>
    bool Fullscreen { get; set; }

    /// <summary>Fired when user presses Back.</summary>
    event Action? BackRequested;
}

/// <summary>
/// Save/Load screen — grid of save slots.
/// </summary>
public interface ISaveLoadScreen : IScreenCategory
{
    /// <summary>Whether in save mode (true) or load mode (false).</summary>
    bool IsSaveMode { get; set; }

    /// <summary>Fired when a slot is selected.</summary>
    event Action<int>? SlotSelected;

    /// <summary>Fired when Back is pressed.</summary>
    event Action? BackRequested;
}

/// <summary>
/// Gallery screen — CG/illustration/OST browser.
/// </summary>
public interface IGalleryScreen : IScreenCategory
{
    /// <summary>Fired when Back is pressed.</summary>
    event Action? BackRequested;
}
