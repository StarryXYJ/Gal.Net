# P7：资源失败诊断贯通

## 边界与实现

`AssetManager.Diagnostic` 是可选的 `Action<AssetDiagnostic>` 事件，诊断数据定义在 Assets。Core 的 `IAssetManager`、provider/decoder 公共协议保持不变，Assets 不依赖 Runtime、Editor、Serilog 或 UI。加载器只报告事实，由宿主选择日志与展示方式。

诊断包含阶段、失败类别、可用的逻辑 ID/路径、provider 名称、目标 CLR 类型及原异常实例。找不到资源时保留请求键；尚未解析的身份不会伪造。provider 抛出 `InvalidDataException` 归为无效数据，原异常保留具体 PAK/元数据/校验原因，不通过解析异常字符串猜测错误码。解码器抛出同一异常类型时仍明确归为 DecodeFailure，避免误报为包校验错误。

| 阶段 | 失败类别 | 行为 |
| --- | --- | --- |
| Lookup | NotFound、InvalidData、ProviderFailure | provider 按原顺序回退；某 provider 已报错时不再追加 NotFound |
| Read | InvalidData、ReadFailure | 保留已解析文件及来源，失败仍完成为空结果 |
| Decode | DecoderMissing、DecodeFailure | 未注册 decoder、返回 null、抛错均有诊断；不改变显示回退 |
| Enumeration | InvalidData、ProviderFailure | 保留正在枚举的资源 ID，仍尝试后续 provider |

内部 ResolvedAsset 将 provider 与已找到的文件一起传入读取/解码阶段，不将它们放进另一个缓存。ResourceOverlayProvider 的内部文件提供实际 provider 和替换资源 ID，诊断同时保留逻辑原资源身份；不会把来源只记成 ResourceOverlay。移除 InFlightLoad 中已无使用方的重复目标 Type 状态。

按路径的共享失败测试进一步发现原有重试误判：空句柄会删除路径到 ID 映射并重复加载，即使文件已找到而只是读取/解码失败。修正为共享 completion 返回是否找到文件；原来的 completion 结果对象没有消费者，资源仍由原缓存拥有，因此没有添加第二份资源状态。路径只在未找到文件时重查；读取/解码失败返回原有空句柄，后续显式调用仍可重试。新增测试分别确认真正 ID 替换可重查、暖路径读取失败只尝试一次及后续恢复。此项是由测试驱动的行为修正，不是仅把重复日志隐藏起来。

诊断在缓存锁外调用；每个观察者单独隔离异常，不因日志失败阻断共享 producer 完成或其他观察者。没有订阅者时保留 Trace；Trace listener 抛错也被隔离。操作取消不报告为资源失败。事件同步执行，观察者不得阻塞/同步等待当前加载；没有新增后台队列、锁或常驻失败缓存。

编辑器每个 generation 的 manager 连接现有 Serilog，包含 SourceContext；AvaloniaGameSession 在配置 decoder/预加载前连接 GameLog，进入玩家日志，开发版可进入 SampleDebugLogStore。两个宿主只负责日志适配，不把 UI 逻辑下沉到 Assets；已有通用日志视图无需增加独立资源面板。

## 验证

- 新增诊断单测覆盖 ID/路径缺失、provider IO/无效数据及后续成功回退、真实无压缩 PAK payload 篡改、延迟读取来源、缺少/null/抛错 decoder、12 个共享等待者只报告一次、取消不影响另一等待者、三阶段取消、失败后成功重试、监听器异常隔离与锁外执行、枚举回退、无监听方。
- 覆盖包用真实 override 构建/选择逻辑后模拟 backing provider 校验失败，确认原 ID/路径、替换 ID、实际 provider 与原异常全部保留。
- 编辑器日志测试跨 manager generation 核对 NotFound/ProviderFailure、SourceContext 与原异常实例；同时复用资源身份/索引/Picker 回归。
- 玩家测试经过真实 AvaloniaGameSession 初始化路径，确认缺失/provider 失败进入 Game 通道且初始化成功。Release 验证通用 Serilog sink；Debug 额外核对现有 SampleDebugLogStore 的完整异常文本。

本阶段新增 27 项用例（Assets 25、Editor 1、Integration 1；同一宿主用例分别在 Release/Debug 验证）。最终结果全部退出码 0：Assets 全量 248、Architecture 53、ResourceIdentity 文件全部 8、SharedPlayerHost/GameInstallation 的 Release 集成 18、玩家日志与 SampleDebugLogStore 的 Debug 专项 3 项通过。P1 的共享等待者生命周期测试随 Assets 全量一起通过。保留仓库已有分析器/空值警告；新日志测试的区域设置提示已改为显式 InvariantCulture。

命令统一使用 `--no-restore --nologo -m:1 -nr:false -p:UseSharedCompilation=false`，默认 Release，调试日志专项使用 Debug；界面/集成测试设置 `AVALONIA_TELEMETRY_OPTOUT=1`。日志：

- `out/project-quality-p7-diagnostics.log`：首批 21 项诊断测试通过。
- `out/project-quality-p7-assets.log`；`out/project-quality-p7-path-failure-before.log` 保留路径测试修正前 245 通过/1 失败的证据。
- `out/project-quality-p7-architecture.log`
- `out/project-quality-p7-editor.log`
- `out/project-quality-p7-integration.log`
- `out/project-quality-p7-debug-log.log`

初次集成测试直接引用 SampleDebugLogStore，Release 编译 CS0234 失败。项目在 `EnableGameDebugging=false` 时明确移除该类型及既有专项测试；修正为所有配置验证通用日志，`GALNET_GAME_DEBUG` 下再验证调试 store，没有放开发布版裁剪。失败日志保留为 `out/project-quality-p7-integration-initial-build.log`；这是测试配置修正，不改生产功能开关。

## 限制

去重范围是一个共享 producer/一次 provider 尝试：共享等待者不重复写同一失败，provider 错误后不追加缺失。读取/解码失败不会再误触路径重查；独立重试、未找到文件时的路径重查和不同 provider 失败仍分别可见，不承诺全会话相同文字只出现一次，也不缓存失败来阻止恢复。路径查找本身仍遵循原有调度方式。经验记录见 `.agents/lessons/L-20261030-09-asset-path-failure-retry.md`。

本阶段连接编辑器资源 generation 与 Avalonia 玩家资源会话；CLI、Headless 及启动内容加载中的临时 manager 未自动连接宿主 Serilog，未订阅者沿用 Trace。作者资源索引直接抛错、启动内容错误和资源释放错误保留原边界，不在本阶段统一成可恢复加载。没有原生窗口人工验收、跨进程日志服务、性能基准或全平台发行验收。正式 docs 留给用户后续整理。
