# 实施阶段

用户于 2026-10-27 授权实现，并要求本地优先、先验证 CI；可用 GitHub CLI 读取远端失败证据。

| Phase | 范围与退出条件 | 状态 |
| --- | --- | --- |
| P1 | 读取最近 CI 失败日志；将 restore/build/test/format 形成仓库本地入口，Actions 复用；本地实际运行，保留失败或通过证据 | verified：15 项目，2011 passed / 0 failed，含 coverage |
| P2 | 5 个聚合 NuGet 包、外部消费验证、R2R/Contracts 本地发布入口、版本与产物索引；实际生成验收 | in-progress |
| P3 | 桌面 Sample AOT 发布与运行验证；不将 Headless 证据当作桌面支持 | in-progress：严格试编译失败，未开放 AOT 发行 |
| P4 | tag workflow 调用同一入口、上传已验证产物的独立脚本、文档和收尾；无明确发布请求不上传公共 NuGet 或创建版本 tag | in-progress |

验证按阶段顺序运行，避免构建输出竞争。失败不能通过跳过用例或放宽校验掩盖。跨平台后端 job 仍需对应系统执行，本地 Windows 不能证明所有 runner 都通过。

P1 证据：`out/ci/local-pass/ci-results.json` 整体 passed=true；15 份 TRX 合计 2011 passed / 0 failed，含 coverage。6 项发布保护负例独立通过；最终保护检查接线/格式验证见 `out/ci/format-with-release-guards`。Headless AOT 自检输出 AOT_ACCEPTANCE_OK dynamicCode=False reflectionJson=False。

五包初步外部消费验证通过，最终版本打包仍需本地完整入口验收。详见 ci-investigation.md。
