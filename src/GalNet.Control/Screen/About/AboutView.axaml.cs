using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace GalNet.Control.Screen.About;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateMarkdownResources();
    }

    private void UpdateMarkdownResources()
    {
        if (DataContext is not AboutViewModel viewModel) return;
        var config = viewModel.Configuration;
        SetResource("AboutSelectionBrush", new SolidColorBrush(config.SelectionColor));
        SetResource("AboutTextBrush", new SolidColorBrush(config.TextColor));
        SetResource("AboutHeadingBrush", new SolidColorBrush(config.HeadingColor));
        SetResource("AboutBlockquoteBackgroundBrush", new SolidColorBrush(config.BlockquoteBackgroundColor));
        SetResource("AboutBlockquoteBorderBrush", new SolidColorBrush(config.BlockquoteBorderColor));
        SetResource("AboutCodeBackgroundBrush", new SolidColorBrush(config.CodeBackgroundColor));
        SetResource("AboutCodeBorderBrush", new SolidColorBrush(config.CodeBorderColor));
        SetResource("AboutCodeTextBrush", new SolidColorBrush(config.CodeTextColor));
        SetResource("AboutRuleBrush", new SolidColorBrush(config.RuleColor));
        SetResource("AboutLinkBrush", new SolidColorBrush(config.LinkColor));
        SetResource("AboutLinkHoverBrush", new SolidColorBrush(config.LinkHoverColor));
        SetResource("AboutLinkVisitedBrush", new SolidColorBrush(config.LinkVisitedColor));
        SetResource("AboutFontSize", config.FontSize);
        SetResource("AboutCodeFontSize", config.CodeFontSize);
    }

    private void SetResource(string key, object value)
    {
        Resources[key] = value;
        MarkdownScrollViewer.Resources[key] = value;
        if (Application.Current is { } application)
            application.Resources[key] = value;
    }
}
