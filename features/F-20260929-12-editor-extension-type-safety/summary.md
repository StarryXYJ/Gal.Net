# Editor 扩展贡献类型安全实现总结

## 结果

Editor 扩展 API 现在保留稳定的非泛型 registry 接口，同时为 dock panel、带参数 panel 和 inspector contribution 提供强类型实现入口。必要的 `object` 类型擦除被限制在 Abstraction bridge，内置贡献不再在注册 lambda 中执行无上下文的业务类型强转。

## 主要改动

- 在 `GalNet.Editor.Abstraction.Extensibility` 新增 `DockPanelContributionBase<TViewModel>`、`DockPanelContributionBase<TViewModel, TParameter>` 和 `InspectorControlContributionBase<TDockViewModel, TInspectorViewModel>`。
- bridge 对 parameter、dock ViewModel 和 inspector ViewModel 做统一 null/type 校验，异常包含 contribution、参数名、预期类型和实际类型。
- 保留 `IDockPanelContribution`、`IInspectorControlContribution`、非泛型 `DockPanelContributionBase` 与 `IServiceProvider`，现有实现可以继续工作。
- 将 6 个内置 dock panel 和 3 个 inspector contribution 迁移到泛型 delegate；`GroupEditor` 的 `GraphNode` 参数由泛型约束表达。
- 增加 registry 重复 ID、三类 typed bridge 和内置 Log panel 注册/创建测试。
- 将扩展 registry、DI scope 与 Avalonia 边界同步到 [架构规范](../../docs/spec/architecture.md)。

## 关键决定

- `IServiceProvider` 是全局/项目 scope 的生命周期边界，不因 service locator 形式而删除。
- registry 需要保存异构贡献，因此不改为破坏性的全泛型 API；`object` 只保留在兼容接口和 view 的程序集边界。
- view 继续返回 `object`，避免 Editor.Abstraction 引用 Avalonia；具体 `Control` 创建仍由 Editor 外层负责。
- 仓库内未发现外部插件消费者，因此本次保证源码兼容和接口保留，不宣称已经验证第三方二进制兼容。

## 验证

- 10 个测试项目顺序执行，329/329 通过。
- `GalNet.Editor`、`GalNet.Editor.Headless` 和 `GalNet.Editor.Abstraction` Release 构建通过。
- 完整 `GalNet.slnx` Release 构建通过，覆盖 Desktop、Android、Browser、iOS、Sample、Editor 和测试项目，0 错误。
- 构建输出仍包含仓库既有分析器告警、离线 NuGet 漏洞源 `NU1900` 和 Wasm native-reference 提示；本 feature 未新增已知告警。

## 计划偏差与后续

未发生设计偏差，也没有新增 blocker。维护性路线图 Phase 5 已完成；后续 Phase 6 将单独处理物理目录和开发者文档整理，避免与本次公共 API 行为修改混合。
