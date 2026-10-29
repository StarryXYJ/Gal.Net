using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Services;

public static class ProjectReloadPrompt
{
    public static Task<bool> ConfirmAsync(IEditorLocalizationService localization)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows.FirstOrDefault(window => window.IsActive);
        return owner is null ? Task.FromResult(false) : CreateDialog(localization).ShowDialog<bool>(owner);
    }

    public static Window CreateDialog(IEditorLocalizationService localization)
    {
        var bindings = new List<IDisposable>();
        var dialog = new Window { Width = 600, MinHeight = 200, SizeToContent = SizeToContent.Height, CanResize = true, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        bindings.Add(LocalizedBindings.Bind(dialog, Window.TitleProperty, localization, "External.Reload"));
        var question = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        bindings.Add(LocalizedBindings.Bind(question, TextBlock.TextProperty, localization, "External.DiscardQuestion"));
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Orientation = Orientation.Horizontal };
        foreach (var (key, accept) in new[] { ("External.DiscardReload", true), ("Common.Cancel", false) })
        {
            var button = new Button { Name = accept ? "ReloadExternal" : "CancelReload", Margin = new Thickness(4) };
            bindings.Add(LocalizedBindings.Bind(button, ContentControl.ContentProperty, localization, key));
            button.Click += (_, _) => dialog.Close(accept); buttons.Children.Add(button);
        }
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 20, Children = { question, buttons } };
        dialog.Closed += (_, _) => { foreach (var binding in bindings) binding.Dispose(); };
        return dialog;
    }
}
