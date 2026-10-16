using GalNet.Avalonia.GameView.Navigation;
using GalNet.Avalonia.GameView.Services;
using GalNet.Avalonia.GameView.Settings;
using GalNet.Game.Controls.Forms;

namespace GalNet.Avalonia.GameView.ViewModels;

public sealed partial class SettingsPageViewModel(IGameNavigationService navigation,
    IPlayerSettingsService settings)
    : BackNavigablePageViewModel(navigation)
{
    public IReadOnlyList<SettingCategory> Categories => settings.Categories;
    public EditorRegistry Editors => settings.Editors;
}
