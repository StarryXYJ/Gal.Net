# 对接外部游戏调试

调试由游戏开发者显式启用。`GalNet.Runtime` 只提供 Engine 的受控调试端口；可选 `GalNet.Debugging` 提供 loopback TCP、会话和默认 Engine 适配。游戏、Engine 和玩家数据仍由宿主拥有。

## Editor 操作

1. 在“项目设置”的调试启动配置中设置可执行文件、附加参数（每行一个）及可选工作目录，点击保存本地配置；也可以首次使用“测试 → 启动应用”时选文件。配置保存在项目 `.galnet/editor/debug-launch.json`。
2. 启动前保存并正式校验/编译工程，产物位于 `Output/Debug`。Editor 先监听，再向 App 传入该目录、`--profile <项目/.galnet/debug-profile>` 和调试邀请参数。该 profile 与游戏原有玩家目录分离。
3. App 连接后，若注册了开始回调，可点“开始游戏”；否则在 App 中自行开始。标题页连接和游戏运行绑定是两件事。
4. 打开“游戏调试”面板，分别查看 Player/Save 当前实例变量。修改输入框后点击“应用”，刷新不会覆盖草稿。原值已改变时应用会拒绝；使用重置按钮取当前值后再编辑。变量提交不产生作者 Undo 或工程 dirty。
5. Graph 的绿色运行标记独立于作者选择。右键 Group 跳到入口；选中 B/C/D/E 构成唯一完整直接后继链时优先使用该链，否则从有界候选中选路线。候选包含实际出口，条件不参与路线筛选。
6. Group 页面显示活动作者条目。条目右键可定位到其编译展开范围开头，也可切换断点。定位后暂停；继续或单步时仅目标作者范围一次性豁免条件，之后恢复普通语义。单步保留正常阻塞/Skip 行为，不强制播放完不可跳过媒体。

“当前 Group 末尾”在出边前停止。与游戏根断开的 Group 可以显式选“以初始场景从此处测试”，缺少前置对象时仍会报告实际错误。

调试面板顶部使用分组图标工具栏，悬停可查看操作名称，灰显表示当前不可用；窄 Dock 会按组换行。启动配置默认折叠。变量按“当前值／修改为”显示，勾选图标应用草稿，回转箭头取游戏当前值重置草稿；值冲突会显示文字提示。连接指示、运行状态和错误位于底部，较长错误可滚动并复制。

关闭调试 Dock 保留连接和草稿；关闭项目会断开。停止只停止当前运行，App 可以再次开始。默认 Engine 适配在断线后取消旧请求、清除断点并解除暂停，App 继续由玩家控制。断开不会终止外部进程。

“等待应用”用于 IDE 自行启动：面板显示构建目录、隔离 profile 和 `--debug-port`、`--debug-token`、`--debug-session`、`--debug-project`、`--debug-contract` 参数。邀请只针对本次监听。

## 游戏接入

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

需要呈现恢复的 view 必须提供 `ICatchUpPresentation` 或 `GameDebugOptions.SynchronizePresentation`。回调不得重新等待 Engine 推进门。默认 Avalonia 宿主提供恢复协调，只需通过 `AvaloniaGameHostOptions.AttachDebugRun` 的可选中立回调绑定；其程序集不引用调试通信组件。Sample 的 `SampleExternalDebug` 展示完整接入，包括对白、场景指令、变量/Gallery/截图副作用策略。

自定义运行可以直接实现 `IDebugGameRun`，按 `Info.Capabilities` 报告可用方法；可覆写 `OnDisconnectedAsync` 定义断线行为。它不要求实现 IGameRun 或持有 Editor 文档。

## 状态与缓存语义

定位沿显式边路线无条件追帧，保留当前 Player/Save 值，跳过声明为 Ignore 的变量赋值、Gallery 解锁和其他持久副作用。它生成测试场景，不恢复真实玩家历史。普通 `SkipToGroupEndAsync` 默认仍执行原条件和逻辑写入，和调试定位有不同用途。

每 Group 最多一份最近自然入口的纯场景；向后恢复入口再重放，向前先收束当前活动呈现再重放余下内容。调试追帧不覆盖自然入口。默认 LRU 上限为 256 个 Group、64 MiB 序列化 payload，可通过 `GameDebugOptions` 调整或设为 0；单独保留初始运行场景。该预算不表示精确托管堆占用。普通读档清缓存并推进执行代次。热点前缀和固定场景预计算未启用。

Player 修改先同步当前 Runtime 与玩家变量服务，默认 Avalonia 宿主在停止/释放运行时刷新到隔离 profile；Save 修改保留在当前运行，普通保存仍由游戏触发。面板不把“已同步”解释为“已写存档槽”。

当前协议校验版本、随机会话邀请、项目身份和构建内容指纹；请求按 runId/执行代次拒绝过期运行，变量还检查 UID/类型/原值。内容指纹覆盖 Graph、I18n、settings 及存在的 Contracts 目录；另可在 DebugIdentity 中提供 ContractFingerprint。Editor 使用项目已加载契约 DLL 的 SHA256，并通过 `--debug-contract` 传入预期值；Sample 对自己实际加载的 `ProjectSchemas` 程序集计算 SHA256，不匹配即拒绝。自定义 App 必须同样提供实际契约身份，不应直接信任命令行中的预期值。契约指纹不等价于执行模块程序集的代码指纹。

CLI/Editor/App 应使用同一份项目契约构建产物，包括默认只含内置条目的工程；选择 Sample 时需先部署 Sample 对应的 Contracts。运行内容来自构建目录，不提供资源热更新；作者内容编辑后旧源码映射失效，需要重新启动测试。

在仓库根目录运行 `./scripts/export-sample-contracts.ps1`（可选 `-Configuration Release`），会先构建 Avalonia Sample，再将配套契约及依赖 DLL 导出到根目录 `output/Contracts`。该输出目录已被 Git 忽略。关闭 Editor 项目，将导出的 DLL 复制到项目 `Contracts` 目录，重新打开项目，并使用脚本打印的对应 App 路径启动测试。

Editor 启动的 App 在连接前退出（包括退出码为 0），或连接后异常退出时，调试面板与 Editor 日志会报告退出码及最多 8192 字符的标准错误尾部。Sample 的命令行/契约检查失败也会输出异常并以非零码退出。断开调试只停止进程监控，不终止外部 App；IDE 自行启动的进程仍由 IDE 管理。

当前状态同步为 150ms 全量轮询，有帧大小、并发请求和路线搜索上限；流程暂停不完整冻结媒体时钟。Group/Entry 定位、变量修改和生命周期已有真实 Sample 进程及挂载 UI 测试，视频/音频/效果的全部组合仍需项目自行验收。
