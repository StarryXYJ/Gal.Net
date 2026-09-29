---
id: F-20260929-12-editor-extension-type-safety
title: Editor 扩展贡献类型安全
type: refactor
status: done
created: 2026-09-29
updated: 2026-09-29
parent: F-20260929-03-maintainability-roadmap
---

# Editor 扩展贡献类型安全

## 目标

完成维护性路线图 Phase 5，在不破坏现有异构扩展 registry 的前提下，为 dock panel、带参数 panel 与 inspector contribution 提供强类型实现入口和明确的运行时错误，并用内置贡献验证完整注册链路。

## 范围

- 盘点 `Editor.Abstraction.Extensibility` 的仓库内消费者与类型组合。
- 保留现有 `IDockPanelContribution`、`IInspectorControlContribution` 和 `IServiceProvider` 兼容表面。
- 新增强类型基类，把 `object` bridge、null/type 检查集中在 Abstraction。
- 迁移 Editor 内置 contribution，移除 delegate 内的业务类型强制转换。
- 补充 registry、typed bridge 与至少一个内置 contribution 注册/创建测试。

## 非目标

- 不引入插件加载器、版本协商或独立插件包格式。
- 不把 Avalonia `Control` 类型泄漏到 Editor.Abstraction。
- 不重做 dock layout、Inspector 选择同步或 DI scope。
- 不删除现有非泛型接口，不制造破坏性公共 API 迁移。

## 验收标准

- 外部实现可继续实现现有接口；新实现可以选择泛型基类获得编译期 ViewModel/parameter 类型。
- 错误的 parameter、dock ViewModel 或 inspector ViewModel 在 bridge 边界得到包含预期/实际类型的明确异常。
- 6 个内置 dock contribution 使用强类型 delegate/base，不再在注册 lambda 中直接强转参数。
- Editor 与 Editor.Shared 测试、架构测试、Editor/Headless Release 构建通过。
- 每个实现阶段单独提交 Git。

## 约束

- 仓库内未发现外部插件消费者；因此只能保证源码兼容与现有接口保留，不宣称第三方二进制兼容已实测。
- `IServiceProvider` 继续表达全局/项目 scope 服务解析，不以 service locator 数量本身作为删除理由。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [维护性路线图 Phase 5](../F-20260929-03-maintainability-roadmap/phase-plan.md)
