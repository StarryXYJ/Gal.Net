# 游戏宿主接入外部调试

## 游戏接入

Sample 必须以 `EnableGameDebugging=true` 构建才能接入（Debug 配置默认开启，Release 默认关闭），`EnableLaunchArguments` 默认随之开启。发布给玩家的 Sample 不包含调试协议、工具和启动参数；Release 联调可显式传入 `-p:EnableGameDebugging=true`，或使用发行脚本的 `-SampleFlavor Developer` 生成带调试协议的开发包。两种发行 flavor 都不带 PDB，详见[调试构建与玩家发行](customize-sample.md#调试构建与玩家发行)。

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
