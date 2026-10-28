# 对接外部游戏调试

Editor 的旧内嵌 Preview 已移除，游戏测试统一使用本页的外部进程调试链路。

调试由游戏开发者显式启用。`GalNet.Runtime` 只提供 Engine 的受控调试端口；可选 `GalNet.Debugging` 提供 loopback TCP、会话和默认 Engine 适配。游戏、Engine 和玩家数据仍由宿主拥有。

## Editor 操作

1. 在“项目设置”的启动配置分类中设置可执行文件、附加参数（每行一个）及可选工作目录；路径右侧的按钮可选择文件或目录，修改自动保存。也可以首次使用“测试 → 启动应用”时选文件。配置保存在项目 `.galnet/editor/debug-launch.json`。
2. 启动前保存并正式校验/编译工程，产物位于 `Output/Debug`。Editor 先监听，再向 App 传入该目录、`--profile <项目/.galnet/debug-profile>` 和调试邀请参数。该 profile 与游戏原有玩家目录分离。
3. App 连接后，若注册了开始回调，可点“开始游戏”；否则在 App 中自行开始。标题页连接和游戏运行绑定是两件事。
4. 打开“游戏调试”面板，分别查看 Player/Save 当前实例变量。修改输入框后点击“应用”，刷新不会覆盖草稿。原值已改变时应用会拒绝；使用重置按钮取当前值后再编辑。变量提交不产生作者 Undo 或工程 dirty。
5. Graph 的绿色运行标记独立于作者选择。右键 Group 跳到入口；选中 B/C/D/E 构成唯一完整直接后继链时优先使用该链，否则从有界候选中选路线。候选包含实际出口，条件不参与路线筛选。
6. Group 页面显示活动作者条目。条目右键可定位到其编译展开范围开头，也可切换断点。定位后暂停；继续或单步时仅目标作者范围一次性豁免条件，之后恢复普通语义。单步保留正常阻塞/Skip 行为，不强制播放完不可跳过媒体。

“当前 Group 末尾”在出边前停止。与游戏根断开的 Group 可以显式选“以初始场景从此处测试”，缺少前置对象时仍会报告实际错误。

调试面板顶部使用分组图标工具栏，悬停可查看操作名称，灰显表示当前不可用；窄 Dock 会按组换行。启动配置默认折叠。保存、构建、作者映射和启动阶段会显示在状态栏；构建与作者映射在后台执行，避免大项目校验占住 Editor UI。映射直接来自这次正式编译的结果，随构建内容一起返回；构建期间若作者内容改变，旧映射不会被认作当前作者版本。变量按“当前值／修改为”显示，勾选图标应用草稿，回转箭头取游戏当前值重置草稿；值冲突会显示文字提示。连接指示、运行状态和错误位于底部，较长错误可滚动并复制。

关闭调试 Dock 保留连接和草稿；关闭项目会断开。停止只停止当前运行，App 可以再次开始。默认 Engine 适配在断线后取消旧请求、清除断点并解除暂停，App 继续由玩家控制。断开不会终止外部进程。

“等待应用”用于 IDE 自行启动：面板显示构建目录、隔离 profile 和 `--debug-port`、`--debug-token`、`--debug-session`、`--debug-project`、`--debug-contract` 参数。邀请只针对本次监听。

## 游戏接入

Sample 必须以 `EnableGameDebugging=true` 构建才能接入（Debug 配置默认开启，Release 默认关闭）。发布给玩家的 Sample 不包含调试协议和工具；Release 联调可显式传入 `-p:EnableGameDebugging=true`。详见[调试构建与玩家发行](customize-avalonia-sample.md#调试构建与玩家发行)。

引用 `GalNet.Debugging`，在项目组合根创建服务。开始回调可省略，开始/停止回调中所需的 UI 调度由宿主负责。

```csharp
await using var host = new GameDebugHost(startGame: StartNewGameAsync);
await host.ConnectAsync(invitation, cancellationToken);

// 在宿主创建本次 Engine 后绑定；停止与释放 Engine 仍走原有宿主链。
using var adapter = new EngineDebugRun(engine, new GameDebugOptions
{
    Instructions = new Dictionary<string, DebugReplayBehavior>
    {
        ["dialogue.text"] = DebugReplayBehavior.Scene,
        ["variable.set"] = DebugReplayBehavior.Ignore
    }
}, stop: StopCurrentGameAsync);
using var binding = host.AttachRun(adapter);

// 生命周期结束：先 await engine.StopAsync()，再释放 binding / adapter。
```

上例白名单仅为接口示例，应列出实际支持的指令。Scene 表示模块支持 CatchUp 场景重建；Ignore 表示调试重放时跳过副作用；未声明的类型默认拒绝定位。不能把任意插件一律标为 Scene。参数和资源校验仍执行，不因忽略流程条件而放宽。

需要呈现恢复的 view 必须提供 `ICatchUpPresentation` 或 `GameDebugOptions.SynchronizePresentation`。回调不得重新等待 Engine 推进门。默认 Avalonia 宿主提供恢复协调，只需通过 `AvaloniaGameHostOptions.AttachRun` 的可选中立回调绑定；其程序集不引用调试通信组件。Sample 的 `SampleExternalDebug` 展示完整接入，包括对白、场景指令、变量/Gallery/截图副作用策略。

自定义运行可以直接实现 `IDebugGameRun`，按 `Info.Capabilities` 报告可用方法；可覆写 `OnDisconnectedAsync` 定义断线行为。它不要求实现 IGameRun 或持有 Editor 文档。

## 状态与缓存语义

定位沿显式边路线无条件追帧，保留当前 Player/Save 值，跳过声明为 Ignore 的变量赋值、Gallery 解锁和其他持久副作用。它生成测试场景，不恢复真实玩家历史。普通 `SkipToGroupEndAsync` 默认仍执行原条件和逻辑写入，和调试定位有不同用途。

每 Group 最多一份最近自然入口的纯场景；向后恢复入口再重放，向前先收束当前活动呈现再重放余下内容。调试追帧不覆盖自然入口。默认 LRU 上限为 256 个 Group、64 MiB 序列化 payload，可通过 `GameDebugOptions` 调整或设为 0；单独保留初始运行场景。该预算不表示精确托管堆占用。普通读档清缓存并推进执行代次。热点前缀和固定场景预计算未启用。

Player 修改先同步当前 Runtime 与玩家变量服务，默认 Avalonia 宿主在停止/释放运行时刷新到隔离 profile；Save 修改保留在当前运行，普通保存仍由游戏触发。面板不把“已同步”解释为“已写存档槽”。

当前协议校验版本、随机会话邀请、项目身份和构建内容指纹；请求按 runId/执行代次拒绝过期运行，变量还检查 UID/类型/原值。内容指纹覆盖 Graph、I18n、settings 及存在的 Contracts 目录；另可在 DebugIdentity 中提供 ContractFingerprint。Editor 使用项目已加载契约 DLL 的 SHA256，并通过 `--debug-contract` 传入预期值；Sample 对自己实际加载的 `ProjectSchemas` 程序集计算 SHA256，不匹配即拒绝。自定义 App 必须同样提供实际契约身份，不应直接信任命令行中的预期值。契约指纹不等价于执行模块程序集的代码指纹。

CLI/Editor/App 应使用同一份项目契约构建产物，包括默认只含内置条目的工程；选择 Sample 时需先部署 Sample 对应的 Contracts。运行内容来自构建目录，不提供资源热更新；作者内容编辑后旧源码映射失效，需要重新启动测试。

在仓库根目录运行 `./scripts/contracts/export-sample-contracts.ps1`（可选 `-Configuration Release`），会先构建 Avalonia Sample，再将配套契约及依赖 DLL 导出到根目录 `out/Contracts`。该输出目录已被 Git 忽略。关闭 Editor 项目，将导出的 DLL 复制到项目 `Contracts` 目录，重新打开项目，并使用脚本打印的对应 App 路径启动测试。

仓库对项目契约及其 Core/Builtins 编译引用禁用提交相关的 InformationalVersion 后缀和 SourceLink，以免仅提交无关文件就改变 DLL 身份；PDB 和本地调试仍保留，App 自身仍带提交信息。可用 `./scripts/contracts/test-contract-reproducibility.ps1` 对两个 SourceRevisionId 做真实 Rebuild 检查。首次迁移此构建规则需重导出并部署一次旧契约；源码、引用 API、生成器、SDK、路径或编译配置变化仍可能改变严格二进制身份。仓库外自定义契约及依赖也需自行保证构建稳定，不能只依靠相同程序集版本号。

Editor 启动的 App 在 30 秒内未连接时，调试面板会报告连接超时；Editor 不会因此终止 App。调试请求 60 秒无响应时，面板会结束等待并提示操作可能仍在运行；先检查运行状态再重试。状态/检查轮询的单次请求上限为 10 秒。

每次本地测试会话会在测试 profile 的 `logs/` 下生成 `debug-launch-*.log`，记录项目保存/构建/作者映射耗时、进程 ID、连接和调试请求结果、退出码及 App 的 stdout/stderr；创建时会把完整文件路径写入内置 Log Panel 的 Game 频道及 Editor Console。App 输出也会转发到 Editor 的内置 Log Panel（Game 频道；stderr 显示为 Error）和 Editor 的 Serilog Console sink，因此从命令行启动 Editor 时也能在命令行看到。单份诊断日志最多 1 MiB，最多保留 20 份；启动元数据不记录调试 token 或配置参数值，stdout/stderr 中的调试 token 会被遮盖。错误信息中会附诊断文件路径。Sample 的运行日志仍写入同目录的 `game-YYYYMMDD.log`。反馈问题时请一并提供这两类日志；启动前的契约校验错误可能只出现在 `debug-launch` 日志，因为它发生在 Game 日志初始化之前。

Sample 的命令行/契约检查失败会输出异常并以非零码退出。断开调试只断开控制通道，不终止外部 App；Editor 会继续排空其标准输出/错误并记录到本次诊断日志，直到 App 退出。IDE 自行启动的进程仍由 IDE 管理。

当前状态同步为 150ms 全量轮询，有帧大小、并发请求和路线搜索上限；流程暂停不完整冻结媒体时钟。Group/Entry 定位、变量修改和生命周期已有真实 Sample 进程及挂载 UI 测试，视频/音频/效果的全部组合仍需项目自行验收。
