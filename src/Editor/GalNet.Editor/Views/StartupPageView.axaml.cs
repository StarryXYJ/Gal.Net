using Avalonia.Controls;

namespace GalNet.Editor.Views;

public partial class StartupPageView : UserControl
{
    public StartupPageView()
    {
#if BENCHMARK
        using var measurement = GalNet.Diagnostics.CpuProfiler.Shared.Measure("Editor.CreateStartupView");
#endif
        InitializeComponent();
#if BENCHMARK
        Loaded += (_, _) =>
        {
            GalNet.Diagnostics.CpuProfiler.Shared.Mark("Editor.Startup.Loaded");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (GalNet.Diagnostics.StartupBenchmark.Complete())
                    (TopLevel.GetTopLevel(this) as Window)?.Close();
            }, Avalonia.Threading.DispatcherPriority.Background);
        };
#endif
    }
}
