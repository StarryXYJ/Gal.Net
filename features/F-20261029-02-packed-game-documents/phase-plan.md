# 实施阶段

| 阶段 | 退出条件 | 状态 |
| --- | --- | --- |
| P1 | 文档 GPAK、导出、安装与旧布局兼容；空目录和校验/原子安装用例通过 | verified |
| P2 | 运行内容/内置语言与外置翻译/profile/CLI inspect，语言覆盖和完整性用例通过 | verified |
| P3 | 发行脚本读取新入口并保留空目录；实际组合包与两种部署启动验证 | verified-with-follow-up |
| P4 | 相关回归、格式、自检、正式文档与总结同步 | verified-with-follow-up |

证据目录 out/packed-documents/。构建/测试按依赖顺序运行，不重叠同输出根的构建和 no-build 测试。不上传，不自动提交本轮。

P1/P2 证据：isolated-build.log 完整 solution 构建成功；isolated-integration.log / tests/isolated-integration.trx 164 项通过；isolated-editor-shared.log / tests/isolated-editor-shared.trx 477 项通过。为隔离同时进行的对话历史与 Editor 改动，最终发行验收使用 HEAD 86cc021b 加本轮文件覆盖的独立源码快照 source/，范围与 SHA-256 见 source-files.json。当前工作区先前的构建失败日志保留，不视为本轮独立快照的结果。

P3/P4：release/0.1.0-dev.44 生成并验收七 ZIP；实际两种 GameTestCase 加载 2 passed；galpak-pwsh、galpak-winps 均 passed。native-comparison/results.json 中旧版、自包含新版、依赖框架新版均 ready=true，运行时位置符合部署。组合桌面 smoke 曾发生 Skia 字体合成 0xC0000005，未定位根因，不能宣称完整桌面 smoke 通过；证据及后续见 summary.md / review.md。修改后的验证脚本已在失败分支保留 stdout、stderr 与 profile 日志，不掩盖失败或自动重试。
