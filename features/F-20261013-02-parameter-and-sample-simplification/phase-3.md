# Phase 3：Sample 组合与服务整理

状态：verified，2026-10-14。当前无阻断项；下一阶段为 Phase 4。整个 feature 仍在进行中。

## 实现与收益

平台项目新增一个 SampleCustomization 文件，集中主题、本地化 bootstrap、可选调试、服务组合、字段工厂、内容/Gallery catalog、decoder、纹理预加载选择、effect-program 选择和执行 module 注册。App 保留应用/关闭编排，ResourceScope 保留加载及租约释放，Presentation 保留平台对象创建和所有权；这三处不再维护项目业务选择。MainWindow 和 settings 的 registry 也消费同一入口。

ProjectSchemas.CreateCatalog 从唯一 Modules 集合构造 catalog，Avalonia 和 Headless 共用该入口；工具仍独立加载 Modules，不增加动态加载、第二份 schema 或全内置 fallback。截图已在 ConfigureExecution 注册，业务实现仍在 SampleScreenshotModule；新增类似业务只需契约声明、平台业务实现与集中注册，不需修改 App、Session、Runner 或关闭流程。注册的 module 由 view 拥有，借用的平台对象仍在 view 后释放。

Session 原来持有仅供初始化的 resource/gallery catalog，现在由资源作用域按组合策略局部构造，删除两个字段及其传递；Runner 原来重复持有 Presentation.Restorer，现在直接消费唯一所有者，删除一个字段。未新增生产项目、接口、选项容器、服务定位器或通用 Runner。

10 个生产 C# 文件（含新增入口）净增 34 行：已有文件增加 16、删除 75，集中入口 93 行。测试新增 89 行。收益是减少稳定生命周期文件中的项目修改点及重复持有，未声称总代码量下降。契约与平台仍分别维护明确职责。

## Service 候选处理

| 候选 | 本阶段结论 |
|---|---|
| App 主题/本地化/调试注册 | 移入集中入口；保留 startup/关闭机制 |
| ResourceScope decoder/preload、Session catalogs | 选择移入集中入口；scope 持有租约，Session 删除初始化字段 |
| Presentation schema/执行注册 | 移入集中入口；平台资源创建和释放仍由 Presentation 承担 |
| Runner 重复 restorer 引用 | 删除，使用 Presentation 的唯一属性 |
| Session / Runner | 保留跨运行的应用会话与单次 Engine 两种生命周期 |
| PlayerSettings / Startup | 保留设置应用/持久化与首次启动语言/计数策略，不机械合并；registry 选择已集中 |
| Localization / LibVlcRuntime | 保留 observable string/provider 与应用媒体后端的独立所有权；bootstrap 选择已集中 |
| WindowDisplay / Restarter | 保留原生窗口与进程边界 |
| SaveSession / Gallery resolver | 保留 profile 与 Gallery 租约所有权；资源 materializer 已共享，移动 resolver 只会增加传递 |
| 默认 layer adapter | 保留预加载借用及 fallback 所有权，与 Gallery 的独立 acquire 语义不同 |
| Debug actions / Session 调试存档 | 保留 debug UI/重启策略与会话锁/runner/saves 协调；增加转发接口或 partial 拆分没有净收益 |
| Session 清理短路 / 截图异步样板 | 留在计划已有 Phase 4b；本阶段没有夹带错误策略修正 |
| Headless 组合 | 共用契约 catalog；资源类型选择保留在其小型组合根，不为两行配置新增共享宿主项目 |

## 验证

| 范围 | 通过 |
|---|---:|
| Sample 组合、真实截图、启动、本地化、显示设置、存档、调试 | 40 |
| 项目契约隔离/部分选择、CLI help | 21 |
| Architecture | 36 |
| 合计 | 97 |

TRX 位于 Temp/F-20261013-02/phase3-{integration,contract-tools,architecture}.trx。新增组合用例验证 DI scope 共享/隔离、每 scope 独立 registry、调试开关、本地化 dispose 清理，编译截图经集中执行入口注册并 CatchUp 完成；先释放一个 view 不影响另一个，缺实现仍明确失败。既有截图用例验证真实 PNG、Normal/Quick、取消、失败和释放。项目 scope 测试继续覆盖相同程序集名下不同/部分 schema 不串用与无 fallback。

独立契约部署脚本成功刷新 GameTestCase/Contracts；Headless 通过静态 ProjectReference 构建，未增加契约 DLL 动态加载。CLI 独立构建后 schema show sample.screenshot 成功，build GameTestCase 成功编译 4 个 Group、diagnostics=[]；JSON 证据位于同目录 phase3-schema.json、phase3-build.json。未进行完整 Headless 剧情或原生媒体人工播放验证，也未运行全解决方案测试。

构建禁用 shared compilation 和 Avalonia telemetry。沙箱内 testhost 90 秒连接超时，同一已编译产物在沙箱外 40 项通过；Roslyn format 的 named pipe 同样需沙箱外执行。第一次构建暴露移动代码遗漏 Core.Scene using；测试夹具补齐序列化/Entry using、必需 Graph 和 GameRuntime 参数后通过，未修改产品协议。CLI 首次误将 operationId project.build 当作 commandPath，help --json 确认命令为 build 后通过。改动文件 whitespace / IDE0005 检查完成。

正式接入步骤与所有权同步至 docs/development.md 和 Sample.Contracts/README.md。Phase 4 应以已有 DTO/For<T> 为起点验证普通同步、自定义语义/Composite 的作者成本；Phase 4b 继续处理异步/清理，不重复建立平行参数基础。
