using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using BenchmarkDotNet.Attributes;
using GalNet.Avalonia.GameView.Presentation;
using GalNet.Sample.CrossPlatform;
using GalNet.Sample.CrossPlatform.ViewModels;
using GalNet.Sample.CrossPlatform.Views;

namespace GalNet.Sample.Benchmarks;

/// <summary>Repeatedly resize the mounted Sample game page across its toolbar breakpoint.</summary>
[MemoryDiagnoser]
public class ToolbarBenchmarks : IDisposable
{
    private Window _window = null!;
    private GamePage _page = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var state = new GamePresentationState { IsGameUiReady = true };
        _page = new GamePage(state) { DataContext = new GamePageViewModel(state, null!) };
        var root = new Border { Child = _page };
        root.Classes.Add("galnet-player");
        root.Styles.Add(SampleCustomization.CreateStyles());
        _window = new Window { Width = 1280, Height = 720, Content = root };
        _window.Show();
        _window.UpdateLayout();
    }

    [Benchmark(OperationsPerInvoke = 20)]
    public void ResizeAcrossBreakpoint()
    {
        for (var index = 0; index < 20; index++)
        {
            _window.Width = index % 2 == 0 ? 450 : 1280;
            _window.UpdateLayout();
        }
    }

    public void Verify()
    {
        var full = _page.GetVisualDescendants().OfType<StackPanel>().Single(control => control.Name == "FullToolbar");
        var compact = _page.GetVisualDescendants().OfType<StackPanel>().Single(control => control.Name == "CompactToolbar");
        _window.Width = 450;
        _window.UpdateLayout();
        if (!compact.IsVisible || full.IsVisible) throw new InvalidOperationException("Compact toolbar did not appear.");
        _window.Width = 1280;
        _window.UpdateLayout();
        if (!full.IsVisible || compact.IsVisible) throw new InvalidOperationException("Full toolbar did not reappear.");
    }

    [GlobalCleanup]
    public void Dispose()
    {
        _window?.Close();
        GC.SuppressFinalize(this);
    }
}
