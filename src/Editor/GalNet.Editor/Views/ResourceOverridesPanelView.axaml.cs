using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using GalNet.Editor.Controls;
using GalNet.Editor.Shared.Services;
using GalNet.Editor.ViewModels;

namespace GalNet.Editor.Views;

/// <summary>One source column and one editable column per independent resource patch.</summary>
public sealed partial class ResourceOverridesPanelView : UserControl
{
    private ResourceOverridesPanelViewModel? _model;
    public ResourceOverridesPanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Connect();
        AttachedToVisualTree += (_, _) => Connect();
        DetachedFromVisualTree += (_, _) => Disconnect();
    }
    private void Disconnect() { if (_model is { } model) model.StructureChanged -= Rebuild; _model = null; }
    private void Connect()
    {
        Disconnect(); _model = DataContext as ResourceOverridesPanelViewModel;
        if (_model is { } model) { model.StructureChanged += Rebuild; Rebuild(); }
    }
    private void Rebuild()
    {
        if (_model is not { } model) return;
        var columns = model.Columns.ToArray();
        var table = ResourcePatchTable; table.Children.Clear();
        table.Width = 300 + 260 * columns.Length;
        ResourcePatchEmpty.IsVisible = model.Rows.Count == 0;
        var headers = new StackPanel { Orientation = Orientation.Horizontal };
        headers.Children.Add(new TextBlock { Text = model.L["Packaging.Original"], Width = 292, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center });
        foreach (var column in columns)
        {
            var header = new Grid { Width = 260, RowDefinitions = new("Auto,Auto"), Margin = new Thickness(0, 0, 0, 6), RowSpacing = 4 };
            var top = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 2 };
            var name = new TextBox { Name = "PatchName_" + column.Id, Text = column.Name, MinWidth = 0, VerticalContentAlignment = VerticalAlignment.Center };
            name.Classes.Add("patchName"); ToolTip.SetTip(name, model.L["Packaging.Name"]);
            var priority = new NumericUpDown
            {
                Name = "PatchPriority_" + column.Id,
                Minimum = int.MinValue,
                Maximum = int.MaxValue,
                Increment = 1,
                FormatString = "0",
                ParsingNumberStyle = System.Globalization.NumberStyles.Integer,
                Value = column.Priority,
                Width = 108,
                VerticalAlignment = VerticalAlignment.Center
            };
            async Task SaveHeader()
            {
                if (name.Text == column.Name && priority.Value == column.Priority) return;
                await model.SetHeaderAsync(column, name.Text ?? "", (int)(priority.Value ?? column.Priority));
                name.Text = column.Name; priority.Value = column.Priority;
            }
            name.LostFocus += async (_, _) => await SaveHeader();
            name.KeyDown += async (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; await SaveHeader(); } };
            priority.ValueChanged += async (_, _) => await SaveHeader();
            name.Bind(TextBox.TextProperty, new Binding(nameof(ResourcePatchColumn.Name)) { Source = column, Mode = BindingMode.OneWay });
            priority.Bind(NumericUpDown.ValueProperty, new Binding(nameof(ResourcePatchColumn.Priority)) { Source = column, Mode = BindingMode.OneWay });
            var export = IconButton("PanelExportIcon", model.L["Packaging.Export"], "ExportPatch_" + column.Id);
            export.Bind(IsEnabledProperty, new Binding(nameof(ResourceOverridesPanelViewModel.CanExport)) { Source = model });
            export.Click += async (_, _) => await model.ExportAsync(column.Id);
            var delete = IconButton("DeleteRegularIcon", model.L["Packaging.Delete"], "DeletePatch_" + column.Id);
            delete.Click += async (_, _) => await model.DeleteAsync(column.Id);
            top.Children.Add(name); Grid.SetColumn(export, 1); top.Children.Add(export); Grid.SetColumn(delete, 2); top.Children.Add(delete);
            header.Children.Add(top);
            var bottom = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(6, 0, 6, 0) };
            bottom.Children.Add(new TextBlock { Text = model.L["Packaging.PriorityLabel"], FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            ToolTip.SetTip(bottom, model.L["Packaging.Priority"]);
            Grid.SetColumn(priority, 1); bottom.Children.Add(priority); Grid.SetRow(bottom, 1); header.Children.Add(bottom);
            headers.Children.Add(header);
        }
        table.Children.Add(headers);
        var rows = new ListBox
        {
            Name = "ResourcePatchRows",
            ItemsSource = model.Rows.ToArray(),
            ItemTemplate = new FuncDataTemplate<ResourceReferenceRow>((row, _) => CreateRow(model, columns, row!))
        };
        rows.Styles.Add(new Avalonia.Styling.Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) }
        });
        Grid.SetRow(rows, 1); table.Children.Add(rows);
    }
    private async void CreatePatch(object? sender, Avalonia.Interactivity.RoutedEventArgs args) { if (_model is { } model) await model.CreateAsync(); }
    private async void AddOriginal(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    { if (_model is { } model && AdditionalOriginal.SelectedAssetId.Length > 0) await model.AddOriginalAsync(AdditionalOriginal.SelectedAssetId); }
    private void CancelExport(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => _model?.CancelExport();
    private Button IconButton(string icon, string label, string name)
    {
        var button = new Button
        {
            Name = name,
            Theme = (ControlTheme)this.FindResource("EditorIconButton")!,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new PathIcon { Width = 16, Height = 16, Data = (Avalonia.Media.Geometry)this.FindResource(icon)! }
        };
        ToolTip.SetTip(button, label); Avalonia.Automation.AutomationProperties.SetName(button, label); return button;
    }
    private Control CreateRow(ResourceOverridesPanelViewModel model, ResourcePatchColumn[] columns, ResourceReferenceRow row)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, MinHeight = 40 };
        var source = new Grid { Width = 300, ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 0, 0, 2) };
        var description = new StackPanel { Margin = new Thickness(6, 4), VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        description.Children.Add(new TextBlock { Text = Path.GetFileName(row.Path), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
        var type = new TextBlock { Text = row.TypeId + (row.Missing ? " · " + model.L["Packaging.Missing"] : ""), FontSize = 11 };
        type.Bind(ForegroundProperty, this.GetResourceObservable("SemiColorText2")); description.Children.Add(type); source.Children.Add(description);
        ToolTip.SetTip(source, row.Path + "\n" + row.Id + "\n" + string.Join("\n", row.Uses.Select(use => use.GroupId + "/" + use.EntryId + use.FieldPath + (use.Through is null ? "" : " ← " + use.Through))));
        var locate = IconButton("PanelNavigateIcon", model.L["Packaging.Locate"], "Locate_" + row.Id);
        locate.IsEnabled = !row.Missing; locate.Margin = new Thickness(4, 0); locate.Click += (_, _) => model.Locate(row);
        Grid.SetColumn(locate, 1); source.Children.Add(locate); panel.Children.Add(source);
        foreach (var column in columns)
        {
            var picker = new AssetPicker
            {
                Name = "Replacement_" + column.Id + "_" + row.Id,
                Width = 250,
                Margin = new Thickness(0, 0, 10, 0),
                AssetManager = model.Service.Assets,
                ResourceTypeId = row.TypeId.Length == 0 ? "*" : row.TypeId,
                SelectedAssetId = column.Mappings.GetValueOrDefault(row.Id) ?? ""
            };
            picker.PropertyChanged += async (_, args) =>
            {
                if (args.Property == AssetPicker.SelectedAssetIdProperty)
                    await model.SetMappingAsync(column, row, picker.SelectedAssetId);
            };
            panel.Children.Add(picker);
        }
        return panel;
    }
}
