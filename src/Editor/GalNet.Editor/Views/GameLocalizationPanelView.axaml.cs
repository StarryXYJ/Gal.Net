using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

public partial class GameLocalizationPanelView : UserControl
{
    private GameLocalizationPanelViewModel? _model;
    private readonly TranslateTransform _contextOffset = new();
    private bool? _narrow;
    public GameLocalizationPanelView()
    {
        InitializeComponent();
        ContextHeader.RenderTransform = _contextOffset;
        SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 650;
            if (_narrow == narrow) return;
            _narrow = narrow;
            LocalizationColumns.ColumnDefinitions = new(narrow ? "0,0,*" : "160,6,*");
            LocalizationCategories.IsVisible = CategorySplitter.IsVisible = !narrow;
            CompactLocalizationCategory.IsVisible = narrow;
        };
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
        foreach (var language in model.Languages)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Width = 244, Spacing = 4 };
            panel.Children.Add(new TextBlock { Text = language, VerticalAlignment = VerticalAlignment.Center });
            if (language != model.Languages[0])
            {
                foreach (var direction in new[] { -1, 1 })
                {
                    var button = IconButton(direction < 0 ? "PanelLeftIcon" : "PanelRightIcon", model.Service.Text[direction < 0 ? "I18n.MoveLeft" : "I18n.MoveRight"]);
                    button.Name = (direction < 0 ? "LanguageLeft_" : "LanguageRight_") + language.Replace('-', '_');
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
        LocalizationHeader.Width = LocalizationRows.Width;
    }
    private void OnTableScroll(object? sender, ScrollChangedEventArgs args) => _contextOffset.X = LocalizationTableScroll.Offset.X;
    private Button IconButton(string icon, string label)
    {
        var button = new Button { Theme = (ControlTheme)this.FindResource("EditorIconButton")!, Content = new PathIcon { Width = 16, Height = 16, Data = (Geometry)this.FindResource(icon)! } };
        ToolTip.SetTip(button, label); global::Avalonia.Automation.AutomationProperties.SetName(button, label);
        return button;
    }
    private Control CreateRow(LocalizationGridRow row)
    {
        var panel = new Grid { ColumnDefinitions = new("188,*"), ClipToBounds = true };
        var context = new StackPanel { Margin = new Thickness(4), Spacing = 4 };
        context.Children.Add(new TextBlock { Text = row.Context, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        var navigate = IconButton("PanelNavigateIcon", _model?.Service.Text["I18n.Navigate"] ?? "");
        navigate.Click += (_, _) => _model?.Service.Navigate(row.Source);
        context.Children.Add(navigate);
        var frozen = new Border { Child = context, RenderTransform = _contextOffset, ZIndex = 1 };
        frozen.Bind(BackgroundProperty, this.GetResourceObservable("SemiColorBackground0"));
        var cells = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(cells, 1); panel.Children.Add(cells); panel.Children.Add(frozen);
        foreach (var cell in row.Cells) cells.Children.Add(CreateCell(cell));
        return panel;
    }
    private Control CreateCell(LocalizationGridCell cell)
    {
        var box = new StackPanel { Width = 244, Margin = new Thickness(0, 4), Spacing = 4, DataContext = cell };
        var input = new TextBox { Name = "LocalizationCellInput", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 160, Margin = new Thickness(4, 0) };
        input.Bind(TextBox.TextProperty, new Binding(nameof(cell.Text)) { Mode = BindingMode.TwoWay });
        input.Bind(IsEnabledProperty, new Binding(nameof(cell.Enabled)));
        input.Bind(TextBox.IsReadOnlyProperty, new Binding(nameof(cell.Busy)));
        input.GotFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_model is null || !input.IsFocused || input.TranslatePoint(default, LocalizationTableScroll) is not { } point) return;
            var left = 188d; var right = LocalizationTableScroll.Viewport.Width;
            var offset = LocalizationTableScroll.Offset;
            var delta = point.X < left || right < left + input.Bounds.Width ? point.X - left
                : Math.Max(0, point.X + input.Bounds.Width - right);
            LocalizationTableScroll.Offset = new(Math.Max(0, offset.X + delta), offset.Y);
        }, DispatcherPriority.Background);
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
        status.Bind(IsVisibleProperty, new Binding(nameof(cell.HasStatus)));
        status.Bind(TextBlock.TextProperty, new Binding(nameof(cell.Status))); box.Children.Add(status);
        var error = new TextBlock { Margin = new Thickness(4, 0), TextWrapping = TextWrapping.Wrap };
        error.Bind(IsVisibleProperty, new Binding(nameof(cell.HasMessage)));
        error.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("SystemControlErrorTextForegroundBrush"));
        error.Bind(TextBlock.TextProperty, new Binding(nameof(cell.Message))); box.Children.Add(error);
        return box;
    }
}
