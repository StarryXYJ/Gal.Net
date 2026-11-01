# 实现与验证记录

## P1：启动协议与视觉策略

状态：verified，2026-11-01。用户授权实现后，因需要公开便利入口和属性，按已约定边界转普通 feature 流程；没有新增接口体系或扩大到拆包。

GameLaunchFlow 增加新游戏、继续、读档及带变量启动的便利方法，集中适配普通/两阶段会话；原构造及 RunAsync 保留。项目 VM 保留命令与业务、导航，移除重复 Prepare/Begin 判断。RunAsync 不再固定最短展示时间，明确检查取消后禁止切入游戏/Begin，并继续等待准备清理。

SampleCustomization 定义 350ms 加载停留与 2.8 秒公告；加载回调异步等待，负值明确拒绝。PlayerSurface 新增正时长 StyledProperty，通用默认 3 秒，Sample 显式覆盖；确认、输入、焦点和公告清理机制保留。

证据根 `.artifacts/sample-template-polish/p1/`：

- presentation.trx：234 total / 228 executed / 228 passed / 0 failed，6 个显式用例未执行。新增 7 种会话动作/能力组合、参数化不支持提前失败、准备忽略取消令牌时仍不开始播放；原顺序和取消收束回归继续通过。
- integration.trx：335 total / 323 executed / 322 passed / 1 failed，12 项显式未执行。唯一失败为前项已记录的 Editor 保存后 dirty/关闭取消，不作为本 feature 已修复或新增失败。
- targeted.trx：最终页面/宿主/扩展定向 39/39，包括实际 Toast 使用 50ms 项目时长提前关闭、非正时长拒绝、项目页启动及确认/焦点/输入恢复。
- architecture.trx：56/56。docs check 72 页及本地链接通过，diff 空白检查通过。首次新增测试误用了无参 AssertionException 导致编译失败，已修正并完成上述重跑，初始日志保留。

工作区有并行开场序列/启动可用性改动，涉及同一 TitlePageViewModel 与 SampleCustomization。只提交本 feature 的启动协议/时长改动，保留其余工作树内容；不将并行改动或旧 review、用户 lesson 混入阶段提交。

## P2：同一布局来源与独立重播状态

状态：verified，2026-11-01。Sample 的普通游戏和重播均通过项目私有 CreateGameView 读取实际页面映射。普通控件保持 scoped 实例；重播新建映射类型，构造时获得独立 state/VM，其他服务借用当前 scope。移除固定 Views.GamePage 注册和重播直接 new 该类型的路径。映射基础 GamePage 的别名递归也有回归保护。

`.artifacts/sample-template-polish/p2/`：layout.trx 13/13；lifecycle.trx 4/4，覆盖取消结果不得回写与释放等待者；scenes-explicit.trx 4/4，两个真实内容场景 × 默认/替换布局，检查关闭、不可保存/读档、玩家进度/解锁不受污染，并保存四张独立 PNG。已目视检查自定义场景布局的渲染。

中间 ResourceGallery 测试参数修改曾误匹配另一个 TestCase 导致编译错误，已修正。scenes.trx 混选普通类与 Explicit 方法时仅执行 13 个普通用例，未将它当作原生通过证据；随后单独选择场景方法取得上面真正执行的四项结果。

## P3：独立交付、依赖清理与兼容性收尾

状态：verified（本 feature 范围内），2026-11-01。新增 Template/files.json 与复制入口 scripts/samples/export-template.py；输出保留最小源码层级和必要 MSBuild/native/Branding 附件，GalNetUsePackages 切换同一工程的源码/包引用，调用者明确提供 GalNetPackageVersion。不重写工程 XML，不复制引擎 C# 源码，不生成第二份应用 UI。版本缺失时有明确错误。

移除未使用的 LibVLCSharp.Avalonia 引用、中央版本及锁节点，保留实际使用的 LibVLCSharp；Markdown.Avalonia 仅移除未使用的中央版本。标准包消费者增加无 VideoView 集成泄漏门禁。复核 P2 后，普通游戏继续调用 IPageViewFactory，兼容项目装饰工厂；重播仍按同一映射新建独立状态。

证据根 `.artifacts/sample-template-polish/p3/`：

- packages：同版本本地候选 0.1.0-dev.101 的六包；package-validation/package-validation.json 六个真实消费者通过。未上传包。
- consumer-final/external-template.trx：15/15，覆盖局部纯自定义按钮、命令、新页面、配色、实际工具栏点击、普通/重播布局与工厂装饰。external-template.json 确认复制的工程 XML 哈希未变，实际项目依赖仅 GalNet.Sample.CrossPlatform 和自有 GalNet.Project.Contracts。149 文件的 manifest 标记源工作树有修改；包含并行开场工作快照，不冒称干净提交构建。
- index-validation/external-template.trx：另从暂存内容重建最小输入源，仅复制属于本 feature 的已暂存源码，排除并行开场/未暂存资源。仓库外交付 140 文件，restore 和定制测试 15/15；工程 XML 哈希不变、项目闭包检查通过。先前把该额外消费者放在仓库内被探针正确拒绝，随后改用真正的外部目录。此项确认提交本身不依赖并行工作区改动；manifest 仍如实标记源仓库存在工作树修改。
- targeted/source-layout-final.trx：最终源码 14/14；native-media-final.trx：8/8 显式实际解码/场景用例；media-cancellation-final.trx：取消释放 1/1。删除 UI 包后两音频、两视频及四场景仍通过。
- runtime-final-isolated/runtime-validation.json：真实 Windows HWND 存读档、改名、重开、暂停/重挂载、取消/确认退出和 PNG 预览通过。Player/Developer 自包含 publish、各自 locked restore、三个契约字节一致、无 LibVLCSharp.Avalonia.dll 和可选 Avalonia 调试 UI 隔离通过；published-processes.trx 两个实际 EXE 启动/正常退出 2/2，PATH 仅系统目录，不依赖 SDK 发现。
- locks/results.json：22 种配置各 force/locked，共 44 次成功；历史 SDL 宿主探针也 force/locked 通过。覆盖默认/玩家、Desktop 四 RID 和调试开关、移动/Browser 图、R2R。最终默认 Debug 图再 force/locked，恢复 Editor 诊断依赖，避免 Release 图清理带入无关变化。audit.json：59 个锁文件，共有包 resolved/contentHash/type 与框架键保持；部分旧项目版本边规范化至 1.0.0，历史探针补齐现有 DI 边，无依赖版本升级。src/test/历史探针无残余 VideoView 包节点。
- ci/ci-results.json：Release build 0 错误；TRX 合计 2586 total / 2563 executed / 2560 passed / 3 failed，23 未执行。3 个失败均已在前项记录：Editor 字面文案检查、生成 checkbox 居中、Sample 内容编辑保存后 dirty/关闭取消。整套 CI 仍非绿色；未在本 feature 修复或掩盖。CI 编译早于工厂兼容性最后修正，因此最终修正另有上述源码/消费者定向和真实发布验证。
- 文档检查：72 页、导航/本地链接及仓库引用校验通过；diff 空白检查通过。

### 环境失败与未验边界

最初进程探针以 File.ReadAllText 读正在写入的日志，因共享句柄规则失败；改为 FileShare.ReadWrite|Delete，并给每次运行新 profile 后通过，初始失败 TRX/log 保留。原模板 allowlist 曾误把项目文件名当程序集 ID，修为 GalNet.Project.Contracts，未将前次测试通过直接当完整脚本成功。

续跑时全局 NuGet 缓存 Avalonia 文件已缺失，force 还原又因占用的构建 DLL 拒绝解压。改用任务独立缓存后最终源码/原生/发布验证通过；切换缓存后测试探针必须重新 restore，已修正 verify-runtime.py，保留失败日志。

独立缓存的额外默认图重建发现本机 SDK library-packs 中 Microsoft.NET.Sdk.WebAssembly.Pack 10.0.12 的 contentHash 与原锁不同，官方源探针也出现 NU1403；不把这种工具链产物差异改成依赖更新。恢复 Browser 原已跟踪 hash，仅保留本轮 UI 包删除；因此不能将额外缓存切换后的 locked 成功当作该保留 hash 的新 Browser 证明。最初 44 次使用原环境的锁图证据保留，Browser 本来不在应用支持验收内，后续专门核对 workload 包来源。

WSL Ubuntu 24.04.3/WSLg 仍存在，但先前私有 dotnet SDK、媒体验证目录已不在，未新增本轮 Linux 原生执行证据。Windows cross-target restore 不是 Linux 原生运行验证。Android 真机按用户决定延期；macOS/iOS、Browser 和真实手柄亦未补验。默认后端包 native 仍限 win-x64，不声称此 feature 扩大平台发行支持。
