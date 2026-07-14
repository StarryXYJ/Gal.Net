using Avalonia.Controls;
using GalNet.Control.ViewModels;

namespace GalNet.Control.Views;
public partial class GalleryView : UserControl
{
    public GalleryView() { InitializeComponent(); }
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not GalleryViewModel vm) return;
        BackButton.Click += (_, _) => vm.Back(); Render(vm);
    }
    private void Render(GalleryViewModel vm)
    {
        CategoryPanel.Children.Clear(); ItemPanel.Children.Clear();
        if (vm.ShowCategorySelector) foreach (var category in vm.Categories) { var button = new Button { Content = category.ToString(), Margin = new Avalonia.Thickness(0, 0, 8, 8) }; button.Click += (_, _) => { vm.SelectCategory(category); Render(vm); }; CategoryPanel.Children.Add(button); }
        foreach (var item in vm.Items) { var button = new Button { Content = item.Title ?? $"{item.Category} {item.SequenceId + 1}", Width = 180, Height = 120, Margin = new Avalonia.Thickness(8) }; button.Click += (_, _) => vm.Open(item); ItemPanel.Children.Add(button); }
    }
}
