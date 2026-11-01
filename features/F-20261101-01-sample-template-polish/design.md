# 精简设计

状态：implemented。四项优化共享同一目标：项目只表达自己的选择，固定机制与构建接线不要求开发者反复理解。下文保留设计决策与取舍，实际结果和限制见 summary.md。

## 模板交付

继续维护唯一 Sample 应用源码。先梳理 Sample 目录对根 Directory.Build.props、Directory.Packages.props、packaging/GalNet.Player.props 和平台 targets 的实际依赖，区分项目必需配置与仓库维护配置。

优先复用现有包交付和 MSBuild 能力，为同一份源码提供明确的独立包消费入口及随项目交付的最小配置。版本有单一来源，可选 Debugging 与玩家版依赖保持隔离。仓库开发使用源码引用，交付消费者使用包引用；差异仅为依赖来源，不形成两套应用 UI 或运行机制。

不复制整个仓库构建系统到消费者，不以代码生成脚本逐个重写 csproj 掩盖交付不足。具体引用选择方式在实现前以现有发布/锁图验证确定；若需要重新设计全局打包规则，退出快速通道。

P3 选择保留最小源码目录布局的文件复制清单：唯一应用/平台项目、项目契约、Branding、移动 native 源码及必要构建附件。新的交付根 props 显式设置 GalNetUsePackages；Sample/项目契约已有 csproj 用条件选择源码/包依赖，保持 XML 原样复制。交付根中央版本从原中央版本文件导入，并为四个 GalNet 包使用调用者提供的 GalNetPackageVersion。复制既有根构建 props/targets 和 Player 附件，保留严格契约路径规范化、R2R 例外与 apphost 接线，不重新发明构建系统。

复制命令只按清单复制文件并记录来源/哈希，不生成第二份应用实现、不替换命名空间、不重写工程 XML；仓库内开发默认仍使用源码项目引用。平台项目保留原相对资源/native 路径，避免再增加一套路径开关。交付验证实际构建 Desktop 外壳，不能仅编译共享应用库。

## 启动协议与视觉策略

Sample 的命令仍决定何时开始新游戏、继续或读档，以及前后执行什么项目业务。会话能力识别、准备后等待页面就绪再 Begin、取消及失败收束应只有一个维护位置，优先收敛到已有内置启动路径，避免为几个动作建立新路由框架。

`GameLaunchFlow` 的异步协调职责保留；350ms 属于项目展示策略。项目选择加载展示行为，内置只保证准备任务、呈现完成和启动的正确顺序。公告展示时长同理由项目显式提供，内置保留确认期间抑制、取消和清理。

保持默认外观时序。不能直接在 UI 线程阻塞等待、为了时长可配置暴露新的生命周期所有权，也不能让取消后的准备任务继续回写。若收敛启动或时长配置必须新增/修改公共接口，先转普通 feature 设计阶段，不能将此视为无需评审的实现细节。

## P1 接口决策与流程变更

2026-11-01 用户授权实现。收敛运行协议必须让 Sample 调用内置公开入口，故按 fast-track 规则转普通 feature 流程，不压缩公共接口设计。

- 在已有 GameLaunchFlow 增加 StartNewGameAsync（普通/带变量）、ContinueAsync、LoadAsync 便利方法。它们适配现有 IGameSessionService/IPreparedGameSessionService，再复用原 RunAsync。现有构造函数与 RunAsync 签名保留；不新增启动服务接口或路由层。参数化启动继续要求既有 IParameterizedGameSessionService，提前报告不支持。
- RunAsync 只等待项目 showLoading 回调和准备完成，不再固定最短展示时间。Sample 的 showLoading 回调在切页后异步等待项目定义的 350ms；准备工作仍同时开始，保持现有默认时序和取消收束。其他消费者可自行控制展示策略。
- PlayerSurface 提供可由 XAML/代码设置的 AudioAnnouncementDuration，采用 Avalonia StyledProperty，要求正时长。通用默认值为 3 秒；Sample 在组合时明确设置 2.8 秒，与项目入场动画匹配。公告取消、确认期间抑制与资源清理保持内置。
- 项目默认时长集中在 SampleCustomization，开发者改项目即可。不给 Sample 增加运行期配置框架，不把所有异步生命周期暴露给项目。

## 游戏布局创建

普通游戏的 scoped 页面身份和运行 surface 必须继续一致。重播沿用相同的项目布局选择，但创建独立的呈现状态和必要项目 VM，交由 GalleryScenePlayback 管理重播运行。

优先用项目内小型创建函数/现有映射能力完成，避免新的公开工厂体系。不能直接解析主游戏的 scoped state 给重播，也不能仅统一类名而继续绕过开发者配置的视图选择。自定义视图构造所需依赖与独立状态传入方式须在定向测试中体现。

P2 实现选择项目私有 CreateGameView：普通游戏通过 IPageViewFactory 解析映射的 scoped 控件，保留项目装饰页面工厂的扩展入口；重播通过 ActivatorUtilities 新建同一映射类型，项目私有服务视图只替换构造函数中的 GamePresentationState/GamePageViewModel，其他服务继续借用所属 scope。两者均显式设置 Presentation/DataContext。映射到基础 GamePage 时直接构造，避免 scoped 运行别名解析自身；不增加公开工厂 API。P3 兼容性复核补充了项目装饰工厂的回归测试。

## 取舍与风险

- 保留页面 VM 组合层，接受少量清晰的项目命令代码，不追求最少文件数。
- 保留配色程序集命名和现有平台外壳，避免把无关重命名混入行为收敛。
- 最敏感的风险是源/包构建图混用、契约字节变化，以及重播误共享状态。验证这些边界的优先级高于代码行数。
- Android 真机、既有 CI 失败和 Linux 音频瞬态异常继承前项记录，本条只对新增变化负责，不将旧项默认为通过或已修复。
