# 游戏宿主分层重构

用户已确认：核心提供调度、编译和执行机制，指令由可选模块提供。保留 Core 现有模型位置，暂不实现 WPF、WinUI 或 Unity。

目标：Core/Runtime → 平台无关的游戏应用与展示契约 → Avalonia 实现与默认组合 → Sample/Editor。Assets 与 Storage.FileSystem 作为外部基础设施组合。

## 实施步骤

1. 移除项目目录删除与 ProjectTools.Windows；保留最近记录移除。
2. 下放默认 Avalonia 展示组合、纹理工厂及释放流程。
3. 抽出 GameHost 通用运行会话；Engine 独占执行机制。
4. 分离通用资源和 Avalonia 纹理预加载。
5. 下放玩家持久化协调，分离 Gallery 展示。
6. 分离玩家偏好规则与 Avalonia 设置 UI。
7. 调整平台无关接口归属，检查媒体后端边界。
8. 收敛 Sample、补充架构文档并执行回归。

每一步验证后独立提交。不得提交任务开始前已有的输入设置与编辑器改动。

## 验证记录

- Step 1 verified: Architecture 37/37; Editor.Shared 180/180; ProjectSchemaLifecycle 5/5.
- Step 2: 默认展示组合位于独立 Avalonia.GameHost，避免页面控件层反向依赖 Runtime/Builtins。模块由调用者配置；纹理工厂已下放。配置与清理回归 6/6。
- Step 3: 新增中立 GameSession/IGameRun/GameRun；Sample 的存档与替换共用命令门，Headless 也接入 GameRun。增加串行保存/替换、准备失败清理、停止失败与重复关闭测试。
- Step 4: GameResourceScope 位于文件基础设施，AvaloniaGameResources 仅持有渲染资源租约；移除 Sample 资源 scope。批量预加载逐个记录成功租约，部分失败时统一释放。
- Step 4 verified: 资源、配置与清理回归 7/7；经验见 `.agents/lessons/L-20261016-06-partial-preload-leases.md`。
- Step 5: PlayerSession 统一存档、空槽选择、继续选择、进度 flush、玩家变量和 Gallery 数据；FilePlayerSession 组合文件实现。Gallery 媒体 resolver 改由展示会话独立持有。清理协作者 ResourceCleanup 下放 GameHost。
- Step 6: PlayerPreferences/PlayerStartup 为中立偏好读写、非法值回退和首次启动规则；AvaloniaPlayerSettingsService 负责设置控件、预览 timer 与 UI 应用。默认窗口/手柄适配下放 Avalonia，编辑器 registry 可注入，项目本地化由 IPlayerLocalization 接入。
- Step 7: 会话/显示/Gallery 路径接口迁至 Presentation.Abstractions.Application；无 Avalonia API 的音频播放、音效处理和共享 LibVLC 生命周期迁至独立 GalNet.Media.LibVlc。视频画面仍由 Avalonia 适配。
- Step 8: 默认页面会话和运行绑定下放 Avalonia.GameHost；Sample 会话仅保留启动参数与调试策略。Layer/音频工厂可替换并统一转交所有权。增加无 Builtins/Layer 的自定义指令运行测试和工厂释放测试。

## 收敛结果

中立 GameHost 不注册内置指令、不要求 Layer Presenter；默认 GameRun 在借用 CompositeGameView 时可自行创建恢复会话，并先释放 Engine 后释放自身拥有的恢复会话。显式提供的恢复会话仍由调用者拥有。Headless 通过 Prepare/Start 走相同读档展示同步流程。

实际可替换点：catalog、执行模块、decoder、纹理/效果预加载策略、Layer 工厂、音频后端、设置字段编辑器、本地化适配。Sample 的 Session 只保留启动参数与调试策略。

## 分步提交与最终验证

| 步骤 | 提交 | 验证 |
| --- | --- | --- |
| 1 删除项目清理 | `76bf416` | 架构 37、Editor.Shared 180、项目生命周期 5 |
| 2 默认展示组合 | `edac1cd` | 配置/清理 6、架构 38、Sample 编译 |
| 3 通用运行会话 | `43248b4` | 会话 3、集成 7、架构 40、Headless 编译 |
| 4 资源分离 | `e63f650` | 资源/配置/清理 7，含部分获取失败释放 |
| 5 玩家持久化 | `ccc24fb` | 集成 8、架构 40、Sample 编译 |
| 6 偏好与设置 UI | `1b380f1` | 设置回归 25、中立层 4、架构 40 |
| 7 应用契约与音频后端 | `49b8976` | 展示 80、集成 6、架构 45、Editor 编译 |
| 8 Sample 收敛与文档 | 本次收尾提交 | 全部 12 个测试项目共 1,038 项通过，零失败；Release 全解决方案构建、格式与 IDE0005 检查通过 |

原生音视频播放和完整 GUI 人工流程未验收；自动回归覆盖媒体生命周期、音效逻辑、页面绑定、状态恢复与失败清理。Debug 全解决方案构建仅因正在运行的 Sample 锁定可执行文件而失败，独立 Release 输出的完整解决方案构建通过（零错误）。

## 后续补充：共享本地化扩展

Sample 的 `LocalizeExtension` 下放到 `Avalonia.GameView.MarkupExtensions`，与 `PlayerTextExtension` 共用 observable text 解析入口；Sample 的三个窗口/页面改用共享扩展。保留 Key 回退、StringFormat 和语言即时刷新，不依赖 SampleLocalizationService.Current，也不向中立层引入 Avalonia。Editor 的动态 Path 本地化扩展保留原实现。

验证：Sample XAML 与集成项目编译通过；本地化集成测试 11/11，包含实际 TextBlock 的中英文切换、格式化、未配置解析器与空 Key 回退。
