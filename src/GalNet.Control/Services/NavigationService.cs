using System;
using System.Collections.Generic;
using GalNet.Core.Services;

namespace GalNet.Control.Services;

/// <summary>
/// 导航服务 —— 字典注册 VM↔View，管理页面栈和 CurrentPage。
/// 支持 CreateScope 创建子导航器（每个 dock 页面独立导航栈）。
/// _parent 引用使子导航器可以向父级查询注册。
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<Type, Type> _registry = new();
    private readonly Stack<object> _stack = new();
    private readonly NavigationService? _parent;
    private object? _currentPage;

    public object? CurrentPage
    {
        get => _currentPage;
        private set
        {
            _currentPage = value;
            CurrentPageChanged?.Invoke(value);
        }
    }

    public bool CanGoBack => _stack.Count > 0;

    public event Action<object?>? CurrentPageChanged;

    public NavigationService(IServiceProvider services)
    {
        _services = services;
    }

    private NavigationService(IServiceProvider services, NavigationService parent)
    {
        _services = services;
        _parent = parent;
    }

    // ── 注册 ──

    public void RegisterMap(Type viewModelType, Type viewType)
    {
        _registry[viewModelType] = viewType;
    }

    /// <summary>当前导航器及父级链式查找注册的 View 类型。</summary>
    public Type? GetRegisteredViewType(Type viewModelType)
    {
        if (_registry.TryGetValue(viewModelType, out var viewType))
            return viewType;
        return _parent?.GetRegisteredViewType(viewModelType);
    }

    // ── 导航 ──

    public void NavigateTo<TViewModel>() where TViewModel : class
    {
        var vm = _services.GetService(typeof(TViewModel));
        if (vm == null)
            throw new InvalidOperationException($"Cannot resolve {typeof(TViewModel)} from DI");
        NavigateTo(vm);
    }

    public void NavigateTo(object viewModel)
    {
        if (_currentPage != null)
            _stack.Push(_currentPage);

        CurrentPage = viewModel;
    }

    public void ResetTo<TViewModel>() where TViewModel : class
    {
        var vm = _services.GetService(typeof(TViewModel));
        if (vm is null)
            throw new InvalidOperationException($"Cannot resolve {typeof(TViewModel)} from DI");
        ResetTo(vm);
    }

    public void ResetTo(object viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _stack.Clear();
        CurrentPage = viewModel;
    }

    public void GoBack()
    {
        if (_stack.Count > 0)
            CurrentPage = _stack.Pop();
    }

    public void Clear()
    {
        _stack.Clear();
        CurrentPage = null;
    }

    // ── 子导航 ──

    public INavigationService CreateScope()
    {
        return new NavigationService(_services, this);
    }
}
