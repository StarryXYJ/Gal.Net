namespace GalNet.Core.Services;

/// <summary>
/// 页面激活时通知接口。
/// 实现此接口的 ViewModel 在被导航服务设置为当前页面时，
/// 会收到 OnActivated 调用。
/// </summary>
public interface IActivableViewModel
{
    /// <summary>页面被激活（成为当前页）。</summary>
    void OnActivated();
}
