---
id: F-20260929-13-repository-physical-layout
title: 仓库物理目录与开发者文档整理
type: refactor
status: planned
created: 2026-09-29
updated: 2026-09-29
parent: F-20260929-03-maintainability-roadmap
---

# 仓库物理目录与开发者文档整理

## 目标

完成维护性路线图 Phase 6，使 `src` 物理目录与 solution 分组一致，消除 Launcher 多余嵌套和已删除项目的本地残留，并提供可直接使用的仓库地图、构建测试命令和平台限制说明。

## 范围

- 将 Shared、Presentation、Editor、Infrastructure、Samples 和 Launcher 项目移动到同名物理分组目录。
- 保持程序集名、根命名空间、公开 API 和运行时行为不变。
- 更新 solution、项目引用、测试引用、CI、脚本和当前开发者文档中的路径。
- 清理已删除 `GalNet.Storage.Abstractions` 留下的空源码目录和忽略构建产物。
- 补充根 README 的仓库地图、常用命令、平台构建限制和 feature 工作流入口。

## 非目标

- 不重命名程序集、命名空间、项目文件或测试项目。
- 不调整项目依赖方向、包版本、业务代码或 UI 行为。
- 不把历史 design/feature 中的架构叙述改写成当前事实；只修复仍应可用的源码链接和命令。
- 不执行与目录移动无关的格式化或告警清理。

## 验收标准

- `GalNet.slnx` 中的 solution 分组与 `src/<Group>/<Project>` 物理路径一致。
- 所有 `ProjectReference`、CI 命令和 sample 脚本使用新路径，仓库搜索不再发现活动旧路径。
- 10 个测试项目顺序通过，Headless/Avalonia sample 脚本可执行，完整 solution Release 构建成功。
- 根 README 能让新贡献者定位项目、执行常用验证并理解受限环境差异。
- 每个实施阶段独立提交 Git。

## 约束

- Windows 上的目录移动必须保持大小写和路径准确，不删除用户文件或未跟踪源码。
- 大量项目共享生产依赖，全量测试继续顺序执行，避免共享 `obj` 锁冲突。
- Avalonia 与平台 solution 构建需要在可访问用户 SDK/BuildServices 目录的环境执行。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [维护性路线图 Phase 6](../F-20260929-03-maintainability-roadmap/phase-plan.md)
