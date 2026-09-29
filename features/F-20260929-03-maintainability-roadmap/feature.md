---
id: F-20260929-03-maintainability-roadmap
title: 项目结构与可维护性治理路线图
type: refactor-roadmap
status: done
created: 2026-09-29
updated: 2026-09-29
---

# 项目结构与可维护性治理路线图

## 原始目标

系统规划 GalNet 在项目职责、物理目录、程序集边界、命名空间、代码规模、测试组织、代码规范、依赖治理和长期文档方面的优化，使每个项目职责可解释、依赖方向可验证、代码不因重复或聚合职责持续膨胀，并为后续维护提供稳定基线。

## 当前问题

- `docs/spec/architecture.md` 声明 Runtime 只依赖 Core 与呈现抽象，但 `GalNet.Runtime` 实际还依赖 `GalNet.Storage.Abstractions`。
- 多个程序集使用其他程序集的命名空间：Presentation 契约位于 `GalNet.Core.View`，Storage 契约位于 `GalNet.Core.Services`，Builtins 类型位于 `GalNet.Core.Entry`。
- `GalNet.Storage.Abstractions` 同时承载资源、内容、存档、变量和 Gallery 契约，名称与职责不完全一致。
- Core 中保留了若干历史 UI/宿主服务契约；`ISaveService` 同时存在迁移前后的重复重载。
- `EditorWorkspaceViewModel`、`SampleGameSessionService`、`AvaloniaGamePageView` 和 `BuiltInEditorCommandHandler` 已成为主要职责聚合点。
- `GeneralTest` 引用大部分生产程序集，单元测试、UI 测试和跨模块集成测试边界不清；`GalNet.Assets.Tests` 也覆盖了 Editor 与 FileSystem 场景。
- 主测试存在一项嵌入 JSON 格式往返失败；资产测试的命名规则产生大量分析器告警。
- CI 仅支持手动触发，缺少 PR/push 门禁、格式校验和架构依赖检查。
- 缺少仓库级 `.editorconfig`；存在未使用包、预览版 DI 包和分散的项目级重复配置。
- 解决方案逻辑分组与物理目录不一致，agent knowledge 和部分历史文档仍引用旧磁盘路径。

## 范围

- 定义目标程序集职责和允许的依赖方向。
- 规划 Storage/Runtime/Core/Presentation/Builtins 契约与命名空间迁移。
- 规划历史公共接口和重复 API 的清理方式。
- 规划测试项目拆分、架构测试、CI 和代码规范门禁。
- 规划大型协调类的渐进式职责拆分。
- 规划解决方案目录、物理目录、README、正式文档和 agent knowledge 的整理。
- 将实施拆成边界独立、可验证的后续 feature，并标明依赖关系。

## 非目标

- 本路线图 feature 不修改业务代码、项目引用、公共 API、目录或 CI。
- 不在一次提交中完成全部重构，也不执行全仓库格式化。
- 不改变剧情执行、存档语义、资源格式、渲染行为或粒子系统设计。
- 不替 `F-20260916-02-audio-system` 决定音频后端与播放语义。
- 不与进行中的 `F-20260929-02-particle-system-v2` 共享实现提交。

## 验收标准

- 每个已识别优化点都映射到明确的目标状态和后续实施阶段。
- 目标依赖图能说明每个程序集为什么存在、允许引用谁、哪些契约归谁所有。
- 公共 API 与命名空间迁移有兼容性、顺序和验证策略，不要求一次性大爆炸修改。
- 每个实施阶段都有前置条件、测试、文档和退出条件。
- 高风险架构决定被识别为 ADR 候选，不在路线图中伪装成已接受决定。
- 当前并行 feature 的工作树改动不被覆盖或混入本路线图。

## 约束与默认

- Core、Runtime 和 Editor.Shared 继续保持与 Avalonia、Skia、LibVLC 和具体文件系统解耦。
- 契约优先由消费该策略的内层模块拥有，实现留在外层基础设施或宿主。
- 程序集名、根命名空间、物理目录和文档术语应尽量表达同一职责模型。
- 先建立可重复验证的绿色基线，再迁移依赖和公共 API；纯移动与行为变化分开提交。
- 项目尚处快速迭代期，可以接受有计划的破坏性 API 调整，但必须一次迁移仓库内消费者并更新文档。

## 待确认决策

- `GalNet.Storage.Abstractions` 最终删除、重命名还是按端口类别拆分，需要 ADR 确认。
- Builtins 公共类型迁出 `GalNet.Core.Entry` 是否需要临时类型转发或直接破坏性迁移。
- Editor 扩展 API 是否继续暴露 `IServiceProvider + object`，还是引入强类型上下文。
- Launcher 是长期产品、模板还是实验外壳；在产品定位确定前只整理目录，不扩大其职责。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [当前架构](../../docs/spec/architecture.md)
- [运行时与呈现解耦计划](../../docs/design/runtime-presentation-decoupling-phase-plan.md)
- [Feature 工作流](../../docs/design/feature-workflow.md)
- [杂项待办](../../docs/design/misc-todo.md)
