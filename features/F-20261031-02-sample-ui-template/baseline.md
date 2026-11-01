# P1 基线、消费者与迁移边界

源码基点：`d2d8665a4e02b8ce4ea66c57f61feab00a74d667`（完整身份以本阶段提交父提交为准）。检查日期：2026-10-31。

开始时业务源码干净；未提交文件为本 feature 文档、其他任务的 `.agents/lessons/L-20261031-03-tooltip-enabled-target.md`，以及本次测试输出。提交仅包含本 feature 文档与后续本 feature 修改，不包含该 lesson 或构建输出。资源鉴赏当前实现已进入基线。

## 真实消费者与所有权

| 对象/能力 | 当前消费者与所有者 | 目标处理 |
| --- | --- | --- |
| GamePageViewModel 的场景/对白/选项/等待/播放/历史 | GamePage、DialogueHistoryOverlay、SkipConfirmation、全部 Avalonia presenter、VideoPresenter；GameHost 的 GameRun/Presentation/Session/Settings，Debugging | 提取 GamePresentationState，机制留在 GameView，运行实例和作用域所有权不变 |
| GamePageViewModel 的保存/读取/设置/返回页面命令 | 内置工具栏、GameActionDispatcher、GameShell/Host 返回处理 | 应用 VM 与 Sample 接线拥有路由，设备输入通过窄回调交给应用 |
| Title/Loading/Settings/SaveSlots/About/Gallery 等普通页面 VM | GameLaunchFlow、GalleryNavigationService、注册表、GameShell、三套主题页面；Sample 与测试 | 迁入 Sample，移除内置层的具体页面目标 |
| GameSaveSlotRowViewModel | 存档卡片；拥有缩略图 Bitmap、编辑草稿和重命名错误状态 | 作为可复用控件模型保留内置，页面组织与返回归 Sample |
| SceneGalleryPlayerPageViewModel | scene 鉴赏详情；拥有独立 replay 呈现状态/表面和异步关闭 | 生命周期机制保留内置，应用返回与布局迁入 Sample；replay 不共用主剧情状态 |
| GameShell | 导航呈现/切换完成、输入监听、截图/确认、存档预览、具体页面判断 | 基础呈现/输入机制保留；具体页面流程和项目根布局进入 Sample，不能整类机械搬迁 |
| PlayerView / LanguageSelection | AvaloniaPlayerHost、Sample App、测试；首次选择、确认、公告/画布挂载 | 应用布局/文案进入 Sample；初始化取消、输入屏蔽及清理保留机制层 |
| AvaloniaPlayerHost | Sample App 与直接组合测试；拥有 root/scope/startup CTS 和 resolver 借用 | 保留机制所有权，解除固定页面/视图类型，项目负责 UI 决策 |
| GamePalette | Sample 窗口/玩家表面、主题、调试外层和渲染测试 | 保留语义配色及预设，独立于整页布局；不依赖页面类型 |
| IGameLayoutTheme / GameLayoutTheme / 三个主题对象 | GameShell、截图、SampleCustomization、测试；提供 styles+views | 删除抽象及三个对象，不另造同义协议 |
| Themes 普通页面、TitleMenu、GameToolbar | 三套完整布局 | 默认一套 XAML 迁 Sample，常改按钮展开；删除其他完整页面树 |
| Themes Common 设置/卡片等组件 | 各主题页面 | 按独立交互/资源所有权保留必要控件，纯应用组合进入 Sample |

源码检索 `src/Editor` 未发现 GamePageViewModel、GameShell 或 PlayerView 的 C# / csproj 消费；旧“Editor 与 Sample 共享页面”注释不能作为当前消费者证据。测试和历史 probes 中仍有默认页面消费者，迁移时分别调整有效测试与当前工具入口，不为历史执行记录重写已完成事实。

## 生命周期与唯一性

- AvaloniaPlayerHost：UI 线程创建 DI root 与异步 scope；取消初始化，等待结束，停止 session，解绑显示/视图，恢复 resolver，释放 scope/root。
- AvaloniaGameSession/GameRun：内容/玩家状态与单次运行；旧运行停止后替换；保存、媒体与资源按现有服务顺序释放。
- 主剧情使用一个 scoped GamePresentationState；scene replay 拥有单独状态/表面和运行句柄，结束必须停止并释放。
- 内置游戏控件拥有 scene/dialogue/NVL/choice/history 实例；页面只安排承载位置，不重复创建 presenter。模板 PART 校验继续有效。
- 页面导航为当前 scope 内历史，不进入持久化。启动、错误、返回标题、设置和存档目标由应用决定。
- 截图预览 Bitmap、存档卡片缩略图、Gallery 媒体 lease 仍按具体拥有者释放；不能因应用 VM 可编辑而下放异步正确性。

## 默认行为矩阵

| 范围 | 必须保持的基线 | 现有验证入口 |
| --- | --- | --- |
| 启动与退出 | 首次选择/取消、语言持久化、失败状态、确认焦点与取消 | AvaloniaPlayerHostTests、SampleStartupTests、SampleWindowThemeTests |
| 新游戏/继续/读档 | 准备与开始分离、加载呈现完成、取消、失败回退 | GameRunCoordinatorTests、SampleCleanupTests、SampleExtensibilityTests |
| 剧情呈现 | 对白、NVL、选项、图层/动画/粒子、媒体与截图 | AvaloniaPresenterTests、GameThemeTests、StateRestoreTests、ImageVariantsSampleTests |
| 播放交互 | 自动/快进/跳过、隐藏 UI、历史/重播、模态输入屏蔽 | PlayerInputTests、PageBackInputTests、PlayerInteractionTests、DialogueHistoryTests |
| 存档 | 保存/恢复、改名、缩略图、首个空槽、损坏槽行为 | SampleSaveSessionTests、GameNavigationTests、GameThemeTests |
| 设置 | 应用队列/恢复/持久化、字段编辑器、音频与显示/输入 | PlayerSettingTests、SampleInputSettingsTests、SampleDisplaySettingsTests、SampleReadTextColorTests |
| 鉴赏 | cg/video/audio/scene 解锁、播放停止、独立 replay 与取消 | ResourceGalleryTests、GameThemeTests.ResourceGallery、SampleCustomizationTests |
| 生命周期 | 同页重挂载不丢视觉树、暂停落盘、Activity 重挂载 | GameThemeTests、SampleActivitySurfaceTests、SampleCleanupTests |
| 配色与布局 | 默认 Classic+Mono、中英文、最小窗口、局部覆盖 | GameThemeTests、SampleWindowThemeTests、GameTypographyTests |
| 调试/发行 | Player 不含调试，Developer 正常注册，契约和 profile 身份不变 | SampleBuildModeTests、SampleAotPreparationTests、ProjectDependencyTests、发布工具 |

## 项目与发布边界

唯一应用源码项目保持 `GalNet.Sample.CrossPlatform`。Desktop/Android/iOS/Browser 为现有平台构建外壳，不承载第二份应用 UI/VM；是否支持/验收仍按已有平台规范。`GalNet.Sample.Contracts` 保留为内容工具与 App 共同消费的独立契约。

受影响构建路径包含主 solution、CrossPlatform 子 solution、Desktop Player/Developer、Android 双 ABI 及禁用调试锁图；独立契约编译身份、内容 PAK 与稳定 profile 策略保持。Themes 若改程序集/引用须同步 solution、架构测试、包锁图、发布消费者；先保留工程名清理职责，不把重命名与行为迁移混杂。

## 最小接口决策

1. P2 提取 `GamePresentationState`，保留所有真实呈现机制；默认页面暂用短期继承适配，不生成整套属性转发，最终 Sample Game VM 组合该状态，P4/P5 删除适配。
2. 页面切换继续使用现有 registry/DI；应用注册本项目类型。输入层以 `GamePresentationState` 工作，打开项目页面通过一个应用动作回调交回 Sample，不引入路由插件系统。
3. 通用宿主保留异步 scope/初始化/释放，语言选择及挂载由窄回调或必要表面契约接入；不以全部页面类型作泛型参数，不引用 Sample。
4. 主题对象彻底删除，配色作为资源提供；应用 styles/模板在 Sample；控件具备必要默认样式，不承诺跨模板整页替换。

## 验证证据

本地证据保存在 `.artifacts/sample-ui-template/p1/`，不提交二进制/测试输出；结果在阶段收尾时填写。所有测试均区分 headless 与原生，不以进程存活证明页面正确。

- Presentation 基线 TRX：`sample-template-p1.trx`，364 total / 358 executed / 358 passed / 0 failed；6 项未执行，不能将 notExecuted=0 解释为全执行。
- Sample 窗口定向 TRX：`window.trx`，2 passed / 0 failed。
- 架构 TRX：`architecture.trx`，56 passed / 0 failed。
- Sample 集成 TRX：`integration-unrestricted.trx`，80 executed / 79 passed / 1 failed。失败为 `GameTestCaseEditorTests.SampleOpensEditsSavesReopensAndBuildsWithItsOwnContract`，保存后 IsDirty=true 并导致关闭取消；发生于业务源码尚未修改的基线，不纳入本 feature 顺手修复。
- 保存/读取机制由上述 SampleSaveSession 等集成用例覆盖；尚未取得原生窗口存读档操作成功证据，P6 仍须完成，不能从机制用例推断。
- 默认中英文/紧凑窗口等 15 张截图保存在 `captures/`；已目视检查标题与中文最小窗口退出确认，作为默认视觉参考。
- 原生探针 `native.trx` 首次失败：测试复制资源但遗漏资源名称文案，初始化报 asset.evening.name 缺失。仅修正 `ImageVariantsNativeSampleTests` 夹具复制 en-US/resources.json；不改变产品行为。
- 修正后的 `native-fixed.trx` 已成功启动正式 Sample 并捕获 exact/reduced/default/scene/reset 五个不同的原生场景 PNG（每张约 2.8–3.1 MB），但关闭检查无法枚举出自有可见主窗口，故最终 1 failed，不能声明正常退出通过。证据为 `artifacts/image-variants-p5/native/` 本次目录；正常退出和真实窗口存读档保留为后续原生验收要求。
- 受限执行的集成 runner 曾因本地 testhost 连接 90 秒超时而 aborted（`integration.trx`）；放宽执行后取得上面的真实断言结果，不用 aborted 结果作通过证据。

P1 完成的是消费者/所有权、修订设计与可复查基线建立；已知失败与原生验收缺口如实保留，不代表默认功能全部通过。P2 后须比较相同测试，不能将这些失败隐去或无条件归因环境。
