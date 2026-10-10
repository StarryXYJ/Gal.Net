using Avalonia.Controls;
using Avalonia.Interactivity;
using GalNet.Avalonia.GameView.Services;

namespace GalNet.Sample.Avalonia.Views;

public partial class LanguageSelectionView : UserControl
{
    private TaskCompletionSource<string?>? _selection;

    public LanguageSelectionView() => InitializeComponent();

    public async Task<string?> ChooseAsync(IReadOnlyList<SettingOption> options, string preferred, CancellationToken cancellationToken)
    {
        var selection = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _selection = selection;
        LanguagePicker.ItemsSource = options;
        LanguagePicker.SelectedItem = options.FirstOrDefault(option => option.Value.Equals(preferred, StringComparison.OrdinalIgnoreCase))
            ?? (options.Count > 0 ? options[0] : null);
        using var cancellation = cancellationToken.Register(() => selection.TrySetCanceled(cancellationToken));
        try { return await selection.Task; }
        finally { if (ReferenceEquals(_selection, selection)) _selection = null; }
    }

    private void ConfirmClicked(object? sender, RoutedEventArgs args)
    {
        if (LanguagePicker.SelectedItem is SettingOption option) _selection?.TrySetResult(option.Value);
    }

    private void CancelClicked(object? sender, RoutedEventArgs args) => _selection?.TrySetResult(null);
}
