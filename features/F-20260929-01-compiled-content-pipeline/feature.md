---
id: F-20260929-01-compiled-content-pipeline
title: 编译内容管线与可运行示例
type: feature
status: implementation
created: 2026-09-29
updated: 2026-09-29
---

# 编译内容管线与可运行示例

## 原始目标

让项目以未编译的 `.rawgalgroup` 作为唯一可编辑剧情源；由 `GalNet.Editor.Headless` 将项目内容编译为可运行目录，再由 Headless/Avalonia Sample 启动该目录。导出必须复用同一编译路径。将 `GameTestCase` 从过短的 Runtime smoke fixture 恢复为能展示转场、动画和粒子的示例游戏。

## 当前事实与问题

- Editor 只加载和保存 `Graph/**/*.rawgalgroup`，Runtime 只加载已编译 `Graph/groups/<group-id>.galgroup`；这是正确的 authoring/runtime 边界。
- `GalgroupCompiler` 已能将 Raw group 展开为 Runtime primitive envelope，但当前只由 Editor 临时预览调用。
- `GalNet.Editor.Headless` 只有查询、编辑、校验和 export 命令，没有 build/compile 命令。
- 现有 `export` 原样收集 `Graph/**`，因此会把 Raw 内容直接装入 `.galpak`，不保证包可运行。
- 两个 Sample PowerShell 脚本固定运行仓库中的 `GameTestCase`，不构建项目。
- `GameTestCase` 直接提交 Compiled 文件，且只覆盖基本层、分支、效果和一个粒子调用；没有 authored transition 或 animation。Avalonia 页面虽已有粒子 presenter，但 Builtins 未创建粒子实例或调用它，因此 `particle.play` 实际仍是空操作。

## 范围

- 增加 Headless 的确定性项目 build 命令，产出独立可运行目录。
- 仅把图中引用的 Raw Group 编译为 Runtime `.galgroup`；递归发现 Raw 文件并对缺失、重复和孤立来源给出诊断。
- 让 package export 从相同的已编译 staging 内容构建，而不是复制作者源。
- 更新 Sample 启动脚本，使其先经 Headless build，再启动对应的播放器。
- 接通 `particle.play` / `particle.stop` 的 Runtime 状态与平台 presenter 桥接。
- 把 `GameTestCase` 改为 Raw authoring 输入，并扩充为展示转场、动画、粒子的短示例。
- 为编译、Headless、打包与目录运行添加自动化验证。

## 非目标

- Runtime/Sample 不读取或即时编译 `.rawgalgroup`。
- 不改变 Composite 展开、Entry schema 或 Runtime 的 Compiled 格式。
- 不实现 Editor 的文件监听、增量编译或热重载。
- 不复制、重构或覆盖当前工作树中无关的 Sample Debug 改动。

## 验收标准

- `build <project>` 可从只含 Raw Group 的项目生成可由两个 Sample 直接加载的目录；输出不含 `.rawgalgroup`。
- 一处 Raw Group 能位于 `Graph/` 下的嵌套目录；图引用、未引用和重复映射的错误可观察且不会生成半成品。
- `.galpak` 只携带 Compiled Graph 内容，且可由安装后的内容提供者加载。
- 两个脚本支持指定项目，默认 Sample 在每次运行前构建，并把构建输出传给播放器。
- `GameTestCase` 使用 Raw 文件，且其编译后执行路径覆盖可见 transition、animation 与 particle 呈现。
- 既有 Runtime 的“只接受 Compiled”契约保持不变。

## 约束与默认

- Core/Runtime 不依赖 Editor 或文件系统实现；编译 orchestration 留在 Editor.Shared/Headless。
- 开发构建必须输出完整独立目录（Graph、Assets、I18n、settings），而不是让播放器回退读取 authoring 根目录。
- 输出路径默认为项目 `Output`；构建操作只能替换该专用输出目录或用户显式指定的输出目录。
- `GameGraphContentLoader` 当前按 Group ID 寻找 `Graph/groups/<id>.galgroup`。本 feature 保持这一运行格式，Headless 将每个引用 Raw Group 编译到该规范位置。
- 导出使用独立 staging 目录，避免把中间文件写回用户 authoring 目录。

## 相关链接

- [设计](design.md)
- [实施计划](phase-plan.md)
- [文件格式规范](../../docs/spec/file-formats.md)
- [Runtime 规范](../../docs/spec/runtime.md)
- [运行时与呈现计划](../../docs/design/runtime-presentation-decoupling-phase-plan.md)
- [渲染/粒子计划](../../docs/design/render-effects-pipeline-plan.md)
- [GalgroupCompiler](../../src/GalNet.Core/Compilation/GalgroupCompiler.cs)
- [Headless CLI](../../src/GalNet.Editor.Headless/Program.cs)
- [导出器](../../src/GalNet.Editor.Shared/Services/GamePackageExporter.cs)
