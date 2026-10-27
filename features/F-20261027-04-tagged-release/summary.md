# 本地 CI 与 R2R 发布实现总结

日期：2026-10-28。范围：已完成本地 CI、五个聚合 NuGet、Windows x64 R2R 与 Contracts ZIP；未完成桌面 AOT，没有公开上传或远端验证。

## 实现结果

- `scripts/ci/Invoke-CI.ps1` 是本地与 Actions 共用入口，固定 SDK、Configuration 和 locked restore；构建完成后逐项目 no-build 测试，支持 coverage，输出 JSON/TRX/原始日志。
- CI 已纳入全部 15 个 GalNet 测试项目；所有契约 fixture 加入 solution，避免 Release 测试读取 Debug 构建之外的旧 DLL。修正既有格式/using、音频源生成 JSON 默认值、旧 UI 测试入口、Headless 选择器交互和 GameTestCase 过时参数。
- `packaging/packages.json` 明确 19 个库项目归属；按 Contracts / Engine / Desktop / Avalonia / SchemaGenerator 发布 5 个聚合包，每个 DLL 只有一个 owner，保留原程序集与命名空间。没有 Editor SDK 包。
- `Release.ps1` 统一版本、构建、NuGet pack、仓库外消费测试、R2R 应用/内容组装、ZIP 验收及最终索引；只有全流程成功才暴露版本目录。
- Editor 附独立、自包含的 Headless CLI 与项目创作 Skill。纯 Sample 与整合 Sample 复用相同玩家，整合内容直接展开；Contracts ZIP 从实际玩家复制三份匹配 IL DLL，不重新构建另一份契约。
- `Push.ps1` 只上传索引列出的文件，检查哈希/版本/干净 tag/消费验收；使用 draft、依赖顺序、逐文件 journal，不重新编译或静默跳过重复版本。当前只验证了不触达外部命令的负例，没有实际推包。
- Release workflow 调用相同 CI/本地发布入口；workflow_dispatch 仅生成候选，tag 才进入公开上传 job。NuGet 认证由 NUGET_API_KEY 环境变量提供。

## 本地证据

| 检查 | 结果与位置 |
| --- | --- |
| 完整 CI + coverage | `out/ci/local-pass/ci-results.json` passed=true；15 项目，2011 passed / 0 failed |
| 发布保护 | `out/ci/release-guards-final/guard-results.json`，7 项通过（含 tag commit 校验），无构建/上传命令 |
| 最终 guard 接线与格式 | `out/ci/format-with-release-guards/ci-results.json` passed=true |
| Headless NativeAOT 回归 | `out/tagged-release-headless-aot.log`：AOT_ACCEPTANCE_OK dynamicCode=False reflectionJson=False |
| 完整本地发行 | `out/release-local-acceptance.log`，Release.ps1 退出 0 |
| 五包/四 ZIP 与哈希 | `out/release/0.1.0-dev.1/release-index.json`、SHA256SUMS |
| 外部包消费 | `out/release/.work/0.1.0-dev.1-f62990397b54464d8d6cf72cad4b2ceb/validation/package-validation.json` passed=true |
| ZIP 解压、R2R、契约、分发 CLI | 同 work 根的 `app-validation/app-validation.json` passed=true |
| 真实桌面 Sample 初始化 | `out/release-smoke/dev1/app-validation.json` passed=true、desktopStartup=true；独立 profile 日志含 Session initialized |
| Workflow YAML | 三份 YAML 使用 PyYAML 解析成功；尚未在 GitHub 执行新 workflow |

ZIP 大小约：Editor 261.43 MiB，纯 Sample 191.81 MiB，整合 Sample 222.17 MiB，Contracts 0.34 MiB。五个 NuGet 合计约 2.4 MiB。

### 2026-10-28 发行体积跟进

真实载荷发现 VLC NuGet 带了约 178.4 MiB 的 win-x86 / win-arm64，Editor 还有约 102.8 MiB PDB。现在在生成 manifest 前过滤全部 PDB 及上述非目标架构文件，保留 x64 核心与插件。Editor 的独立自包含 CLI 暂时仍保留运行时副本。

| 应用 ZIP | 首次候选 MiB | 精简后 MiB |
| --- | --- | --- |
| Editor | 261.43 | 149.66 |
| Sample | 191.81 | 107.35 |
| Sample-GameTestCase | 222.17 | 137.70 |

`out/release-size-check/0.1.0-dev.2` 为本次 Apps 候选；未重复构建 NuGet。所有应用 PDB=0，Sample 的 DLL 数量由 1214 降到 577。四个 ZIP 的解压/hash/契约/R2R/CLI/架构及内容检查通过；`out/release-size-smoke/dev2/app-validation.json` 为 passed=true、desktopStartup=true；7 项发布保护用例再次通过。第一次验证因用户同时清理输出目录而中断，换独立输出根重跑后成功。

整合 GameTestCase 原本已通过 export-directory 编译，本次进一步锁定产物形态：资源 `Assets/Paks/000-base.pak` 约 30.4 MiB、剧情 `.galgroup`，保留运行时 Graph/配置/本地化 JSON；发布验证拒绝散装作者素材、rawgalgroup 和 Contracts 作者目录。不能将这些运行时 JSON 当作“未编译素材”删除。

本地候选从 CI 修复 commit `88c12e7b` 及当时未提交的发布脚本构建，索引如实标记 dirty=true，没有 tag，因此不能通过公开上传门禁。它是可运行开发候选，不冒充正式 tag 发行。SDK/第三方源审计遇到网络问题时仍有 NU1900 警告；构建和测试的通过不代表零警告。

## 未完成与实际限制

1. **桌面 Sample AOT 未完成。** 首次真实 ILC 严格链接约 60 条诊断，涉及 JSON、反射绑定、主题资源、DI 保留和 Serilog/Ursa 裁剪。没有全局吞警告，也没有伪造 AOT ZIP。`Test-DesktopAot.ps1` 可重现；Release 的 Aot/Both 明确失败。Headless 通过不能替代桌面验收。
2. **远端尚未验证。** 已读取旧失败 job 的真实堆栈，当前本地已通过；未推送或重跑 GitHub Actions，不能宣称远端已绿。公开上传、凭据配置及跨服务中断恢复还需实际 preview 发布验证。
3. **桌面 smoke 范围有限。** 验证解压后玩家加载整合内容并初始化，不代表完整剧情、全部音视频设备、存读档和正常退出均已完成原生验收；测试只终止自己启动的进程。
4. 首期仅 win-x64；ARM64、其他系统和移动端未纳入发行。CLI 自包含分发已验收，但 Editor 原有“导出游戏”界面的仓库脚本依赖仍保留，未宣称 GUI 已成为脱离源码的完整构建工具链。

## 与目标设计的接口调整

- 首期 Target 为 All / NuGet / Apps；Contracts 随 Apps 或 All 生成，从实际玩家复制，未增加独立 Contracts 构建目标。
- Version 通过 SDK 统一写入程序集元数据，AssemblyVersion 采用 SDK 的 major.minor.patch.0 默认策略，未额外固定到 major.minor；原有契约稳定 Git 元数据及 R2R 排除规则保留。
- 本地执行的是 Actions 共用的真实检查脚本，不模拟 checkout、权限、GitHub artifact 服务或 NuGet 上传端点。后者必须在远端或实际预发布时验证。

## 文档与经验

- 当前使用说明：`docs/manual/release-packaging.md`、`scripts/README.md`。
- 过程与远端证据：`ci-investigation.md`。
- lessons：solution fixture 配置与 init-only 源生成 JSON 默认值，已索引到 agent-knowledge。
- CI 阶段提交：`88c12e7b`。发行阶段提交在验收后独立创建。
