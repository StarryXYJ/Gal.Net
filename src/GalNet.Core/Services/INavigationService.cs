namespace GalNet.Core.Services;

/// <summary>
/// ViewModel-first 导航服务接口。
/// 字典注册 VM↔View，支持导航栈和子导航（CreateScope）。
/// CurrentPage 绑定到 ContentControl。
/// </summary>
public interface INavigationService
{
    /// <summary>当前页面对象（VM，绑定到 ContentControl）。</summary>
    object? CurrentPage { get; }

    /// <summary>是否存在上一页。</summary>
    bool CanGoBack { get; }

    /// <summary>当前页面变化事件。</summary>
    event Action<object?>? CurrentPageChanged;

    /// <summary>注册 ViewModel 类型到 View 类型的映射。</summary>
    void RegisterMap(Type viewModelType, Type viewType);

    /// <summary>查询 VM 类型注册的 View 类型（若当前未注册则向父导航器查询）。</summary>
    Type? GetRegisteredViewType(Type viewModelType);

    /// <summary>从 DI 容器解析 VM 并导航。</summary>
    void NavigateTo<TViewModel>() where TViewModel : class;

    /// <summary>导航到现有 VM 实例。</summary>
    void NavigateTo(object viewModel);

    /// <summary>返回上一页。</summary>
    void GoBack();

    /// <summary>清空导航栈。</summary>
    void Clear();

    /// <summary>创建子导航器（每个 dock 页面独立导航栈）。</summary>
    INavigationService CreateScope();
}
