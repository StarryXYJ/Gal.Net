using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
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
        LanguageHeaders.Children.Clear(); LanguageManagerRows.Children.Clear();
        foreach (var language in model.Languages)
        {
            var source = language == model.Languages[0];
            var culture = CultureInfo.GetCultureInfo(language);
            var languageName = culture.NativeName + " · " + language;
            var manager = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 4 };
            var visible = new CheckBox { Content = languageName, IsChecked = model.IsLanguageVisible(language), IsEnabled = !source, MinWidth = 0 };
            ToolTip.SetTip(visible, model.Service.Text["Uiux.I18n.VisibleColumns"]);
            visible.IsCheckedChanged += (_, _) => model.SetLanguageVisible(language, visible.IsChecked == true);
            manager.Children.Add(visible);
            if (source)
            {
                var label = new TextBlock { Text = model.Service.Text["I18n.Source"], VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
                Grid.SetColumn(label, 1); Grid.SetColumnSpan(label, 2); manager.Children.Add(label);
            }
            else
            {
                var more = IconButton("PanelMoreIcon", model.Service.Text["Uiux.I18n.LanguageActions"]);
                var actions = new StackPanel { Spacing = 4 };
                foreach (var direction in new[] { -1, 1 })
                {
                    var move = new Button { Content = model.Service.Text[direction < 0 ? "I18n.MoveLeft" : "I18n.MoveRight"], HorizontalAlignment = HorizontalAlignment.Stretch };
                    move.Name = (direction < 0 ? "LanguageLeft_" : "LanguageRight_") + language.Replace('-', '_');
                    var index = model.Languages.IndexOf(language);
                    move.IsEnabled = direction < 0 ? index > 1 : index < model.Languages.Count - 1;
                    move.Click += async (_, _) => { more.Flyout?.Hide(); await model.RunAsync(() => model.Service.MoveLanguageAsync(language, direction)); };
                    actions.Children.Add(move);
                }
                var development = new Button { Content = model.Service.Text["I18n.ApplyDevelopment"], HorizontalAlignment = HorizontalAlignment.Stretch };
                development.Click += (_, _) =>
                {
                    model.DevelopmentLanguage = language; model.PreviewDevelopmentLanguageCommand.Execute(null); more.Flyout?.Hide();
                };
                actions.Children.Add(development); more.Flyout = new Flyout { Content = actions };
                Grid.SetColumn(more, 1); manager.Children.Add(more);
                var remove = IconButton("DeleteRegularIcon", model.Service.Text["Uiux.I18n.RemoveLanguage"]);
                remove.Name = "RemoveLanguage_" + language.Replace('-', '_');
                remove.Click += async (_, _) => await model.RemoveLanguageAsync(language);
                Grid.SetColumn(remove, 2); manager.Children.Add(remove);
            }
            LanguageManagerRows.Children.Add(manager);
            if (!model.IsLanguageVisible(language)) continue;
            var header = new Grid { Width = model.ColumnWidth(language), ColumnDefinitions = new("*,6"), MinHeight = 34 };
            var title = new TextBlock { Text = language + (source ? " · " + model.Service.Text["I18n.Source"] : ""), Margin = new Thickness(8, 4), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(title, languageName); header.Children.Add(title);
            var resize = new Thumb { Name = "ResizeLanguage_" + language.Replace('-', '_'), Cursor = new Cursor(StandardCursorType.SizeWestEast), Background = Brushes.Transparent, Template = new FuncControlTemplate<Thumb>((_, _) => new Border { Background = Brushes.Transparent }) };
            Grid.SetColumn(resize, 1); header.Children.Add(resize);
            resize.DragDelta += (_, args) =>
            {
                model.SetColumnWidth(language, header.Width + args.Vector.X); header.Width = model.ColumnWidth(language); TableWidth();
            };
            LanguageHeaders.Children.Add(header);
        }
        TableWidth();
    }
    private void TableWidth()
    {
        if (_model is not { } model) return;
        LocalizationRows.Width = 188 + model.VisibleLanguages.Sum(model.ColumnWidth);
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
        var panel = new Grid { ColumnDefinitions = new("188,*"), ClipToBounds = true, DataContext = row };
        panel.Bind(BackgroundProperty, this.GetResourceObservable("SemiColorBackground0"));
        var context = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(8, 4), MinHeight = 44 };
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(row.Title))); labels.Children.Add(title);
        var slot = new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        slot.Bind(TextBlock.TextProperty, new Binding(nameof(row.Slot)));
        slot.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("SemiColorText2"));
        slot.IsVisible = row.Slot.Length > 0; labels.Children.Add(slot);
        context.Bind(ToolTip.TipProperty, new Binding(nameof(row.Details))); context.Children.Add(labels);
        var navigate = IconButton("PanelNavigateIcon", _model?.Service.Text["I18n.Navigate"] ?? "");
        navigate.Name = "LocalizationNavigate";
        navigate.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(navigate, 1); context.Children.Add(navigate);
        navigate.Click += (_, _) =>
        {
            if (_model is not { } model) return;
            if (row.Source.Locations.Count <= 1) { model.Service.Navigate(row.Source); return; }
            var locations = new StackPanel { Spacing = 2, MaxWidth = 420 };
            foreach (var location in row.Source.Locations)
            {
                var target = new Button { Content = location.Title + " · " + location.Slot, HorizontalAlignment = HorizontalAlignment.Stretch };
                ToolTip.SetTip(target, location.DisplayDetails);
                target.Click += (_, _) => { navigate.Flyout?.Hide(); model.Service.Navigate(location.Reference); };
                locations.Children.Add(target);
            }
            navigate.Flyout = new Flyout { Content = new ScrollViewer { Content = locations, MaxHeight = 300 } };
            navigate.Flyout.ShowAt(navigate);
        };
        var count = new TextBlock { FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        count.Bind(TextBlock.TextProperty, new Binding(nameof(row.ReferenceCount))); Grid.SetColumn(count, 1); context.Children.Add(count);
        var frozen = new Border { Name = "LocalizationContext", Child = context, RenderTransform = _contextOffset, ZIndex = 1 };
        frozen.Bind(BackgroundProperty, this.GetResourceObservable("SemiColorBackground0"));
        var cells = new StackPanel { Orientation = Orientation.Horizontal };
        Grid.SetColumn(cells, 1); panel.Children.Add(cells); panel.Children.Add(frozen);
        foreach (var cell in row.Cells) cells.Children.Add(CreateCell(cell));
        return panel;
    }
    private Control CreateCell(LocalizationGridCell cell)
    {
        var box = new StackPanel { Margin = new Thickness(0, 4), DataContext = cell };
        box.Bind(WidthProperty, new Binding(nameof(cell.Width)));
        var content = new Grid(); box.Children.Add(content);
        var input = new TextBox { Name = "LocalizationCellInput", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0), Opacity = 0 };
        input.Classes.Add("localizationCell");
        input.Bind(TextBox.TextProperty, new Binding(nameof(cell.Text)) { Mode = BindingMode.OneWay });
        input.Bind(IsEnabledProperty, new Binding(nameof(cell.Enabled)));
        var preview = new TextBlock { Margin = new Thickness(10, 4), MaxLines = 2, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        preview.Bind(TextBlock.TextProperty, new Binding(nameof(cell.DisplayText)));
        input.Bind(TextBox.PlaceholderTextProperty, new Binding(nameof(cell.IsMissing)) { Converter = new MissingTranslationConverter(_model?.Service.Text["I18n.Missing"] ?? "") });
        content.Children.Add(input); content.Children.Add(preview);
        bool Composing() => input.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
        async Task Update(bool explicitValue = false)
        {
            if (_model is not { } model || Composing()) return;
            if (input.Text != cell.Text) cell.Text = input.Text ?? "";
            await model.RunAsync(() => cell.CommitAsync(explicitValue));
        }
        void InputChanged()
        {
            if (_model is not null && !Composing() && input.Text != cell.Text) cell.Text = input.Text ?? "";
        }
        input.TextChanging += (_, _) => InputChanged();
        input.TextInput += (_, _) => Dispatcher.UIThread.Post(InputChanged, DispatcherPriority.Background);
        input.GotFocus += (_, _) =>
        {
            input.Opacity = 1; preview.IsVisible = false;
            Dispatcher.UIThread.Post(() =>
            {
                if (_model is null || !input.IsFocused || input.TranslatePoint(default, LocalizationTableScroll) is not { } point) return;
                var left = 188d; var right = LocalizationTableScroll.Viewport.Width;
                var offset = LocalizationTableScroll.Offset;
                var delta = point.X < left || right < left + input.Bounds.Width ? point.X - left : Math.Max(0, point.X + input.Bounds.Width - right);
                LocalizationTableScroll.Offset = new(Math.Max(0, offset.X + delta), offset.Y);
            }, DispatcherPriority.Background);
        };
        input.LostFocus += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            if (input.IsFocused) return;
            await Update(); input.Opacity = 0; preview.IsVisible = true;
        }, DispatcherPriority.Background);
        input.AddHandler(InputElement.KeyDownEvent, async (_, args) =>
        {
            if (Composing()) return;
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                cell.Revert();
                input.Text = cell.Text;
                LocalizationSearch.Focus();
            }
            else if (args.Key == Key.Enter && args.KeyModifiers.HasFlag(KeyModifiers.Control)) { args.Handled = true; await Update(true); }
        }, RoutingStrategies.Tunnel);
        var errors = new Grid { ColumnDefinitions = new("*,Auto") };
        errors.Bind(IsVisibleProperty, new Binding(nameof(cell.HasMessage)));
        var error = new TextBlock { Margin = new Thickness(8, 0), TextWrapping = TextWrapping.Wrap };
        error.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("SystemControlErrorTextForegroundBrush"));
        error.Bind(TextBlock.TextProperty, new Binding(nameof(cell.Message))); errors.Children.Add(error);
        var reset = IconButton("ResetIcon", _model?.Service.Text["Uiux.I18n.ReloadCell"] ?? "");
        reset.Click += (_, _) => cell.Revert(); Grid.SetColumn(reset, 1); errors.Children.Add(reset); box.Children.Add(errors);
        return box;
    }
    private sealed class MissingTranslationConverter(string label) : Avalonia.Data.Converters.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? label : "";
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
