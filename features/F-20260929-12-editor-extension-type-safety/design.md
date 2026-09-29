# Editor 扩展贡献类型安全设计

## 现状与消费者

`IEditorExtensionRegistry` 保存异构 `IDockPanelContribution`，当前只有 Editor 自身注册 6 个内置 panel。panel ViewModel 包含 singleton/scoped DI 服务、无参数实例和 `GraphNode` 参数实例；inspector 根据对应 dock ViewModel 创建。Abstraction 不能引用 Avalonia，因此 view 的返回类型必须保持 `object`。

`IServiceProvider` 用于在全局或当前项目 scope 中解析 contribution 依赖，是生命周期边界的一部分。主要风险来自 `object parameter`、`object dockViewModel` 和 `object viewModel` 的分散强转：错误直到内置 lambda 执行时才表现为无上下文的 `InvalidCastException`。

## 候选方案

1. **保持原接口并只补文档。** 完全兼容，但不能减少实现中的重复强转，也不能提前校验类型。
2. **把 registry 改为全泛型接口。** 编译期最强，但异构集合仍需 type erasure，并会破坏所有 contribution 实现与布局调用链；当前没有足够收益支撑 ADR。
3. **保留接口并增加泛型基类。** registry 继续使用稳定的非泛型接口，泛型基类在 bridge 处集中校验并调用强类型抽象方法；旧实现继续工作，内置实现可立即迁移。

采用方案 3。它把必要的 type erasure 固定在一个边界，同时避免把 Avalonia 或具体 Editor ViewModel 放入 Abstraction。

## API 形状

- `DockPanelContributionBase<TViewModel>`：无参数 panel，提供强类型 `CreateViewModel(IServiceProvider)` 与 `CreateView(IServiceProvider, TViewModel)`。
- `DockPanelContributionBase<TViewModel, TParameter>`：带参数 panel，拒绝 null 和错误参数类型，并提供强类型创建方法。
- `InspectorControlContributionBase<TDockViewModel, TInspectorViewModel>`：约束 inspector ViewModel 实现 `IInspectorControlViewModel`，集中校验 dock 与 inspector ViewModel 类型。

现有非泛型 `DockPanelContributionBase` 和两个 contribution interface 保持不变。泛型基类通过 sealed override 实现 object bridge，错误消息包含 contribution 类型、参数名、预期类型和实际类型。

## 内置迁移

Editor 中的 delegate contribution 改为泛型实现。NodeGraph、Preview、Assets、Log、Inspector 使用无参数类型；GroupEditor 使用 `GraphNode` 参数；Preview inspector 使用 `GamePreviewPanelViewModel`，其他 inspector 使用对应 dock ViewModel 类型。

## 风险与验证

- nullable 参数规则若含糊会让恢复布局行为变化；只有无参数基类允许忽略 null，带参数基类明确要求非 null `TParameter`。
- view 仍返回 `object`，由 Editor 的 `DockViewLocator` 转为 Avalonia `Control`；这是程序集边界要求，不视为未完成类型安全。
- 测试覆盖 bridge 正常/错误类型、重复注册、内置 Log panel 从 registry 到 ViewModel 创建，并通过 Editor/Headless 构建验证组合。
