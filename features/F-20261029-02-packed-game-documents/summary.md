# 内置文档与本地化封装

日期：2026-10-29。状态：implemented-with-follow-up。核心实现、回归与发行载荷验证完成，两种玩家均有实际启动成功证据；组合桌面 smoke 的原生字体崩溃仍待跟进。上一轮已提交 86cc021b，本轮未自动提交或上传。

## 交付行为

- 新 galpak v3 固定为 pak/game.pak 入口，包含 content、installation、settings、Gallery、characters、layout 六个紧凑 JSON 文档；不分发内部散 JSON 或 .galnet。复用 GPAK v3 压缩和逐项校验，没有引入 BSON。
- localization.pak 为一个物理文件，每种文化独立文本条目；作者多文件按原 ordinal 路径次序合并。新内容 pak/I18n/<culture>/**/*.json 仅作为社区翻译的可选覆盖入口，支持部分覆盖、新增语言与无效来源诊断。默认语言完整性只依赖内置文本。
- 导出、普通解压、安装提供空 I18n、ResourcePatches、StoryPatches/<buildId>。纯 Sample 提供三个空扩展目录，没有游戏时不创建虚构 buildId。
- 安装仍使用暂存校验后发布；复用相同内容保留用户翻译和补丁，入口 game.pak 也参与复用比较。v1/v2 galpak 和 content v2 安装目录保留读取兼容；损坏新入口不会回退散文件。
- 只读 CLI game inspect 让发行脚本在 PowerShell 5.1/7 读取新旧描述，无需重复解析 GPAK。Sample 用户 profile 继续按 projectId 摘要，与安装位置和 buildId 无关。

## 实现位置

Assets 的 PackedGameDocuments 编解码固定文档集合；Editor.Shared 的 GamePackageExporter 准备文档和语言包；Storage.FileSystem 的 InstalledGameDescriptor、GalpakInstaller、InstalledPackageSet 和内容加载器负责定位、校验、安装和兼容。Sample 与发行脚本接入新入口。

内部安装索引不包含 game.pak 自身，避免循环哈希；使用固定导出时间使同一输入重复生成稳定文档包，实际发行时间保留在发行清单。新格式标记参与 buildId，存档/GPAK 版本不变。

## 验证

证据根为 out/packed-documents/。

| 检查 | 结果 |
| --- | --- |
| 完整 solution 构建 | isolated-build.log，0 errors |
| 导出/安装/运行内容集成 | tests/isolated-integration.trx，164 passed |
| 共享作者工具与真实 CLI | tests/isolated-editor-shared.trx，477 passed |
| Assets / Core / Storage / Architecture / GameHost | 分别 103 / 139 / 26 / 52 / 8 passed，相关 TRX 与 related-tests.json |
| 发布保护 | guards/guard-results.json，7 passed |
| 修改 C# 格式与 using | format-whitespace.log、format-imports.log，退出 0；git diff --check 通过 |
| 实际分发 CLI 的 galpak 整合 | galpak-pwsh/verification.json、galpak-winps/verification.json，PowerShell 7/5.1 均通过 |
| 两种部署完整 Apps | release/0.1.0-dev.44/release-index.json；release.log 与 .work/0.1.0-dev.44-af2f37c1f9724a388f53762f72419381/app-validation/app-validation.json，七 ZIP 载荷验收 passed |
| 实际两种 GameTestCase 内容加载 | tests/published-content.trx，2 passed |
| 实际玩家默认启动与运行时位置 | native-comparison/results.json：旧版、自包含新版、依赖框架新版均 ready=true，运行时位置正确，唯一 profile 已清理 |
| 组合桌面 smoke | 未全绿；保留 desktop-smoke.log、desktop-smoke-final.log、desktop-smoke-confirmed.log 和各进程日志，见下文 |

工作区有同时进行的对话历史和 Editor 改动，首轮工作区构建曾因这些文件的临时编译错误失败；原日志保留。为提供稳定证据，最终使用 HEAD 86cc021b 加本轮明确文件列表的源码快照构建与打包；source-files.json 记录覆盖路径和 SHA-256，source/ 保存可复现输入。该快照不包含其他任务的未提交代码，不声称当前整个脏工作区已通过完整 CI。

组合桌面 smoke 中发生过 Skia 字体合成的原生 0xC0000005，失败堆栈位于 desktop-smoke-confirmed/SelfContained/player-stderr.log；同一发行玩家随后在 native-comparison 中成功初始化，旧版对照也成功，根因尚未确认，不能据此断言为已有问题或已修复。另一次 CLI 导出暂存目录访问拒绝在同路径重试成功。验证脚本已增强 stdout/stderr 和 profile 日志保留，失败仍返回非零，不自动吞错或重试。正式发布前需要单独定位原生字体风险；本轮没有通过改字体/延时/强制跳过启动步骤来制造通过结果。

## 文档与边界

已同步 [文件格式](../../docs/spec/file-formats.md)、[国际化](../../docs/spec/i18n.md)、[架构](../../docs/spec/architecture.md)、[内容分包](../../docs/manual/content-packaging.md)、[发行打包](../../docs/manual/release-packaging.md)、[Sample 定制](../../docs/manual/customize-avalonia-sample.md)、glossary 和 Headless README。

本轮没有体积专项优化、加密、防提取、BSON、AOT、热加载或通用更新协议。保留整包缓冲；按语言条目组织不代表流式按需 I/O。Windows x64 是发行验收平台，未运行其他平台发行、完整剧情/全部音视频格式或完整 CI/NuGet 消费验收。发现并修复的启动异常继承边界记录在 .agents/lessons/L-20261029-08-invalid-data-startup-fallback.md。
