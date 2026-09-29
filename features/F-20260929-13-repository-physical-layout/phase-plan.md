---
feature: F-20260929-13-repository-physical-layout
status: done
updated: 2026-09-29
---

# 实施计划

## Phase 1 - 共享层与基础设施物理归位

**状态：verified**

**任务：** 移动 Shared、Presentation、Infrastructure 项目；更新全仓 ProjectReference 与主 solution 路径；保持依赖图不变。

**验证：** Architecture、Core、Runtime、Builtins、Assets、Storage、Presentation 测试；相关 Release build；旧路径搜索。

**证据：** Shared、Presentation 和 Infrastructure 共 10 个项目已移动，全部 `ProjectReference` 静态解析成功，主 solution 可列出 31 个项目；Architecture、Core、Runtime、Builtins、Assets、Storage、Presentation 测试顺序执行并以 254/254 通过。活动文件中不再存在这些项目的旧 `src/GalNet.*` 根路径；仅保留既有分析器与 Avalonia XAML 告警。

**退出条件：** 内层项目位于对应物理分组，引用解析唯一且测试通过，创建独立 Git 提交。

## Phase 2 - Editor、Samples 与 Launcher 物理归位

**状态：verified**

**任务：** 移动 Editor、Samples 和 Launcher 项目；更新主/Launcher solution、CI、sample 脚本和 GameTestCase 命令。

**验证：** Editor.Shared、Editor、Integration 测试；Editor.Headless、Sample.Headless、Sample.Avalonia、Launcher.Desktop build；两个 sample 脚本。

**证据：** Editor、Samples 和 Launcher 项目已移动到对应物理分组，Launcher 旧的重复容器层已移除；主 solution 可列出 31 个项目，Launcher solution 可列出 5 个项目，所有 `ProjectReference` 均可解析。Editor.Shared、Editor、Integration 测试顺序执行并以 75/75 通过；Editor.Headless、Sample.Headless、Sample.Avalonia 和 Launcher.Desktop 的 Release build 均通过。Headless sample 脚本完成编译、运行、选择分支和退出的交互冒烟，Avalonia sample 脚本通过 PowerShell 语法解析且对应项目已完成 Release build。CI、sample 脚本和 GameTestCase 命令均已切换到新路径，旧宿主目录未被 restore 重建。

**退出条件：** 所有宿主项目与 solution 分组一致，自动化入口不引用旧路径，创建独立 Git 提交。

## Phase 3 - 仓库地图、清理与全量收尾

**状态：verified**

**任务：** 更新根 README、架构 spec、路线图和 agent knowledge；清理确认无源码的旧目录；生成 summary。

**验证：** 10 个测试项目、完整 solution Release build、locked restore、活动路径搜索、`git diff --check`。

**证据：** 根 README 已补充环境要求、仓库地图、构建测试命令、sample 入口、平台限制和 feature 工作流，架构 spec 已记录六个 `src` 物理分组；无追踪文件的旧 `src/GalNet.Storage.Abstractions` 本地残留已删除。`dotnet restore GalNet.slnx --locked-mode` 成功，全量 10 个测试项目顺序执行并以 329/329 通过；完整 solution 在设置 Avalonia 遥测退出、禁用共享编译并使用单 MSBuild worker 后完成 Release build，结果为 70 个既有告警、0 个错误。活动文件无旧项目路径，`git diff --check` 通过。

**退出条件：** 文档与物理结构一致、无活动旧路径或未记录限制，创建独立 Git 提交。
