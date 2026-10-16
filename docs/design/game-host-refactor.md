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
