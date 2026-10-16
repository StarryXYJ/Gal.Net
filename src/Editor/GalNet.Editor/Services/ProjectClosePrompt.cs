using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Services;

public sealed class ProjectClosePrompt(IEditorLocalizationService localization) : IProjectClosePrompt
{
    public Task<ProjectCloseChoice> ChooseAsync(string projectName)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .FirstOrDefault(window => window.IsActive);
        if (owner is null) return Task.FromResult(ProjectCloseChoice.Cancel);
        return CreateDialog(projectName, localization).ShowDialog<ProjectCloseChoice>(owner);
    }

    public static Window CreateDialog(string projectName, IEditorLocalizationService? localization = null)
    {
        localization ??= EditorLocalizationService.Current ?? new EditorLocalizationService();
        var bindings = new List<IDisposable>();
        var dialog = new Window { Width = 680, MinHeight = 240, SizeToContent = SizeToContent.Height, CanResize = true, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        bindings.Add(LocalizedBindings.Bind(dialog, Window.TitleProperty, localization, "Close.Title"));
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (key, choice) in new[] { ("Close.Save", ProjectCloseChoice.Save), ("Close.Discard", ProjectCloseChoice.Discard), ("Common.Cancel", ProjectCloseChoice.Cancel) })
        {
            var button = new Button { Name = "Close" + choice, Margin = new Thickness(4) };
            bindings.Add(LocalizedBindings.Bind(button, ContentControl.ContentProperty, localization, key));
            button.Click += (_, _) => dialog.Close(choice);
            buttons.Children.Add(button);
        }
        var question = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var summary = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 };
        bindings.Add(LocalizedBindings.Bind(question, TextBlock.TextProperty, localization, "Close.Question", projectName));
        bindings.Add(LocalizedBindings.Bind(summary, TextBlock.TextProperty, localization, "Close.Summary"));
        dialog.Content = new StackPanel { Margin = new Thickness(20), Spacing = 20, Children = { question, summary, buttons } };
        dialog.Closed += (_, _) => { foreach (var binding in bindings) binding.Dispose(); };
        return dialog;
    }
}
