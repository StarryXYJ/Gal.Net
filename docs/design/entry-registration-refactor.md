# Entry 契约与执行注册迁移

用户授权：不保留旧 API/存档兼容；排除 Editor/Launcher 功能改造，允许迁移其公共 API 调用点以保持构建。

## 目标与决定

Entry module 只拥有参数契约和编译期 composite。执行 module 只拥有完整指令 ID 与实例工厂，并可持有共享服务/状态。模块名和指令前缀不是排他注册键，完整 ID 才是。宿主通过 GameRegistration 独立注册契约与实现，在创建游戏时冻结并转移执行模块所有权。

内置入口 AddBuiltins 组合 AddBuiltinSchemas 与 AddBuiltinImplementations；每个领域有独立实现注册入口。音频轨道逻辑可复用 AudioRuntime，平台通过 IAudioPlaybackHost 组合实现；无需继承播放器基类。平台后端不进入 schema。

## 实施顺序

1. 契约/工厂拆分、注册入口、领域拆文件、消费者迁移与注册测试。
2. 删除旧协议与重复状态，统一快照复制及读档取消，补充生命周期回归测试。
3. 修正音频 pending-load/命令顺序、动画统一播放时间源、诊断和宿主生命周期。
4. 顺序执行边界测试、集成测试、格式检查并同步当前文档。

## 验收

纯 schema 注册不创建执行服务；schema-only、自定义实现、部分内置实现、全内置均可组合；相同前缀不同指令可注册，完整 ID 重复报错；缺失实现明确失败；旧异步任务不能污染读档后状态；快照与调用者隔离；停止待加载音频后不能晚开始；动画轨道与事件使用同一播放进度。

## 风险

公共 API 破坏性迁移需要同步测试及所有消费者。取消并不等待所有外部任务，旧任务必须使用运行代际/状态对象隔离。后端资源销毁必须先取消待加载、等待工作结束后删除临时目录。

## 实现结果（2026-10-10）

目标已落地。领域执行模块与 runtime actions 分文件维护，schema 的语义验证用于编译和运行期，包括 composite 展开的指令；重复的执行层视频参数检查移除。缺失实现、未编译作者条目、参数错误和 BatchId 不一致均明确拒绝。

删除无消费者 IAudioService、LibVlcAudioController、调用栈、冗余场景 ID 索引，并把空 GameView 移入展示抽象，删除 Presentation.Defaults 项目。旧文件可通过 Git 恢复。GameSnapshot 升级为 3；输入/输出快照复制，读档重置结束标记并隔离旧 scope/场景。

音频逐 voice 有序提交，Stop 取消 pending Start，平台 host 跟踪加载并异步排空；native 回调只排队信号，播放器清理不在回调线程。新 fade 取消旧 fade。Sample 明确释放 engine、view、后端与 DI scope。动画事件由 presenter 的同一帧进度驱动，旧完成按 identity 清理。项目/安装内容现在读取 settings.json，两种 Sample 将自定义轨道配置注入 Runtime。

## 验证与边界

- 10 个测试项目顺序执行：Architecture 24、Core 57、Runtime 38、Builtins 52、Assets 32、Storage.FileSystem 12、Editor.Shared 16、Editor 28、Presentation 76、Integration 38，共 373 项通过。测试构建关闭分析器并禁用 shared compilation。
- 使用 Sample 携带的 Windows x64 LibVLC，显式运行 AvaloniaAudioLifetimeTests 的停止/销毁两个 native 测试，均通过。
- Sample.Headless 和 Editor.Headless 单独构建成功；Editor 仅公共 API/namespace 消费点迁移，Launcher 未修改。
- 受影响依赖锁强制重算后 locked restore 成功。solution whitespace/imports 门禁检查通过，git diff --check 无错误。
- 普通 Shared 构建开启分析器后无错误，但仍有命名、性能与生命周期等非阻断告警；Avalonia 仍有既存 AVLN3001 告警。本轮未声称全仓零告警，也未进行完整 GUI 交互验收或实现 Unity 后端。

使用方式见 [二次开发手册](../development.md)，当前契约见 [runtime](../spec/runtime.md)，决定见 [ADR-0002](../adr/0002-entry-schema-execution-registration.md)。

## 实现结果（2026-10-10）

目标已落地。领域执行模块与 runtime actions 分文件维护，schema 的语义验证用于编译和运行期，包括 composite 展开的指令；重复的执行层视频参数检查移除。缺失实现、未编译作者条目、参数错误和 BatchId 不一致均明确拒绝。

删除无消费者 IAudioService、LibVlcAudioController、调用栈、冗余场景 ID 索引，并把空 GameView 移入展示抽象，删除 Presentation.Defaults 项目。旧文件可通过 Git 恢复。GameSnapshot 升级为 3；输入/输出快照复制，读档重置结束标记并隔离旧 scope/场景。

音频逐 voice 有序提交，Stop 取消 pending Start，平台 host 跟踪加载并异步排空；native 回调只排队信号，播放器清理不在回调线程。新 fade 取消旧 fade。Sample 明确释放 engine、view、后端与 DI scope。动画事件由 presenter 的同一帧进度驱动，旧完成按 identity 清理。项目/安装内容现在读取 settings.json，两种 Sample 将自定义轨道配置注入 Runtime。

## 验证与边界

- 10 个测试项目顺序执行：Architecture 24、Core 57、Runtime 38、Builtins 52、Assets 32、Storage.FileSystem 12、Editor.Shared 16、Editor 28、Presentation 76、Integration 38，共 373 项通过。测试构建关闭分析器并禁用 shared compilation。
- 使用 Sample 携带的 Windows x64 LibVLC，显式运行 AvaloniaAudioLifetimeTests 的停止/销毁两个 native 测试，均通过。
- Sample.Headless 和 Editor.Headless 单独构建成功；Editor 仅公共 API/namespace 消费点迁移，Launcher 未修改。
- 受影响依赖锁强制重算后 locked restore 成功。solution whitespace/imports 门禁检查通过，git diff --check 无错误。
- 普通 Shared 构建开启分析器后无错误，但仍有命名、性能与生命周期等非阻断告警；Avalonia 仍有既存 AVLN3001 告警。本轮未声称全仓零告警，也未进行完整 GUI 交互验收或实现 Unity 后端。

使用方式见 [二次开发手册](../development.md)，当前契约见 [runtime](../spec/runtime.md)，决定见 [ADR-0002](../adr/0002-entry-schema-execution-registration.md)。
