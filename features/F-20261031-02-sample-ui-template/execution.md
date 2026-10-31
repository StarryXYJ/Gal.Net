# 实施记录

## P1：基线与修订设计

提交：`65bf293d`。消费者、所有权、测试及未验证边界见 [baseline.md](baseline.md)。本阶段修正原生测试夹具遗漏的资源文案，不改变产品行为。

## P2：共享呈现状态

提取 `GamePresentationState` 及 Playback/History partial，持有原场景、对白/NVL/选项、等待、播放与历史交互机制。GamePage、各 presenter、视频、输入、GameRun/Presentation/Session、设置和调试保存接线使用该对象；音频公告也绑定该状态。

默认 GamePageViewModel 暂仅提供存读档、设置及返回页面的命令，继承状态并通过 Scoped DI 别名与运行链路共享唯一实例，不复制状态和转发全部属性。状态暂继承仅含 ObservableObject 的 PageViewModelBase，使旧导航页面继续可运行；P3/P4 将游戏页改为组合状态，P5 删除此过渡继承及旧页面适配，不作为最终公开应用模型。

GameActionDispatcher/PlayerInputRouter 使用活动呈现状态；应用页面动作以单个 `Func<string, Task>` 回调交回当前 shell，设备输入不再引用具体页面 VM。取消播放、首空槽保存及其活动状态检查仍在内置机制执行。

验证：

- `.artifacts/sample-ui-template/p2/presentation-final.trx`：367 total / 361 executed / 361 passed / 0 failed，6 项显式用例未执行，与基线的未执行范围一致。新增 3 项：作用域状态身份、无页面/导航的选项取消后恢复、应用动作回调与交互阻塞。
- `.artifacts/sample-ui-template/p2/integration.trx`：80 executed / 79 passed / 1 failed。失败名称及错误与 P1 相同：Editor 保存后的 IsDirty=true，随后 CloseRequestedAsync 取消。没有新增 Sample 集成失败。
- 受影响构建通过，现有场景鉴赏、模态输入、重挂载、存档与清理测试随上述回归执行。
- 修改/新增 C# 文件的 `dotnet format whitespace --folder --verify-no-changes --include ...` 通过；`git diff --check` 通过。solution 模式格式检查因受限命名管道无法连接 BuildHost，改用不加载项目的文件夹模式完成同一批文件的空白检查。

原生窗口关闭、真实窗口存读档和其他平台验收仍沿用 P1 缺口，不从本阶段 headless 结果推断通过。主题/普通页面仍为迁移前结构，尚未完成用户目标；后续按 P3–P6 继续，不把这次状态提取描述为 UI 模板改造完成。

## P3：项目标题、加载与游戏页切片

Sample 应用项目新增 Views/TitlePage、LoadingPage、GamePage 及其 VM，标题按钮与完整游戏工具栏在项目 XAML 展开；开始/继续和项目页面命令可以直接编辑。GamePageViewModel 继承通用 PageViewModelBase 并组合 GamePresentationState，不派生默认游戏 VM，不镜像呈现属性。SampleCustomization 在一个入口注册项目 VM/View、加载/失败回退与输入动作；稳定 GamePage 控件别名与实际页面解析为同一 scoped 实例。

内置层新增 IPlayerApplication，借用应用表面及启动/返回/退出策略；AvaloniaPlayerHost 仍拥有 root/scope、初始化取消、会话与释放，不解析具体页面 VM。外部调试开始改为调用项目应用的 StartNewGameAsync，避免绕过项目标题命令。GameShell 通过 IGameplayPage.State 获取交互状态，返回标题和应用输入由回调提供。GamePage.Presentation 与页面 DataContext 分离，内部呈现部件显式绑定运行状态。

GameLaunchFlow 不再依赖页面类型，接收三个呈现回调；仍保留加载最低停留时间和准备/播放分离。失败路径补充取消后等待准备任务结束，再回退 UI；门控回归检查准备清理尚未完成时加载流程不能提前返回。

迁移边界：保留短期 DefaultPlayerApplication、旧主题及其他普通页面，供 P4/P5 继续迁移；没有新增 Sample UI/VM 项目，没有启用运行期主题切换。语言选择/确认/公告仍使用当前通用视图，未复制其机制到项目。

验证证据（本地输出不提交）：

- `p3/presentation-final.trx`：370 total / 364 executed / 364 passed / 0 failed，6 项显式用例未执行。包括准备/呈现顺序、失败清理等待，以及禁止解析默认标题/壳/PlayerView 时自定义根表面仍能初始化、暂停和释放。
- `p3/integration-final.trx`：91 executed / 90 passed / 1 failed；唯一失败仍为 P1/P2 已记录的 Editor 保存后 dirty，不是新增失败。范围包含 Sample、PlayerInteraction、ResourceGallery、CrossPlatformWindow 和 SharedPlayerHost。
- `p3/template-debug-final.trx`：最终增量补验 6 passed / 0 failed。模板测试将旧标题/加载/游戏 VM 的解析显式设为抛错，仍完成应用启动命令、新游戏/继续、加载失败/取消、自定义页面进入/返回及工具栏点击不推进剧情。
- `p3/architecture.trx`：56 passed / 0 failed。
- `p3/native-final.trx`：显式原生用例 1 passed / 0 failed。正式 Sample 通过项目启动命令进入游戏，捕获 exact/reduced/default/scene/reset 五张不同场景 PNG，使用自有可见主窗口确认退出，进程退出码 0。该用例不验证实际窗口中的手动存读档或所有媒体/移动平台。
- `p3/captures/`：已检查标题页及中文最小窗口退出确认，与 P1 保持原布局；只作为当前默认布局参考，不声称逐像素相同。
- 修改/新增 C# 的文件夹模式空白格式检查、`git diff --check` 通过。

P1 原生退出缺口已定位并修正测试夹具：NativeSampleWindow 使用过时标题前缀 `GalNet CrossPlatform`，实际窗口为 `GalNet Avalonia CrossPlatform Player`。现在按实际标题精确识别，同时保留自有 PID、可见性和唯一窗口检查，没有放宽为任意窗口或以强杀代替退出。先前失败证据继续保留。

正式宿主/页面规范、Sample 定制说明、页面扩展示例与 Sample README 已同步本阶段事实。P4–P6 仍待实施，特别是其他页面与全局布局迁移、整套主题删除、仓库外模板及平台/发行验收。
