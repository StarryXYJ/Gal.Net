---
status: confirmed
date: 2026-10-30
feature: F-20261029-05-cross-platform-applications
---

# Android 导出工具必须匹配契约构建身份

## 现象

Android ARM64 preview 先成功构建出三份运行时契约，却在 Headless CLI 导出临时 GameTestCase 时被严格契约加载拒绝：先是 `GalNet.Core` 程序集版本不同，统一版本后仍报告 `GalNet.Contracts.Builtins` 字节不同。

## 根因

内容导出会在工具自身加载的 Core/Contracts.Builtins 与项目 `Contracts/` 中的 DLL 之间执行严格字节校验。Android 发布使用 preview `Version` 和 `ContinuousIntegrationBuild=true`；Headless 默认构建没有这些属性。`Contracts.Builtins` 含源生成输入，因此即使程序集名称和版本相同，构建属性差异也会改变最终 DLL 字节。

## 正确做法

- 先以提交的锁图执行 Android `--locked-mode` restore；不要用单 ABI RID 覆盖该双 ABI restore 图。
- 构建 Android 契约后，使用相同的 preview `Version` 与 `ContinuousIntegrationBuild=true` 构建导出 Headless。
- 将刚构建的 Android Core、Contracts.Builtins、Project.Contracts 复制到临时作者项目，再由该 Headless 导出完整 galpak。
- 导出器返回的 `packagePath` 是唯一可信的 galpak 路径；不要依赖项目目录名推断文件名。

## 禁止做法

- 不要关闭 strict contract check、用旧 CLI DLL，或把 Android 契约替换为工具的契约来使导出通过。
- 不要用 `RestoreLockedMode=false`、`--force-evaluate` 或修改锁文件来绕过单 ABI preview 的 restore 失败。

## 发现与证据

`out/local-ci-preview-20261030/android-arm64-final8/android-publish.json`：SDK 10.0.401、locked restore、三份契约逐字节匹配、ARM64 Mono AOT 114 个程序集及 APK/ELF 16 KB 对齐全部通过。

