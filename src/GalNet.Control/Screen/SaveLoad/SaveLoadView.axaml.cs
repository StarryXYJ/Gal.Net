using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GalNet.Control.ViewModels;

namespace GalNet.Control.Views;
public partial class SaveLoadView : UserControl
{
    public SaveLoadView() { InitializeComponent(); }
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not SaveLoadViewModel vm) return;
        TitleBlock.Text = vm.Title;
        Pagination.PropertyChanged += async (_, e) =>
        {
            if (e.Property == Ursa.Controls.Pagination.CurrentPageProperty && e.NewValue is int page)
            { await vm.SetPageAsync(page); Render(vm); }
        };
        BackButton.Click += (_, _) => vm.Back();
        _ = InitializeAsync(vm);
    }
    private async Task InitializeAsync(SaveLoadViewModel vm) { await vm.RefreshAsync(); Render(vm); }
    private void Render(SaveLoadViewModel vm)
    {
        SlotPanel.Children.Clear(); Pagination.TotalCount = vm.TotalSlotCount; Pagination.CurrentPage = vm.CurrentPage + 1;
        foreach (var card in vm.Slots)
        {
            var button = CreateCard($"Slot {card.Info.SlotIndex + 1}\n{card.Label}");
            button.Click += async (_, _) => await vm.SelectAsync(card);
            SlotPanel.Children.Add(button);
        }
        // A partially filled final page still presents a stable 4 x 3 card grid.
        for (var i = vm.Slots.Count; i < SaveLoadViewModel.PageSize; i++)
            SlotPanel.Children.Add(CreateCard("", isEnabled: false));
    }

    private static Button CreateCard(string text, bool isEnabled = true) => new()
    {
        MinHeight = 205,
        Margin = new Avalonia.Thickness(10),
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        Background = SolidColorBrush.Parse("#292933"),
        BorderThickness = new Avalonia.Thickness(0),
        IsEnabled = isEnabled,
        Content = new TextBlock { Text = text, Foreground = Brushes.White, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
    };
}
