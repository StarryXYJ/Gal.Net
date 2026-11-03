using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using GalNet.Editor.Abstraction.Services;
using GalNet.Editor.Shared.Services;

namespace GalNet.Editor.Services;

public static class DefaultApplicationPrompt
{
    private static bool _showing;

    public static async Task OfferAsync(IEditorSettingsService settingsService, IEditorLocalizationService localization, string path)
    {
        var settings = settingsService.GetSettings();
        if (_showing || File.Exists(settings.DefaultApplicationPath) || string.IsNullOrWhiteSpace(path)) return;
        if (!File.Exists(path)) return;
        string? contracts = null;
        try { contracts = ProjectContractSource.FindForApplication(path); }
        catch (Exception) { /* A debug application may not expose a contract. */ }
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault(window => window.IsActive);
        if (owner is null) return;
        _showing = true;
        try
        {
            var dialog = new Window { Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Title = localization["Settings.DefaultApplication"] };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            var yes = new Button { Content = localization["Common.Yes"] };
            var no = new Button { Content = localization["Common.No"] };
            yes.Click += (_, _) => dialog.Close(true);
            no.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(yes); buttons.Children.Add(no);
            dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 20, Children = { new TextBlock { Text = localization["Settings.OfferDefaultApplication"], TextWrapping = Avalonia.Media.TextWrapping.Wrap }, buttons } };
            if (await dialog.ShowDialog<bool>(owner) && !File.Exists(settings.DefaultApplicationPath))
            {
                settings.DefaultApplicationPath = path;
                if (contracts is not null && string.IsNullOrWhiteSpace(settings.DefaultContractsDirectory)) settings.DefaultContractsDirectory = contracts;
                settingsService.SaveSettings();
            }
        }
        finally { _showing = false; }
    }
}
