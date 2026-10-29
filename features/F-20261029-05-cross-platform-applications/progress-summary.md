# 已完成阶段总结

更新：2026-10-29。这里只汇总已实现、已验证的子范围；整个 feature 仍在进行中。[剩余计划](phase-plan.md)负责后续任务，[执行记录](execution.md)保留原始结果、失败和限制，[环境矩阵](environment-matrix.md)记录平台证据。

## 已完成的交付

| 范围 | 结果 | 验证与提交 |
| --- | --- | --- |
| P0 内容闭包、P1 实现 | Editor 仅保留 galpak/内容文件夹；完整 v4 归档包含全部剧情分片，与目录导出等价；不保留旧安装格式及旁置剧情导出 | 集成 181 项、挂载导出 2 项通过；当时 Editor 全量有 2 项既有失败，原生分发 UI 验收仍待补。`5cc98d23` |
| P2 Windows 无 SDK 合包 | 布局元数据、严格契约/hash、纯运行包与内容组合、输入保护及 sh/ps1 薄入口 | 分发 CLI 合包和合成 Sample 原生启动/正常退出通过。`38988818` |
| P3a Windows 媒体 | 剧情视频、Gallery/缩略图和 Editor 预览共用 VLC 初始化工厂，播放器独立释放；完整默认契约部署 | 真实视频像素、Gallery、Editor 音频与包内依赖加载通过；历史全量测试的既有失败保留在执行记录。`42ecb004` |
| P3b 字体与 WSLg | 内置 Inter 默认字体、开放 CJK 回退；WSLg 条件选择 d3d12，保留用户 profile | Presentation 302 项通过；Sample 窗口/会话/退出通过，用户确认声音和 blur 人工复测正常，未测帧耗时。`fbac8cfc`、`455e1965`、`1e3c8cde` |
| Windows 发布稳定性 | 原子 Move 对短暂 Windows 句柄冲突有界重试，保持取消、旧输出和回滚语义；生成 dev.9 | Storage 32 项、Editor.Shared 494 项、两种分发 DesktopSmoke 和连续 40 次导出通过。`27c02fa5` |
| P3b Linux 本地候选 | Ubuntu 24.04 私有媒体闭包、Editor/CLI、纯/整合 Sample tar.gz 和 Contracts；保留权限、空目录及来源记录 | 独立解包 hash、真实 Editor/Sample 启动退出、包内 VLC/FFmpeg 视频帧、两种导出与安装通过。WSLg 证据不替代原生桌面和完整媒体验收。`27c02fa5` |
| P4 Linux 无 SDK 合包 | tar.gz/Unix 目录运行包和 galpak/内容目录输入，重建清单、保留执行位与空目录，拒绝不安全 tar 条目 | Editor.Shared 504 项、打包保护 8 项、WSL 两组真实合包及合成 Sample 正常启动退出通过。`d3b173fa` |
| P4 源码 build-compose | 显式应用工程、apphost、RID/版本/配置；SDK 检查、锁定还原、普通自包含发布、标准布局与严格合包；结构化退出码/日志、取消自有 SDK 进程树 | Editor.Shared 515 项、Windows 原生 1 项通过；真实 Sample 在 Windows 宿主生成 Windows/Linux 包，逐文件 hash、包内运行时/媒体、两平台启动退出通过。Linux 宿主 SDK 尚未实跑；完整证据见执行记录 |

## 当前交付与证据

- Windows 应用候选：`out/release/0.1.0-dev.9/`。
- Linux 应用候选：`out/linux-release/0.1.0-dev.9/`。
- 新 Linux compose CLI 候选：`out/linux-compose-cli-dev10/`；旧 dev.9 应用包未重写，原包中的 CLI 不自动获得新能力。
- `build-compose` 位于当前源码 CLI 构建；未重新封存整套 Editor/CLI 发行包，旧候选不自动获得该入口。
- 精简验证与清理清单：`out/cross-platform-validation/20261029/`、`out/cross-platform-validation/20261029-compose/`；两次清理约 21.1 GiB、2.9 GiB，保留用户存档、手测安装和最新候选。
- 以上均为本地候选，不宣称精确 tag 的公开发行。操作见 [发行打包说明](../../docs/manual/release-packaging.md)，媒体边界见 [跨平台后端说明](../../docs/manual/cross-platform-backends.md)。

## 仍须保留的边界

P1 原生 Editor UI 两种导出、P0 移动风险探针、完整 Linux 媒体/Gallery/调试及原生桌面、公开再分发许可和对应源码可用性、Linux 宿主 SDK/定制工程矩阵、macOS/签名、SDL、跨平台模板与移动真机均未因此完成；具体待办在剩余计划中。

实施约定继续有效：不考虑旧格式兼容；P6 前只维护现有 Sample 和公共库，桌面稳定后再集中接入 CrossPlatform；macOS 环境缺失不阻挡独立的 Windows/Linux 切片。每个可验证阶段完成后提交相关代码、测试和文档，将计划中已完成的步骤归并为本总结，保留未验收项及证据。
