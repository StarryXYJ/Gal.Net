# 当前 Sample 结构与跨平台验收收尾

日期：2026-10-30。P6b–P6e 当前 Sample 范围已完成并验收；用户明确“我都测试过了，可以收尾了”。整体 feature 的独立模板、延期平台和发布专项继续保留，不升级为五平台完整交付。

## 实现结果

| 范围 | 最终职责 |
| --- | --- |
| 两个 Sample 主项目 | 各自拥有 SampleCustomization、主题/页面、项目文案、契约及执行注册；允许少量配置相似，不相互引用 |
| Avalonia.GameHost | 通用玩家视图、语言选择、scope/会话所有权、暂停/检查点及截图机制；不引用 Sample 或项目契约 |
| Storage.FileSystem | 非可寻址内容流安装、SHA256 分版缓存和稳定 projectId profile |
| 可选 Avalonia.Debugging | 调试命令/快照、有界日志、嵌入 UI/桌面外壳和外部会话适配；普通宿主无反向依赖 |
| 平台宿主 | 内容/可写目录、系统生命周期、返回、系统栏与适用平台动作 |

已删除 Sample.Player 的工程、生产引用和锁图节点，没有新建承担相同职责的替代 Player 工程。项目 replay 策略、重启/重建与打开目录策略留在宿主。玩家版通过编译和依赖图排除调试实现/协议/资源，正常日志保留。

发布矩阵现在为 Editor、CLI、原 Sample Player/Developer、CrossPlatform Player/Developer 六角色。玩家版拒绝调试 DLL/依赖；开发版要求调试实现。两种发行 flavor 都拒绝 PDB，因为外部调试通过协议而非源码级单步。各构建上下文内严格比较共享及项目契约，不放宽跨上下文的字节身份。

## 验证

| 检查 | 结果与边界 |
| --- | --- |
| P6d 自动化 | Presentation 333、调试版 Integration 197、玩家版 192、Architecture 56 通过；显式 native 未执行项独立计数 |
| 锁图 | 28 组 force-evaluate/locked restore；200 份既有锁文件的保留包版本/hash 和框架/RID 键核对 |
| Windows | 六角色发布通过；两个宿主×两 flavor 共四份实际进程完成初始化、私有运行库加载、落盘和确认退出 |
| Windows 外部调试 | 两个 Developer 各实际执行 3/3，覆盖连接、start/edit/seek/step/stop/旧 run 拒绝和正常退出 |
| Linux | 原生 SDK 六角色发布、四份 Sample 原生会话/落盘/确认退出通过；Ubuntu 26.04.1/WSLg 与本机媒体，不代表最低基线或原生桌面 |
| 发布保护 | Windows 7/7；Linux 6 passed、1 Windows-only skip |
| Android 构建 | 官方 NDK 正常源码构建，ARM64/x86_64 各 114 Mono AOT；完整 galpak、native 架构和 ELF/ZIP 16 KB 对齐通过，无临时 native 复用 targets |
| Android 设备 | ARM64 新包保留数据更新；冷启动、横屏标题、Back×2/Home×1 同 PID 重进、立绘/对白变化及触摸推进有自动证据 |
| Android 人工确认 | 用户确认本轮其余功能测试通过并要求收尾；视频与实际声音等按人工结果记录，未算作自动截取视频通过 |

Android 设备候选位于 `out/cross-platform-p6e/android-arm64/org.galnet.sample-Signed.apk`，78,191,374 字节，SHA256 `8ad2dc4ff83965faa32b6648348f31abee85b0daba7af1d7d9a29334120b4eb4`。保留原签名/profile；测试的临时旋转锁已恢复 free。

原始证据主要在 `out/cross-platform-p6b/` 至 `out/cross-platform-p6e/`；精确计数、失败与修正见[执行记录](execution.md)。Android/Linux 和外部调试测试使用 69a88546 的冻结源码，Windows raw publish 使用当时工作区；后续并行剧情/窗口/打包变更不由本批结果自动覆盖。

## 关键修正与限制

- 本地化保留精确文化来源，避免内层提前回退遮住外部默认语言翻译；删除工程后强制重算锁图，防止旧 Project 节点残留。
- 调试容器整体重挂 Activity，释放取消未完成确认并解绑内容；日志异步 UI 回调隔离旧挂载。
- Linux SDK 使用已有 ICU 依赖恢复正常运行，没有切换 invariant globalization。
- Android 内容通过同源/同配置 CLI 与实际移动契约重新导出；覆盖安装前核对证书并显式使用旧开发密钥，未卸载或清空玩家数据。见 [签名 lesson](../../.agents/lessons/L-20261030-16-android-update-signature.md)。
- 屏幕熄灭、安全锁屏或其他应用前台没有被计为 Sample 黑屏；未截取其他应用。自动视频探针未完成的结果保留，最终采用用户确认。

## 后续范围

独立模板生成/仓库外交付、macOS/iOS/真实手柄验收继续延期。移动系统音频中断/路由、进程终止恢复、方向/安全区完整矩阵、资源预算和生产签名/AAB，以及 Linux 最低基线/完整媒体/公开分发条件继续按[剩余计划](phase-plan.md)推进。未更新公开发行版本或外部 Wiki，P8 独立审核未由本次自审替代。

当前规范与操作入口已同步至[架构模块](../../docs/spec/architecture/modules.md)、[游戏宿主](../../docs/spec/presentation/game-host.md)、[Sample 定制](../../docs/manual/extensions/customize-sample.md)、[平台支持](../../docs/manual/sample/platform-support.md)和[发布矩阵](../../scripts/ci/README-applications.md)。
