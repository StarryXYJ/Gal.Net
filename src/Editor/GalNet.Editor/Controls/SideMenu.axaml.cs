using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using GalNet.Editor.Models;

namespace GalNet.Editor.Controls;

public partial class SideMenu : UserControl
{
    public static readonly StyledProperty<IEnumerable<MenuData>?> ItemsSourceProperty =
        AvaloniaProperty.Register<SideMenu, IEnumerable<MenuData>?>(nameof(ItemsSource));

    public IEnumerable<MenuData>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public SideMenu()
    {
        InitializeComponent();
    }
}
