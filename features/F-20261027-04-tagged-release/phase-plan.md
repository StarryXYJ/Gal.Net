# 实施阶段

用户于 2026-10-27 授权实现，并要求本地优先、先验证 CI；可用 GitHub CLI 读取远端失败证据。

| Phase | 范围与退出条件 | 状态 |
| --- | --- | --- |
| P1 | 读取最近 CI 失败日志；将 restore/build/test/format 形成仓库本地入口，Actions 复用；本地实际运行，保留失败或通过证据 | verified：15 项目，2011 passed / 0 failed，含 coverage |
| P2 | 5 个聚合 NuGet 包、外部消费验证、R2R/Contracts 本地发布入口、版本与产物索引；实际生成验收 | verified：0.1.0-dev.1 完整本地发行和桌面初始化 smoke 通过 |
| P3 | 桌面 Sample AOT 发布与运行验证；不将 Headless 证据当作桌面支持 | in-progress：严格试编译失败，未开放 AOT 发行 |
| P4 | tag workflow 调用同一入口、上传已验证产物的独立脚本、文档和收尾；无明确发布请求不上传公共 NuGet 或创建版本 tag | 本地实现与保护用例 verified；远端执行/公开上传尚未验证 |

验证按阶段顺序运行，避免构建输出竞争。失败不能通过跳过用例或放宽校验掩盖。跨平台后端 job 仍需对应系统执行，本地 Windows 不能证明所有 runner 都通过。

P1 证据：`out/ci/local-pass/ci-results.json` 整体 passed=true；15 份 TRX 合计 2011 passed / 0 failed，含 coverage。6 项发布保护负例独立通过；最终保护检查接线/格式验证见 `out/ci/format-with-release-guards`。Headless AOT 自检输出 AOT_ACCEPTANCE_OK dynamicCode=False reflectionJson=False。

P2：`out/release/0.1.0-dev.1/release-index.json` 包含 5 个 NuGet 与 4 个 ZIP、完整文件哈希。独立消费工程编译/运行、所有 GalNet 程序集公开类型加载、源生成器、native DLL 加载通过；ZIP 解压/hash/R2R 标记/契约身份/CLI help/Skill 文件验证通过。`out/release-smoke/dev1/app-validation.json` 为 passed=true、desktopStartup=true，日志确认 11 个 sprite 和 15 个 effect program 资产发现，Session initialized。

P3：真实桌面 NativeAOT 严格链接失败，见 ci-investigation.md；保留诊断脚本和明确拒绝 AOT/Both 的发布门禁。不能标记本 feature 全部完成。

2026-10-28 P2 跟进 verified：用户要求核实体积及整合内容形态。过滤全部应用 PDB 和非 x64 VLC 内容；发布验收检查仅保留所需架构，并要求整合内容为非空 PAK / 编译剧情而非原始作者文件。`Release.ps1 -Version 0.1.0-dev.2 -Target Apps -OutputRoot out/release-size-check`、独立解压 DesktopSmoke（`out/release-size-smoke/dev2/app-validation.json`）及 7 项保护检查均通过。ZIP 体积对比见 summary.md。
