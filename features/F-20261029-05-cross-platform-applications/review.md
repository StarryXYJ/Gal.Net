# 跨平台 feature 完成度核查

- 日期：2026-10-30。
- 基线：`774b35f7561708a785f5fa1109efebe3b9aa5da5`；核查开始时工作区干净。
- 范围：需求、设计、剩余计划、阶段总结、执行记录与当前关键实现的完成度分析；不是全部代码的逐行审查。
- 结论：**blocked（整体交付）**。Windows/Linux 已有可用且经过多轮验证的子范围，但桌面三平台和 Android/iOS 五平台模板的必要实现与验收尚未完成；可继续推进独立的 Windows/Linux 工作。

后续范围修订（2026-10-30）：用户已明确手柄、macOS、iOS 验收移到后续，并允许 Mono AOT。上述整体交付结论保留为本次核查的历史结论，不作为当前共享宿主/Desktop/Android 实现的进入门禁；最新依赖见 phase-plan.md。

后续实现核查（P6a）：当前切片为 **pass-with-follow-up**，允许继续独立模板及 Android 完整运行实现。共享代码集中迁入 Sample.Player，原/新桌面宿主实际运行、移动资产流安装/取消与会话清理回归、Android Mono AOT 发布均有证据。全量冻结源码 CI 2,206 通过、2 个已知 Editor 失败，真实当前 HEAD Debug Integration 191 通过。此为实现自审和验收进展，P8 独立审核、设备/完整媒体及延期平台不由此视为完成；最新覆盖及证据见 progress-summary.md。

## 已完成的子范围

| 领域 | 当前结果 | 验证边界 |
| --- | --- | --- |
| 内容导出 P1 | Editor 只提供 galpak/内容文件夹；完整 v4 包包含全部剧情分片，目录与归档共用准备流程，覆盖补丁独立交付 | 历史集成与挂载面板测试通过；无 SDK 的分发 Editor 原生 UI 验收仍缺 |
| Windows 发行 P2 | 布局清单、严格契约/hash、无 SDK compose、输入保护和 sh/ps1 入口 | P2 已 verified；历史真实窗口及正常退出有记录 |
| 媒体与 Linux 候选 P3 | Windows VLC 工厂统一；Linux 私有媒体闭包、tar 候选、开放字体回退、WSLg 启动入口 | 历史 WSLg 窗口/会话/正常退出、视频帧及音频/blur 人工反馈通过；未覆盖完整媒体及原生 Linux 桌面 |
| 合包工具 P4 | win-x64 ZIP、linux-x64 tar.gz/Unix 目录；源码 build-compose 的 SDK/locked restore/publish/安装/打包链路 | Windows 宿主生成 Windows/Linux 包有历史运行记录；Linux 宿主 build-compose 仍缺 |
| 桌面手柄 P5 | 独立 Input.Sdl3、自有窄绑定、固定原生包、替换 XInput；失焦/断连取消、中立重新武装、共享租约和缺库降级 | 历史 Windows/WSLg 各 3×40 正式探针、NuGet 外部消费者通过；真实设备/macOS 未验收 |
| 应用发布矩阵 P4/P5 | 四个 RID 的 CI 配置和锁图；Editor/CLI/Player/Developer 普通自包含发布入口 | 历史 Windows/Linux 原生 SDK 四应用发布通过；macOS 只有还原，无实际发布/运行证据 |

以上历史结果来自 execution.md / progress-summary.md，不能解释为本轮全部重新执行。

## 阻止整体交付的缺口

1. **P1：原生 Editor 无 SDK 导出未验收。** CLI 隔离和挂载面板测试已覆盖实现，但尚缺分发 UI 的两种导出、安装运行、取消与输出冲突保护的完整证据。
2. **P1：macOS 的发行实现和运行验收未完成。** `BuildComposeService.cs:36` 明确只接受 win-x64/linux-x64；.app、bundle 外内容定位、原生 loader、合法链接归档、签名/公证状态，以及 Editor/Sample/CLI 实际运行仍需完成。macOS restore 成功不等于应用支持。
3. **P1：移动模板尚未接入游戏。** CrossPlatform 的 `App.axaml.cs` 仅组合初始 MainViewModel，MainView 仍显示 Greeting；没有游戏会话。P6 集中同步、移动内容/沙盒、会话重建、触摸/返回键、安全区域和生命周期仍待实现；P0 Android/iOS 原生媒体与 AOT 风险探针应先提供证据，P7 真机/签名发布随后验证。
4. **P1：Linux 的完整发行闭环与契约来源仍需收敛。** 原生 Linux SDK 普通发布已完成，但宿主 build-compose、定制工程矩阵、同一契约工件如何供内容导出/源码合包使用尚未闭环。历史记录显示 Windows 工作树与 Linux 快照构建的 DLL hash 不同；严格校验应保持，不能把两套工件当作可互换。
5. **P1：最终平台验收和公开分发条件不足。** Linux 完整视频层效果/Gallery/缩略图/存读档/Editor 预览/外部调试、只读安装根及原生桌面待补；桌面真实手柄、IME/Dock/文件选择/全屏多屏待验收。Linux 媒体的许可、对应源码可用性及实际最低系统依赖须明确后才能公开分发。
6. **P2：最终候选未更新。** 历史 dev.9 应用包、dev.10 CLI 不包含后续全部 build-compose/SDL/矩阵改动；raw SDK 输出也不包含完整私有媒体及最终归档。完成实现后须固定同一候选重新解包、验 hash/契约/权限并运行。
7. **P2：文档入口仍有过时状态。** feature.md 的“当前事实”表仍描述 XInput/缺少 Linux 运行包；progress-summary.md 的更新时间、P4 行和最后边界仍含“Linux SDK 尚未实跑”，与后续已完成原生 SDK 发布段不一致。应以阶段总结最新增量和 phase-plan 为准，并在下一阶段收尾时统一这些入口。

## 本轮实际检查

- 核对 feature/design/phase-plan/progress-summary/execution/environment-matrix，以及导出面板、源码构建入口、SDL 项目部署、应用发布脚本/workflow 和 CrossPlatform 骨架。
- 当前 SDK `10.0.401`，Python `3.14.7`。
- `python scripts/ci/test-application-publish.py`：**4/4 通过**，覆盖输出保留、错误 SDK/native 拒绝、自有进程树超时清理及无关进程保留。首次受限运行因新建临时目录访问拒绝而无有效结果；正常权限下重跑成功。
- `git diff --check`：核查前通过。
- 当前工作区存在部分较早 P1/P2/P3 产物；未发现 `out/cross-platform-validation/20261030-applications/` 等最新归档目录。最新完整验证数字属于仓库历史记录，本机尚不能重新检查对应原始产物。
- 本轮未重新发布应用、执行 GUI/设备测试或修改业务代码。

## 建议继续顺序

1. 先恢复或重建本机需要的工具、契约和候选，明确构建来源；不把旧候选当作最新实现。
2. 收敛契约工件来源，完成 Linux 原生宿主 build-compose；使用原生分发 Editor 补齐 P1 两种无 SDK 导出。
3. 固定最新 Windows/Linux 完整候选，验证 Linux 完整媒体/调试、原生桌面及真实手柄，完成分发依赖与许可检查。
4. 并行条件允许时补 macOS 构建/封装/运行及 Android/iOS 风险探针；无对应宿主时明确保留未验收。
5. 桌面稳定且移动风险探针有结论后，按既定决定一次性集中接入 CrossPlatform，再完成移动真机与最终审核。

阶段状态保持：P2 verified；P0/P1/P3/P4/P5 in-progress；P6/P7/P8 planned。本记录没有将部分平台成功提升为整阶段通过。
