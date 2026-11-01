using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GalNet.Sample.CrossPlatform.Opening;

/// <summary>Application-owned sequence. It owns pages, never the game-loading task.</summary>
public sealed partial class OpeningSequence(IEnumerable<Func<OpeningPageViewModel>> pages) : ObservableObject, IAsyncDisposable
{
    private readonly Func<OpeningPageViewModel>[] _pages = pages.ToArray();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource<string> _profile = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _run;
    private Task? _disposal;
    private bool _skipRemaining;
    private HashSet<string>? _seen;
    [ObservableProperty] private OpeningPageViewModel? _currentPage;
    [ObservableProperty] private bool _isRunning;
    public bool CanSkipCurrent => CurrentPage?.CanSkip == true;
    public bool HasCurrentPage => CurrentPage is not null;
    public Task Completion => _run ?? Task.CompletedTask;

    public void SetProfileDirectory(string directory) => _profile.TrySetResult(directory);

    public Task RunAsync(Func<OpeningPageViewModel, CancellationToken, Task> present,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposal is not null, this);
        return _run ??= RunCoreAsync(present, cancellationToken);
    }

    private async Task RunCoreAsync(Func<OpeningPageViewModel, CancellationToken, Task> present, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        var token = lifetime.Token;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        IsRunning = true;
        try
        {
            foreach (var create in _pages)
            {
                token.ThrowIfCancellationRequested();
                await using var page = create();
                if (string.IsNullOrWhiteSpace(page.Id) || page.Id.IndexOfAny(['\r', '\n']) >= 0 || !ids.Add(page.Id))
                    throw new InvalidOperationException("Opening pages require unique, nonempty, single-line IDs.");
                if (page.ShowPolicy == OpeningShowPolicy.Disabled || (_skipRemaining && page.CanSkip)) continue;
                if (page.ShowPolicy == OpeningShowPolicy.OncePerProfile && (await ReadHistoryAsync(token)).Contains(page.Id)) continue;
                CurrentPage = page;
                try
                {
                    await present(page, token);
                    token.ThrowIfCancellationRequested();
                    page.Start(Finish);
                    await page.Finished.WaitAsync(token);
                    await page.DisposeAsync();
                    if (page.ShowPolicy == OpeningShowPolicy.OncePerProfile)
                    {
                        var seen = await ReadHistoryAsync(token);
                        seen.Add(page.Id);
                        var profile = await _profile.Task.WaitAsync(token);
                        Directory.CreateDirectory(profile);
                        var path = Path.Combine(profile, "opening-pages.txt");
                        var temporary = path + ".tmp";
                        try
                        {
                            await File.WriteAllLinesAsync(temporary, seen.Order(StringComparer.Ordinal), token);
                            File.Move(temporary, path, overwrite: true);
                        }
                        finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    }
                }
                finally
                {
                    // Join cancellation, animation and page-owned work before unmounting or showing the next page.
                    try { await page.DisposeAsync(); }
                    finally { CurrentPage = null; }
                }
            }
        }
        finally { IsRunning = false; }
    }

    private async Task<HashSet<string>> ReadHistoryAsync(CancellationToken token)
    {
        if (_seen is not null) return _seen;
        var path = Path.Combine(await _profile.Task.WaitAsync(token), "opening-pages.txt");
        return _seen = File.Exists(path)
            ? new(await File.ReadAllLinesAsync(path, token), StringComparer.Ordinal)
            : new(StringComparer.Ordinal);
    }

    private void Finish(OpeningPageViewModel page)
    {
        if (ReferenceEquals(CurrentPage, page)) page.Complete();
    }

    [RelayCommand]
    private void SkipCurrent()
    {
        if (CurrentPage is { CanSkip: true } page) Finish(page);
    }

    [RelayCommand]
    private void SkipRemaining()
    {
        _skipRemaining = true;
        SkipCurrent();
    }

    partial void OnCurrentPageChanged(OpeningPageViewModel? value)
    {
        OnPropertyChanged(nameof(CanSkipCurrent));
        OnPropertyChanged(nameof(HasCurrentPage));
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return new(_disposal ??= DisposeCoreAsync());
    }
    private async Task DisposeCoreAsync()
    {
        _lifetime.Cancel();
        try
        {
            if (_run is not null)
            {
                try { await _run; }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            }
        }
        finally { _lifetime.Dispose(); }
    }
}
