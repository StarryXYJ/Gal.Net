# CI 优化跟进（2026-10-29）

用户最终确认：不分块、不选测；PR 用一个 Windows job 跑全部普通测试，master 普通更新不重复测试，版本 tag 进入 master 后验证并发布；tag 和合并顺序不限。保留精确 tag、master 祖先归属、包消费与载荷验证。

实现切片：

1. 从主 solution 生成桌面 solution filter，排除尚未接入的 CrossPlatform 模板工作负载；PR 完整构建后串行运行全部普通测试。默认关闭 coverage，加 NuGet 缓存、阶段耗时。后端三平台与 AOT 矩阵仅保留手动运行。退出条件：过滤器、构建与完整测试有实际验证证据。
2. Release 在 tag push 与 master push 时发现候选；tag 未合并时等待，master 更新扫描新进入历史的版本 tag（含源分支提交）。按 tag 串行并跳过已公开版本，checkout 精确 tag，全量验证后才发布。退出条件：真实 Git 图覆盖两种顺序、历史 tag、普通 merge、多 tag、无 tag、非法 tag。
3. 测试和正式版本构建保持分开，打包只构建 NuGet 所需的 19 个库项目，不重复构建测试。统一文档并记录验证证据。不同 RID/部署还原图仍独立。

Squash/rebase 不保留源提交身份，合并前的 tag 不会自动移动；该方式应给最终 master 提交打 tag。CI 不改写 tag。

当前状态：配置实现、脚本检查和最小库打包验证完成；最终全量 CI 尚未通过，见下列证据。远端 Actions 与公开发布尚未验证，本轮未创建 tag、推送或发布。

验证期间发现共享工作区的生成器 DLL 被后续构建改回默认版本，直接复用普通测试输出时，打包的版本检查正确拒绝该 DLL。因此不引入测试版本注入、跳过库构建开关或 CI 结果缓存；正式打包仍独立执行带版本的最小库构建。

验证记录：

- 四份 workflow 的 YAML、触发条件、PR 单 job、复用 workflow 路径与内嵌 PowerShell 语法检查通过；源脚本语法、文档链接及 diff whitespace 检查通过。
- `out/ci/optimization/final-run/` 中十七项原发布保护检查与十九项新发现/过滤器检查通过，真实 Git 图包含合并前 tag、合并后 tag、普通 merge、多 tag、历史 tag 排除、手动候选与非法版本；发布状态查询使用 mock，无外部调用。
- 首轮桌面过滤器构建 0 错误，15 项目完整测试执行完毕，2135 passed / 6 failed。两项为既有 Editor 文本白名单、复选框布局检查失败；另四项出现试验版本与默认 fixture 的契约身份冲突。版本注入与输出复用试验已全部撤销，不能把该轮视为最终配置通过。
- 最终默认配置重跑在 locked restore 阶段正确失败：工作区新增 `GalNet.Editor.Shared -> GalNet.Storage.FileSystem` 引用，而 `test/GalNet.IntegrationTests/packages.lock.NoGameDebugging.json` 尚未同步，返回 NU1004。未改写另一项进行中业务调整的依赖图或锁文件。最终证据为 `out/ci/optimization/final-run/ci-results.json`，passed=false。
- `Pack-NuGet.ps1 -Version 1.0.0-dev.100` 实际以 19 项目的库过滤器完成 locked restore/build，0 错误，生成全部 5 个 nupkg，见 `out/ci/optimization/library-pack-pass/`。本地使用 `AVALONIA_TELEMETRY_OPTOUT=1`，与 CI 环境一致；首次独立调用未设置此变量时遇到沙箱外 Avalonia telemetry 日志权限错误，设置后通过。未执行本轮应用全量打包、外部包消费者或公开上传。

运行时间的优化来自取消 master 普通合并的重复测试、默认关闭 coverage、NuGet 缓存、后端/AOT 矩阵改为手动、NuGet 版本构建排除测试项目、ZIP artifact 不再重复压缩。未取得远端前后对照数据，不宣称具体分钟数或百分比收益。
