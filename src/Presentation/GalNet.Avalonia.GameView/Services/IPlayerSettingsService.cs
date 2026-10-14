using GalNet.Avalonia.GameView.Settings;
using GalNet.Game.Controls.Forms;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Host-defined, frozen category structure with observable setting values.</summary>
public interface IPlayerSettingsService
{
    IReadOnlyList<SettingCategory> Categories { get; }
    EditorRegistry Editors { get; }
}

// Also used by the initial language-selection flow, independently of settings editors.
public sealed record SettingOption(string Value, string DisplayName);

internal sealed class DefaultPlayerSettingsService : IPlayerSettingsService
{
    public DefaultPlayerSettingsService(ViewModels.GamePageViewModel gameplay)
    {
        Categories = Array.AsReadOnly(new[] { new SettingCategory("game", "Settings.Game",
            [new DoubleSetting("textSpeed", "Settings.TextSpeed", 30, 0, 120, 1,
                () => gameplay.TextSpeed, (value, _) => { gameplay.TextSpeed = value; return Task.CompletedTask; }, "Text speed")
                { EditorKind = NumericEditorKind.Slider }], "Game") });
        new SettingsCoordinator().Attach(Categories);
    }
    public IReadOnlyList<SettingCategory> Categories { get; }
    public EditorRegistry Editors { get; } = EditorRegistry.CreateDefault();
}
