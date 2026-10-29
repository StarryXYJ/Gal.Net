using CommunityToolkit.Mvvm.ComponentModel;

namespace GalNet.Sample.CrossPlatform.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _greeting = "Welcome to Avalonia!";
}
