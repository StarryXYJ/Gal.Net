namespace GalNet.Editor.ViewModels;

/// <summary>可导航页面的基类。</summary>
public abstract class PageViewModelBase : ViewModelBase
{
    private string _title = "";
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }
}
