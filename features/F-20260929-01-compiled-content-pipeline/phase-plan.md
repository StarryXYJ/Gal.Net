---
feature: F-20260929-01-compiled-content-pipeline
updated: 2026-09-29
---

# 实施计划

## Phase 1 — 共享的可运行目录构建器

**状态：verified（通过 Headless 实际构建；GeneralTest 受 Avalonia licensing sandbox 阻断）**

目标：在 `GalNet.Editor.Shared` 建立从 Raw authoring project 到隔离 Runtime directory 的唯一构建路径。

- 实现 source mapping、递归发现、路径安全和 staging/atomic replacement。
- 使用既有 `GalgroupCompiler` 和 recommended target profile 生成规范 Group 输出。
- 复制运行时所需内容，并返回结构化构建结果。
- 添加针对嵌套 Raw 文件、遗漏/孤立/重复映射、输出隔离和 Runtime directory load 的测试。

退出条件：只含 Raw Group 的临时项目经构建后能由 `ProjectGameContentProvider` 读取，且生成目录没有 Raw Group。

## Phase 2 — Headless 与导出收敛

**状态：verified（实际 `.galpak` 检查仅含 Compiled Group）**

目标：让用户命令和 `.galpak` 与 Phase 1 的编译产物一致。

- 添加 `build` CLI verb、帮助文本与 JSON result。
- 重构 `export` 为私有 staging build 后打包；确保包内 Graph 只有 Compiled groups。
- 为 CLI 参数/失败码与 zip 内容加入测试。

退出条件：`build` 返回可运行目录，`export` 的安装内容可加载且不包含 `.rawgalgroup`。

## Phase 3 — Particle Runtime 桥接、Sample 脚本和内容覆盖

**状态：in-progress**

目标：默认 Sample 从 authoring fixture 构建并展示实际的呈现能力。

- 更新两个 PowerShell 脚本的项目、输出与跳过构建参数。
- 为 `particle.play` / `particle.stop` 实现 Runtime 状态、呈现调用和存档恢复语义，并加入模块级测试。
- 固化动画稳定快照语义：一次性非阻塞动画保存末值，循环动画保存第 0 帧状态与完整定义并在读档后从头重播。
- 将 `GameTestCase` 改为 Raw source，加入 transition、animation 与完整 particle 定义。
- 更新 fixture README；通过编译产物运行 Headless smoke，并为 Avalonia 路径保留可手工验证说明。

退出条件：脚本不再把 authoring root 直接传给 Sample，且 fixture 编译、内容加载与 Headless 执行通过。

**验证进展（2026-09-29）：** `particle.play` / `particle.stop` 已通过专用 Primitive 同步 Runtime 状态与 `IParticlePresenter`；`BuiltinPresentationReplay` 现按 effect → particle emitter → looping animation 的顺序重建展示。一次性非阻塞动画保存末值，循环 `animate` / plan 保存第 0 帧状态与完整定义并从头重播，Avalonia plan 也会按 `Loop` / `PingPong` 持续采样。Runtime/Engine 定向测试 24/24 通过，Headless Sample 独立构建为 0 warning / 0 error；全量 `GeneralTest` 为 240/241，唯一失败仍是未触及的 `EditorSettingsSerializationTests.LastDockLayout_RoundTripsAsAString`。本 Phase 仍等待完整交互式 Sample smoke 后再标记 verified。

## Phase 4 — 回归、文档与收尾

**状态：planned**

目标：固化文件格式和可验证行为。

- 更新当前事实文档，说明 Source/Build/Export 的边界与命令。
- 运行受影响项目构建和 `GeneralTest`；区分已存在失败。
- 将每个 Phase 的证据和偏差更新到 feature 记录。

退出条件：所有新增测试通过、文档与实现一致、没有未解释的 feature 范围失败。
