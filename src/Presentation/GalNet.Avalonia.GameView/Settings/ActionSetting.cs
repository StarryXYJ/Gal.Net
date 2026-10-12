using CommunityToolkit.Mvvm.Input;

namespace GalNet.Avalonia.GameView.Settings;

public sealed class ActionSetting : SettingItem
{
    private readonly Func<CancellationToken, Task> _execute;
    public ActionSetting(string id, string nameKey, Func<CancellationToken, Task> execute, object? label = null)
        : base(id, nameKey, label)
    {
        _execute = execute;
        ExecuteCommand = new AsyncRelayCommand(() => ExecuteAsync());
    }
    public override Type ValueType => typeof(void);
    public IAsyncRelayCommand ExecuteCommand { get; }
    public Task<bool> ExecuteAsync(CancellationToken cancellationToken = default) => Coordinator.EnqueueAsync(async () =>
    {
        RefreshAvailability();
        if (!IsEnabled) { ErrorKey = Availability.ReasonKey ?? "Settings.Unavailable"; return false; }
        IsApplying = true;
        ErrorKey = null;
        try
        {
            await _execute(cancellationToken);
            Coordinator.RefreshAvailability();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { ErrorKey = "Settings.Cancelled"; return false; }
        catch (Exception) { ErrorKey = "Settings.ApplyFailed"; return false; }
        finally { IsApplying = false; }
    }, cancellationToken);
    public override void Refresh() { }
}
