using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class GameLocalizationPanelView : UserControl
{
    private GameLocalizationPanelViewModel? _model;
    public GameLocalizationPanelView()
    {
        InitializeComponent();
        LocalizationRows.ItemTemplate = new FuncDataTemplate<LocalizationGridRow>((row, _) => row is null ? new Border() : CreateRow(row));
        DataContextChanged += (_, _) => Connect();
        AttachedToVisualTree += (_, _) => Connect();
        DetachedFromVisualTree += (_, _) => Disconnect();
    }
    private void Disconnect() { if (_model is { } model) model.StructureChanged -= Headers; _model = null; }
    private void Connect()
    {
        Disconnect(); _model = DataContext as GameLocalizationPanelViewModel;
        if (_model is { } model) { model.StructureChanged += Headers; Headers(); }
    }
    private void Headers()
    {
        if (_model is not { } model) return;
        LanguageHeaders.Children.Clear();
        LanguageHeaders.Children.Add(new TextBlock { Width = 180, Text = model.Service.Text["I18n.Context"], Margin = new Thickness(4) });
        foreach (var language in model.Languages)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Width = 244, Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = language, VerticalAlignment = VerticalAlignment.Center });
            if (language != model.Languages[0])
            {
                foreach (var direction in new[] { -1, 1 })
                {
                    var button = new Button { Content = direction < 0 ? "←" : "→", Name = (direction < 0 ? "LanguageLeft_" : "LanguageRight_") + language.Replace('-', '_') };
                    var index = model.Languages.IndexOf(language);
                    button.IsEnabled = direction < 0 ? index > 1 : index < model.Languages.Count - 1;
                    ToolTip.SetTip(button, model.Service.Text[direction < 0 ? "I18n.MoveLeft" : "I18n.MoveRight"]);
                    button.Click += async (_, _) => await model.RunAsync(() => model.Service.MoveLanguageAsync(language, direction));
                    panel.Children.Add(button);
                }
            }
            else panel.Children.Add(new TextBlock { Text = model.Service.Text["I18n.Source"], VerticalAlignment = VerticalAlignment.Center });
            LanguageHeaders.Children.Add(panel);
        }
        LocalizationRows.Width = 188 + model.Languages.Count * 244;
    }
    private Control CreateRow(LocalizationGridRow row)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var context = new StackPanel { Width = 188, Margin = new Thickness(0, 4) };
        context.Children.Add(new TextBlock { Text = row.Context, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        var navigate = new Button { Content = _model?.Service.Text["I18n.Navigate"], Margin = new Thickness(4) };
        navigate.Click += (_, _) => _model?.Service.Navigate(row.Source);
        context.Children.Add(navigate); panel.Children.Add(context);
        foreach (var cell in row.Cells) panel.Children.Add(CreateCell(cell));
        return panel;
    }
    private Control CreateCell(LocalizationGridCell cell)
    {
        var box = new StackPanel { Width = 244, Margin = new Thickness(0, 4), Spacing = 4, DataContext = cell };
        var input = new TextBox { Name = "LocalizationCellInput", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 160, Margin = new Thickness(4, 0) };
        input.Bind(TextBox.TextProperty, new Binding(nameof(cell.Text)) { Mode = BindingMode.TwoWay });
        input.Bind(IsEnabledProperty, new Binding(nameof(cell.Enabled)));
        input.Bind(TextBox.IsReadOnlyProperty, new Binding(nameof(cell.Busy)));
        bool Composing() => input.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
        async Task Commit(bool force = false)
        {
            if (_model is not { } model || Composing()) return;
            await model.RunAsync(() => cell.CommitAsync(force));
        }
        input.LostFocus += (_, _) => Dispatcher.UIThread.Post(async () => { if (!input.IsFocused) await Commit(); }, DispatcherPriority.Background);
        input.TextInput += (_, _) => { if (!input.IsFocused) Dispatcher.UIThread.Post(async () => await Commit(), DispatcherPriority.Background); };
        input.AddHandler(InputElement.KeyDownEvent, async (_, args) =>
        {
            if (args.Key == Key.Escape && !Composing()) { args.Handled = true; cell.Revert(); }
            else if (args.Key == Key.Enter && args.KeyModifiers.HasFlag(KeyModifiers.Control) && !Composing()) { args.Handled = true; await Commit(true); }
        }, RoutingStrategies.Tunnel);
        box.Children.Add(input);
        var status = new TextBlock { Margin = new Thickness(4, 0), TextWrapping = TextWrapping.Wrap };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(cell.Status))); box.Children.Add(status);
        var error = new TextBlock { Margin = new Thickness(4, 0), TextWrapping = TextWrapping.Wrap };
        error.Bind(TextBlock.TextProperty, new Binding(nameof(cell.Message))); box.Children.Add(error);
        return box;
    }
}
