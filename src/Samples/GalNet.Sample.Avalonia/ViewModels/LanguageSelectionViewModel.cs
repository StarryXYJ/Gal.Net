using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GalNet.Avalonia.GameHost;
using GalNet.Avalonia.GameView.Services;
using GalNet.Sample.Avalonia.Services;

namespace GalNet.Sample.Avalonia.ViewModels;

/// <summary>Owns the first-run selection; language sources must be loaded before choosing.</summary>
public sealed partial class LanguageSelectionViewModel(IPlayerLocalization localization) : ObservableObject
{
    private TaskCompletionSource<string?>? _selection;

    [ObservableProperty]
    private IReadOnlyList<SettingOption> _languages = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private SettingOption? _selectedLanguage;

    public async Task<string?> ChooseAsync(string preferred, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_selection is not null)
            throw new InvalidOperationException("A language selection is already pending.");

        Languages = localization.CultureService.AvailableCultures
            .OrderBy(culture => culture.Name, StringComparer.Ordinal)
            .Select(culture => new SettingOption(culture.Name, culture.NativeName)).ToArray();
        var selection = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _selection = selection;
        SelectedLanguage = Languages.FirstOrDefault(option => option.Value.Equals(preferred, StringComparison.OrdinalIgnoreCase))
            ?? (Languages.Count > 0 ? Languages[0] : null);
        ConfirmCommand.NotifyCanExecuteChanged();
        using var cancellation = cancellationToken.Register(() => selection.TrySetCanceled(cancellationToken));
        try
        {
            return await selection.Task;
        }
        finally
        {
            _selection = null;
            ConfirmCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSelectedLanguageChanged(SettingOption? value)
    {
        if (value is not null) localization.ApplyCulture(value.Value);
    }

    private bool CanConfirm() => _selection is { Task.IsCompleted: false } && SelectedLanguage is not null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        if (CanConfirm())
            _selection!.TrySetResult(SelectedLanguage!.Value);
    }

}
