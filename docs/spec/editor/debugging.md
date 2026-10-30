# 外部调试状态与缓存契约

## 状态与缓存语义

定位沿显式边路线无条件追帧，保留当前 Player/Save 值，跳过声明为 Ignore 的变量赋值、Gallery 解锁和其他持久副作用。它生成测试场景，不恢复真实玩家历史。普通 `SkipToGroupEndAsync` 默认仍执行原条件和逻辑写入，和调试定位有不同用途。

每 Group 最多一份最近自然入口的纯场景；向后恢复入口再重放，向前先收束当前活动呈现再重放余下内容。调试追帧不覆盖自然入口。默认 LRU 上限为 256 个 Group、64 MiB 序列化 payload，可通过 `GameDebugOptions` 调整或设为 0；单独保留初始运行场景。该预算不表示精确托管堆占用。普通读档清缓存并推进执行代次。热点前缀和固定场景预计算未启用。

Player 修改先同步当前 Runtime 与玩家变量服务，默认 Avalonia 宿主在停止/释放运行时刷新到隔离 profile；Save 修改保留在当前运行，普通保存仍由游戏触发。面板不把“已同步”解释为“已写存档槽”。

当前协议校验版本、随机会话邀请、项目身份和构建内容指纹；请求按 runId/执行代次拒绝过期运行，变量还检查 UID/类型/原值。内容指纹覆盖 Graph、I18n、settings 及存在的 Contracts 目录；另可在 DebugIdentity 中提供 ContractFingerprint。Editor 使用项目已加载契约 DLL 的 SHA256，并通过 `--debug-contract` 传入预期值；Sample 对自己实际加载的 `ProjectSchemas` 程序集计算 SHA256，不匹配即拒绝。自定义 App 必须同样提供实际契约身份，不应直接信任命令行中的预期值。契约指纹不等价于执行模块程序集的代码指纹。

CLI/Editor/App 应使用同一份项目契约构建产物，包括默认只含内置条目的工程；选择 Sample 时需先部署 Sample 对应的 Contracts。运行内容来自构建目录，不提供资源热更新；作者内容编辑后旧源码映射失效，需要重新启动测试。

在仓库根目录运行 `./scripts/contracts/export-sample-contracts.ps1`（可选 `-Configuration Release`），会先构建 Avalonia Sample，再将配套契约及依赖 DLL 导出到根目录 `out/Contracts`。该输出目录已被 Git 忽略。关闭 Editor 项目，将导出的 DLL 复制到项目 `Contracts` 目录，重新打开项目，并使用脚本打印的对应 App 路径启动测试。

仓库对项目契约及其 Core/Builtins 编译引用禁用提交相关的 InformationalVersion 后缀和 SourceLink，以免仅提交无关文件就改变 DLL 身份；PDB 和本地调试仍保留，App 自身仍带提交信息。可用 `./scripts/contracts/test-contract-reproducibility.ps1` 对两个 SourceRevisionId 做真实 Rebuild 检查。首次迁移此构建规则需重导出并部署一次旧契约；源码、引用 API、生成器、SDK、路径或编译配置变化仍可能改变严格二进制身份。仓库外自定义契约及依赖也需自行保证构建稳定，不能只依靠相同程序集版本号。

Editor 启动的 App 在 30 秒内未连接时，调试面板会报告连接超时；Editor 不会因此终止 App。调试请求 60 秒无响应时，面板会结束等待并提示操作可能仍在运行；先检查运行状态再重试。状态/检查轮询的单次请求上限为 10 秒。

每次本地测试会话会在测试 profile 的 `logs/` 下生成 `debug-launch-*.log`，记录项目保存/构建/作者映射耗时、进程 ID、连接和调试请求结果、退出码及 App 的 stdout/stderr；创建时会把完整文件路径写入内置 Log Panel 的 Game 频道及 Editor Console。App 输出也会转发到 Editor 的内置 Log Panel（Game 频道；stderr 显示为 Error）和 Editor 的 Serilog Console sink，因此从命令行启动 Editor 时也能在命令行看到。单份诊断日志最多 1 MiB，最多保留 20 份；启动元数据不记录调试 token 或配置参数值，stdout/stderr 中的调试 token 会被遮盖。错误信息中会附诊断文件路径。Sample 的运行日志仍写入同目录的 `game-YYYYMMDD.log`。反馈问题时请一并提供这两类日志；启动前的契约校验错误可能只出现在 `debug-launch` 日志，因为它发生在 Game 日志初始化之前。

Sample 的命令行/契约检查失败会输出异常并以非零码退出。断开调试只断开控制通道，不终止外部 App；Editor 会继续排空其标准输出/错误并记录到本次诊断日志，直到 App 退出。IDE 自行启动的进程仍由 IDE 管理。

当前状态同步为 150ms 全量轮询，有帧大小、并发请求和路线搜索上限；流程暂停不完整冻结媒体时钟。Group/Entry 定位、变量修改和生命周期已有真实 Sample 进程及挂载 UI 测试，视频/音频/效果的全部组合仍需项目自行验收。
