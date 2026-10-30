using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GalNet.Avalonia.Debugging;

/// <summary>Command surface for optional sample debug tools; window-specific presentation stays in the host view.</summary>
public sealed partial class GameDebugViewModel(GameDebugActions actions) : ObservableObject
{
    public event Func<GameDebugPrompt, Task<bool>>? ConfirmationRequested;
    public event Func<GameDebugNotification, Task>? NotificationRequested;
    public event Action? LogRequested;
    public bool CanOpenLogDirectory => actions.CanOpenLogDirectory;

    [RelayCommand]
    private Task ShowLogAsync()
    {
        LogRequested?.Invoke();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ClearPlayerStateAsync()
    {
        const string message = "这会停止当前游戏，删除所有存档和阅读进度，并将所有玩家变量还原为默认值，然后自动重新启动。此操作无法撤销。";
        if (!await ConfirmAsync(message, "清空游戏数据"))
            return;

        await RunAsync(actions.ClearPlayerStateAsync, "清空游戏数据失败");
    }

    [RelayCommand]
    private Task ReloadGameResourcesAsync() =>
        RunAsync(actions.ReloadGameResourcesAsync, "重新加载游戏资源失败");

    [RelayCommand]
    private Task SaveProgressOnlyAsync() =>
        SaveDebugSnapshotAsync(DebugSaveKind.ProgressOnly, "仅进度存档");

    [RelayCommand]
    private Task SaveGalGroupStartAsync() =>
        SaveDebugSnapshotAsync(DebugSaveKind.GalGroupStart, "galgroup 存档");

    [RelayCommand]
    private Task OpenLogDirectoryAsync() =>
        RunAsync(actions.OpenLogDirectory, "打开日志目录失败");

    private async Task SaveDebugSnapshotAsync(DebugSaveKind kind, string displayName)
    {
        try
        {
            var result = await actions.SaveDebugSnapshotAsync(kind);
            if (result.RequiresOverwriteConfirmation)
            {
                const string overwriteMessage = "所有可见的存档槽均已占用。是否覆盖第 1 槽？";
                if (!await ConfirmAsync(overwriteMessage, displayName))
                    return;

                result = await actions.SaveDebugSnapshotAsync(kind, overwriteWhenAllVisibleSlotsAreOccupied: true);
            }

            await NotifyAsync($"{displayName}已写入第 {result.SlotIndex!.Value + 1} 槽。", displayName);
        }
        catch (Exception exception)
        {
            await NotifyAsync(exception.Message, $"{displayName}失败");
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, string failureTitle)
    {
        try
        {
            await action(CancellationToken.None);
        }
        catch (Exception exception)
        {
            await NotifyAsync(exception.Message, failureTitle);
        }
    }

    private async Task RunAsync(Action action, string failureTitle)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            await NotifyAsync(exception.Message, failureTitle);
        }
    }

    private async Task<bool> ConfirmAsync(string message, string title)
    {
        var handlers = ConfirmationRequested;
        if (handlers is null)
            return false;

        foreach (Func<GameDebugPrompt, Task<bool>> handler in handlers.GetInvocationList())
            if (!await handler(new GameDebugPrompt(message, title)))
                return false;

        return true;
    }

    private Task NotifyAsync(string message, string title) =>
        NotificationRequested?.Invoke(new GameDebugNotification(message, title)) ?? Task.CompletedTask;
}

public sealed record GameDebugPrompt(string Message, string Title);

public sealed record GameDebugNotification(string Message, string Title);
