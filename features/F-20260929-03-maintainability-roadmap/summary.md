# 项目结构与可维护性治理路线图总结

## 结果

路线图七个阶段已完成并通过独立审核。项目契约归属、命名空间、测试边界、主要职责热点、扩展 API 类型边界和物理目录现在使用同一职责模型，并由架构测试、全量测试和正式文档共同约束。

## 实施切片

- `F-20260929-04`：恢复测试基线、建立 `.editorconfig`、CI 触发与依赖卫生。
- `F-20260929-05` 至 `07`：接受 ADR-0001，迁移 Runtime/Storage 契约，删除 Storage.Abstractions 和历史接口。
- `F-20260929-08`：Presentation 与 Builtins 命名空间归位。
- `F-20260929-09`：将测试拆成 10 个职责项目，并建立 CI 分组。
- `F-20260929-10`、`11`：拆分 Editor 工作区、内置命令、Avalonia presenter 和 Sample 会话职责。
- `F-20260929-12`：以泛型 contribution bridge 收敛 Editor 扩展类型检查。
- `F-20260929-13`：将 21 个生产项目归入六个物理分组并同步开发者文档。
- Phase 7 审核补充测试项目依赖 allowlist 和 test-to-test 引用门禁。

## 最终验证

- 10 个测试项目 340/340 通过，其中 Architecture 25 项。
- 完整 solution Release build 覆盖 Desktop、Browser、Android 和 iOS，0 错误。
- locked restore、Headless sample 交互冒烟、Avalonia sample 构建和脚本语法验证通过。
- 主 solution 31 个项目，所有 `ProjectReference` 可解析，活动入口无旧项目路径。

## 偏差与后续

全仓阻断式格式门禁因约 3,840 项既有机械差异未在功能重构中启用。该项不影响当前结构交付，已由 [F-20260929-14-format-baseline](../F-20260929-14-format-baseline/feature.md) 独立承接。审核结论与剩余风险见 [review.md](review.md)。
