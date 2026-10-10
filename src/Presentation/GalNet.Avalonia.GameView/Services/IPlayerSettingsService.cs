using CommunityToolkit.Mvvm.ComponentModel;

namespace GalNet.Avalonia.GameView.Services;

/// <summary>Host-defined settings rendered by the shared player settings page.</summary>
public interface IPlayerSettingsService
{
    IReadOnlyList<PlayerSetting> Items { get; }
}

public abstract class PlayerSetting(string id, object label) : ObservableObject
{
    public string Id { get; } = id;
    public object Label { get; } = label;
}

public sealed record SettingOption(string Value, string DisplayName);

public sealed class ChoicePlayerSetting(string id, object label, IReadOnlyList<SettingOption> options,
    Func<string> read, Action<string> write) : PlayerSetting(id, label)
{
    public IReadOnlyList<SettingOption> Options { get; } = options;
    public SettingOption? SelectedOption
    {
        get => Options.FirstOrDefault(option => option.Value == read());
        set
        {
            if (value is null || !Options.Contains(value) || value.Value == read()) return;
            write(value.Value);
            Refresh();
        }
    }
    public void Refresh() => OnPropertyChanged(nameof(SelectedOption));
}

public sealed class RangePlayerSetting(string id, object label, double minimum, double maximum,
    Func<double> read, Action<double> write) : PlayerSetting(id, label)
{
    public double Minimum { get; } = minimum;
    public double Maximum { get; } = maximum;
    public double Value
    {
        get => read();
        set
        {
            if (!double.IsFinite(value)) return;
            var next = Math.Clamp(value, Minimum, Maximum);
            if (next == read()) return;
            write(next);
            Refresh();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(Value));
}

internal sealed class DefaultPlayerSettingsService(ViewModels.GamePageViewModel gameplay) : IPlayerSettingsService
{
    public IReadOnlyList<PlayerSetting> Items { get; } =
    [new RangePlayerSetting("textSpeed", "Text speed", 0, 120, () => gameplay.TextSpeed, value => gameplay.TextSpeed = value)];
}
