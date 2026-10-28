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
public sealed class ResourceOverridesPanelView : UserControl
{
    private ResourceOverridesPanelViewModel? _model;
    public ResourceOverridesPanelView()
    {
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
        var root = new Grid { RowDefinitions = new("Auto,Auto,*,Auto"), Margin = new Thickness(8), RowSpacing = 6 };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var create = new Button { Name = "CreateResourcePatch", Content = model.L["Packaging.Create"] };
        create.Click += async (_, _) => await model.CreateAsync(); toolbar.Children.Add(create);
        var indirect = new CheckBox { Content = model.L["Packaging.Dependencies"], IsChecked = model.ShowDependencies };
        indirect.IsCheckedChanged += (_, _) => model.ShowDependencies = indirect.IsChecked == true; toolbar.Children.Add(indirect);
        var cancel = new Button { Content = model.L["Packaging.Cancel"] };
        cancel.Click += (_, _) => model.CancelExport(); toolbar.Children.Add(cancel);
        root.Children.Add(toolbar);
        var originalTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var original = new AssetPicker { Name = "AdditionalOriginal", AssetManager = model.Service.Assets, ResourceTypeId = "*", Width = 250 };
        var add = new Button { Content = model.L["Packaging.AddOriginal"] };
        add.Click += async (_, _) => { if (original.SelectedAssetId.Length > 0) await model.RunAsync(() => model.Service.AddOriginalAsync(original.SelectedAssetId)); };
        originalTools.Children.Add(original); originalTools.Children.Add(add); Grid.SetRow(originalTools, 1); root.Children.Add(originalTools);
        var columns = model.Columns.ToArray();
        var table = new Grid { RowDefinitions = new("Auto,*"), Width = 300 + 260 * columns.Length, MinWidth = 300, HorizontalAlignment = HorizontalAlignment.Left };
        var headers = new StackPanel { Orientation = Orientation.Horizontal };
        headers.Children.Add(new TextBlock { Text = model.L["Packaging.Original"], Width = 292, Margin = new Thickness(4), VerticalAlignment = VerticalAlignment.Center });
        foreach (var column in columns)
        {
            var header = new StackPanel { Width = 252, Margin = new Thickness(0, 0, 8, 6), Spacing = 4 };
            var name = new TextBox { Name = "PatchName_" + column.Id };
            name.Bind(TextBox.TextProperty, new Binding(nameof(ResourcePatchColumn.Name)) { Source = column, Mode = BindingMode.TwoWay });
            ToolTip.SetTip(name, model.L["Packaging.Name"]);
            var priority = new TextBox { Name = "PatchPriority_" + column.Id };
            priority.Bind(TextBox.TextProperty, new Binding(nameof(ResourcePatchColumn.Priority)) { Source = column, Mode = BindingMode.TwoWay });
            ToolTip.SetTip(priority, model.L["Packaging.Priority"]);
            foreach (var input in new[] { name, priority })
            {
                input.KeyDown += async (_, args) => { if (args.Key == Key.Enter) { args.Handled = true; await model.RunAsync(() => model.CommitHeaderAsync(column)); } };
            }
            header.Children.Add(name); header.Children.Add(new TextBlock { Text = model.L["Packaging.Priority"], FontSize = 11 }); header.Children.Add(priority);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            var export = new Button { Name = "ExportPatch_" + column.Id, Content = model.L["Packaging.Export"], HorizontalAlignment = HorizontalAlignment.Stretch };
            export.Click += async (_, _) => await model.ExportAsync(column.Id);
            var delete = new Button { Name = "DeletePatch_" + column.Id, Content = model.L["Packaging.Delete"] };
            delete.Click += async (_, _) => await model.DeleteAsync(column.Id);
            var discard = new Button { Name = "DiscardPatch_" + column.Id, Content = model.L["Packaging.Discard"] };
            discard.Click += (_, _) => model.DiscardDrafts();
            header.Children.Add(export); actions.Children.Add(delete); actions.Children.Add(discard); header.Children.Add(actions);
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
        var horizontal = new ScrollViewer
        {
            Name = "ResourcePatchHorizontalScroll",
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = table
        };
        Grid.SetRow(horizontal, 2); root.Children.Add(horizontal);
        var message = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxHeight = 100 };
        message.Bind(TextBlock.TextProperty, new Binding(nameof(ResourceOverridesPanelViewModel.Message)) { Source = model });
        Grid.SetRow(message, 3); root.Children.Add(message);
        Content = root;
    }
    private static Control CreateRow(ResourceOverridesPanelViewModel model, ResourcePatchColumn[] columns, ResourceReferenceRow row)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0, MinHeight = 40 };
        var source = new Button
        {
            Name = "Locate_" + row.Id,
            Width = 290,
            Margin = new Thickness(0, 0, 10, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = row.Path + " · " + row.TypeId + (row.Missing ? " · " + model.L["Packaging.Missing"] : "")
        };
        ToolTip.SetTip(source, row.Id + "\n" + string.Join("\n", row.Uses.Select(use => use.GroupId + "/" + use.EntryId + use.FieldPath + (use.Through is null ? "" : " ← " + use.Through))));
        source.Click += (_, _) => model.Locate(row); panel.Children.Add(source);
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
