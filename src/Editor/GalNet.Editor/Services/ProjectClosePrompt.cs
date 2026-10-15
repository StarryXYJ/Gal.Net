using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using GalNet.Editor.Abstraction.Services;

namespace GalNet.Editor.Services;

public sealed class ProjectClosePrompt : IProjectClosePrompt
{
    public Task<ProjectCloseChoice> ChooseAsync(string projectName)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            .FirstOrDefault(window => window.IsActive);
        if (owner is null) return Task.FromResult(ProjectCloseChoice.Cancel);
        return CreateDialog(projectName).ShowDialog<ProjectCloseChoice>(owner);
    }

    public static Window CreateDialog(string projectName)
    {
        var dialog = new Window { Title = "Close Project", Width = 520, Height = 190, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, choice) in new[] { ("Save All", ProjectCloseChoice.Save), ("Discard Unsaved Content", ProjectCloseChoice.Discard), ("Cancel", ProjectCloseChoice.Cancel) })
        {
            var button = new Button { Content = label };
            button.Click += (_, _) => dialog.Close(choice);
            buttons.Children.Add(button);
        }
        var summary = new TextBlock
        {
            Text = "Discard applies to unsaved content. File operations already written to disk remain in effect.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 12
        };
        dialog.Height = 240;
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 24,
            Children =
            {
                new TextBlock { Text = $"Save changes to {projectName}?", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                summary,
                buttons
            }
        };
        // Closing the dialog through the window manager is cancellation.
        return dialog;
    }
}
