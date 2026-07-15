using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using GalNet.Core.UI;

namespace GalNet.Editor.ViewModels;

public sealed partial class UiPaletteResetViewModel : ObservableObject
{
    public IReadOnlyList<UiColorPalettePreset> Palettes => UiColorPalettePresets.All;

    [ObservableProperty]
    private string _selectedPaletteId;

    public UiPaletteResetViewModel(string selectedPaletteId) =>
        _selectedPaletteId = UiColorPalettePresets.GetRequired(selectedPaletteId).Id;
}
