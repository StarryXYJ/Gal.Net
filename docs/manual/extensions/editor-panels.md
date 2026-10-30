# 扩展 Editor 面板

使用 Editor.Abstraction.Extensibility 中的公开 contribution 契约注册面板。面板负责自己的 View/订阅，作者状态和历史继续交给项目服务。

## 最小接入

1. 定义项目自己的面板 ViewModel 与 Avalonia View，明确是项目内唯一面板还是按实例参数打开的文档。
2. 实现 `DockPanelContributionBase<TViewModel>`；需要稳定 Group/文档参数时使用双泛型版本。选择唯一 PanelId、中文标题对应的本地化键和默认停靠位置。
3. 通过现有 `IEditorExtensionRegistry.RegisterDockPanel` 注册贡献，并在项目 scope 中提供依赖。
4. 需要详情面板时提供 `IInspectorControlContribution`，处理锁定/失效与 IDisposable，不把已卸载 View 留作作者状态。

以下示例中的 MyPanelViewModel 和 MyPanelView 由项目自行定义；示例展示公开契约，省略本地化资源和应用组合根：

```csharp
using System;
using GalNet.Editor.Abstraction.Extensibility;
using Microsoft.Extensions.DependencyInjection;

public sealed class MyPanelContribution : DockPanelContributionBase<MyPanelViewModel>
{
    public override string PanelId => "MyPanel";
    public override string TitleKey => "Project.Panel.MyPanel";
    public override DockPanelPlacement Placement => DockPanelPlacement.BottomDocument;
    public override bool IsGlobal => true;

    protected override MyPanelViewModel CreateViewModel(IServiceProvider services) =>
        services.GetRequiredService<MyPanelViewModel>();
    protected override object CreateView(IServiceProvider services, MyPanelViewModel viewModel) =>
        new MyPanelView { DataContext = viewModel };
}
```

在已有注册流程中调用 `registry.RegisterDockPanel(new MyPanelContribution())`。内置 Delegate contribution 是 Editor 内部类；外部扩展不要复制旧文档中的非泛型构造器。

## 检查清单

- 同一项目中关闭重开能正确释放和重新订阅，项目关闭后没有遗留任务。
- 撤销、Save All 和作者 dirty 仍来自项目会话；面板筛选、尺寸和焦点不进入内容历史。
- 切换文档、关闭来源或恢复布局后，旧选择不能提交到新对象。
- 缺少 View 映射、错误实例参数和失效 Inspector 都有明确反馈。

布局、生命周期和现有面板入口见[Dock 规范](../../spec/editor/dock.md)。
