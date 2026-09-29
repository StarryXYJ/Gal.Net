---
id: F-20260929-04-quality-baseline
title: 质量基线与工程门禁
type: maintenance
status: done
created: 2026-09-29
updated: 2026-09-29
parent: F-20260929-03-maintainability-roadmap
---

# 质量基线与工程门禁

## 目标

完成维护性路线图 Phase 0：恢复可重复的绿色测试基线，建立仓库级代码规范与自动 CI 门禁，移除已确认的无用依赖，并修正活动 agent 上下文中的失效路径。

## 范围

- 将嵌入 JSON 设置的测试契约明确为结构语义往返，不要求保留空白格式。
- 增加根级 `.editorconfig`，统一基础文本/C# 约定，并只在测试代码中关闭与行为式测试名冲突的 `CA1707`。
- 为 pull request、`master` push 和手动运行建立 CI；核心/Desktop 验证与平台 workload 验证分开。
- 删除 `GalNet.Editor.Shared` 未使用的 `CommunityToolkit.Mvvm` 引用。
- 将 DI 包从旧 preview 版升级到稳定版，并更新受影响锁文件。
- 将活动 agent knowledge 改为仓库相对路径和当前用户无关的描述。

## 非目标

- 不拆测试项目，不增加架构 ADR，不迁移公共 API 或命名空间。
- 不执行全仓库机械格式化，不修改进行中的粒子实现。
- 不重写历史 feature 或 lesson 中用于保留历史证据的旧路径。

## 验收标准

- `GeneralTest` 和 `GalNet.Assets.Tests` 全部通过。
- 资产测试不再产生 `CA1707` 命名告警。
- CI 自动覆盖 PR 和 `master` push，核心/Desktop 与平台 job 相互独立。
- `Editor.Shared` 不再引用未使用的 MVVM 包，所有 lock file 与中央版本一致。
- 活动 agent knowledge 不再依赖旧仓库盘符或旧用户名。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [实现总结](summary.md)
- [维护性路线图](../F-20260929-03-maintainability-roadmap/feature.md)
