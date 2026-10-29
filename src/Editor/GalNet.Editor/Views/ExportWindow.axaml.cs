using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GalNet.Editor.ViewModels;
using Ursa.Controls;
namespace GalNet.Editor.Views;

public partial class ExportWindow : UrsaWindow
{
    public ExportWindow() => InitializeComponent();

    public async Task ExportAsync(ExportPanelViewModel viewModel)
    {
        using var cancellation = new CancellationTokenSource();
        var progressViewModel = new ExportProgressViewModel(viewModel.L, cancellation.Cancel);
        var progressWindow = new ExportProgressWindow { DataContext = progressViewModel };
        void OnClosing(object? sender, Avalonia.Controls.WindowClosingEventArgs e) => cancellation.Cancel();
        progressWindow.Closing += OnClosing;
        try
        {
            _ = progressWindow.ShowDialog(this);

            var result = await viewModel.ExportAsync(cancellation.Token);
            if (result.Success)
            {
                var mainWindow = Owner as MainWindow;
                progressWindow.Close();
                Close();
                mainWindow?.ShowSuccessToast($"{viewModel.L["Export.Success"]} {Path.GetFileName(result.PackagePath)}");
                return;
            }

            if (cancellation.IsCancellationRequested)
            {
                progressWindow.Close();
                return;
            }

            progressViewModel.CompleteFailure(result.Error ?? viewModel.L["Export.Failed"]);
        }
        finally
        {
            progressWindow.Closing -= OnClosing;
        }
    }
}
