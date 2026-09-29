---
feature: F-20260929-04-quality-baseline
updated: 2026-09-29
---

# 实施计划

## Phase 1 - 测试语义与代码规范

**状态：verified**

- 将 `LastDockLayout` 测试改为 JSON 结构比较，并保留拒绝 escaped string 的断言。
- 增加根级 `.editorconfig` 和测试目录的 `CA1707` 例外。
- 验证两个测试项目，并检查分析器输出。

退出条件：测试全绿，已知测试命名告警消失，未引入全仓库格式修改。

**验证（2026-09-29）：** `GeneralTest` 258/258、`GalNet.Assets.Tests` 34/34 通过；`LastDockLayout` 改为比较 JSON 结构语义；测试目录的 `CA1707` 告警已消失。根 `.editorconfig` 已建立，但未强制统一行尾，也未执行全仓库机械格式化。

## Phase 2 - CI 门禁

**状态：verified**

- 增加 PR、`master` push 和手动触发。
- 将核心/Desktop 构建测试与平台 workload 构建拆为独立 job。
- 保留测试结果与覆盖率上传。
- 仅在仓库当前可通过时启用格式门禁。

退出条件：CI 文件语法可解析，job 的目标和失败边界清楚。

**验证（2026-09-29）：** workflow 已覆盖 pull request、`master` push 和手动触发；Desktop/Headless 构建测试与手动平台解决方案构建分为独立 job。当前环境缺少 PyYAML/Ruby，YAML 结构经人工复核。`dotnet format --verify-no-changes` 暴露约 3,840 项既有格式、using 与行尾差异，因此格式门禁延期到独立纯机械 feature，不把天然失败的检查加入 CI。

## Phase 3 - 依赖与活动上下文清理

**状态：verified**

- 删除 Editor.Shared 未使用的 MVVM 包。
- 升级稳定 DI 包并更新锁文件。
- 修正 agent knowledge 中失效的活动绝对路径。
- 运行受影响构建、测试和 diff 检查，生成总结。

退出条件：依赖图与锁文件一致，验证通过，活动文档无旧环境依赖。

**验证（2026-09-29）：** DI 包已升级到稳定版 `10.0.0`，Editor.Shared 的无用 MVVM 直接引用已删除，`dotnet restore GalNet.slnx --locked-mode` 成功。Editor Headless、Headless Sample 及完整 `GalNet.slnx` Release 构建成功；完整构建覆盖 Android、Browser、Desktop 和 iOS，结果为 0 错误、1 条 WebAssembly native reference 未链接警告。`git diff --check` 通过，活动上下文已改用仓库相对路径。
