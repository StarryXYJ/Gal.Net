# 桌面发行目录与统一 pak 内容根

日期：2026-10-29。状态：实现与范围内验证完成；仅本地候选，没有上传或创建 tag。

## 实际行为

- Windows 应用根只保留入口 EXE 与目录。SDK 原生 apphost 指向 lib/<app>.dll，托管库、运行时配置、.NET 自包含运行时及原生依赖都在 lib；说明、许可与发行清单在 docs。Editor 与 CLI 共用一个 lib，作者 Skill 留在根 Skills，默认作者契约在 lib/DefaultContracts。
- Sample 无参数加载应用同级 pak，不受当前工作目录影响。游戏内容缺失/损坏沿现有窗口启动错误流程报告。默认 profile 位于用户 LocalApplicationData/GalNet/<project identity digest>，打包目录中 content.json 的 projectId 决定身份，安装位置和 buildId 不参与。没有自动迁移/删除旧数据。
- EnableLaunchArguments / GALNET_LAUNCH_ARGS 独立控制普通参数，默认跟随 EnableGameDebugging。游戏调试要求参数开关开启；玩家构建拒绝参数，开发构建可覆盖内容/profile。基准脚本显式开启普通参数，保留 Release 基准用法。
- Publish.ps1、Release.ps1 提供 SampleFlavor Player/Developer。默认玩家版排除调试工具、参数解析与 PDB；开发版保持 Release 优化，保留调试及符号，文件名追加 -developer。开发 R2R 恢复图有独立锁文件，契约 IL 的编译规则不变。
- 新 galpak v2 所有 ZIP 条目在 pak/，manifest.Files 和运行时内容路径仍相对内容根；安装显式目标不再追加目录，默认 v2 安装到归档旁 pak。旧 v1 平铺包仍读取并保留原默认安装目标。重复/越界/根外/未声明载荷拒绝，已有不同安装不覆盖。
- 目录导出和整合发行的目标直接是 pak。galpak 旁匹配 buildId 的 StoryPatches 与 ResourcePatches 合入内容根，其他剧情构建不复制。内容/PAK/save 版本与构建身份算法没有因运输外层变更。

## 验证证据

| 验证 | 结果与证据 |
| --- | --- |
| 归档、安装、剧情包和玩家启动策略首轮 | 25 passed，out/package-layout/tests/archive-player.trx |
| 最终玩家完整集成回归 | 154 passed，out/package-layout/tests/final-player.trx |
| Release 开发能力与契约错误子进程 | 7 passed，out/package-layout/tests/developer.trx |
| 无游戏调试的独立参数模式 | 6 passed，out/package-layout/tests/arguments-only.trx |
| 禁用参数却开启调试 | 构建门禁明确拒绝，out/package-layout/invalid-capabilities.log |
| 两种部署完整 Apps 发行 | 7 ZIP，out/package-layout/release/0.1.0-dev.42/release-index.json |
| 解压清单、契约身份、R2R、原生载荷和分发 CLI | release 的 app-validation passed=true |
| 两种 Editor/玩家真实桌面启动 | out/package-layout/desktop-smoke/app-validation.json passed=true, desktopStartup=true；不同工作目录与含空格安装路径，无参数加载唯一测试项目，测试 profile 已清理 |
| 实际开发包三种原生调试场景 + 两种 GameTestCase 内容加载 | 5 passed，out/package-layout/tests/published-native-confirmed.trx |
| 开发包符号、调试依赖、文件哈希 | out/package-layout/developer-extracted/docs/release-manifest.json；ZIP 位于 developer/0.1.0-dev.43 |
| 发布原函数处理 galpak/外部剧情目录 | out/package-layout/galpak-integration-final/verification.json 与 galpak-winps/verification.json，PowerShell 7/5.1 均通过 |
| 发布保护 | 7 passed，out/package-layout/guards/guard-results.json |
| 修改 C# whitespace / IDE0005 | format-whitespace.log、format-imports.log 均退出 0；脚本语法与 git diff --check 通过 |

原生调试验证使用开发 ZIP 的实际 EXE 与其中的契约指纹，覆盖标题连接、开始、变量修改、定位/步进、停止、重开运行、拒绝旧运行、退出确认、零退出码和玩家变量持久化。桌面 smoke 使用独立导出的唯一项目；GameTestCase 的两个实际内容目录另外通过运行时加载，不把 smoke 当作完整剧情验收。

## 偏差、修正与限制

首次构建的 Avalonia telemetry 用户日志写入、首次 format 的本机命名管道受沙箱限制；分别设置 telemetry opt-out 与允许本机验证后通过。没有修改第三方任务来掩盖问题。

原生关闭测试尚未适配已存在的退出确认遮罩，初次三项失败；补充在自有 HWND 的真实确认后，相同开发 ZIP 全部通过。经验见 .agents/lessons/L-20261029-06-native-exit-confirmation.md。发布验收还移除了历史 loose Graph/Group 预期，改为检查当前 content v2 与 PAK 布局。

本轮不做体积优化、AOT、其他平台发行；未跑完整解决方案 CI 或重新打 NuGet，没有完整剧情/所有音视频格式/原生视觉验收。未用 ACL 实测只读安装目录，但已将玩家写入显式移出安装内容，并验证不产生 pak/.galnet。旧 profile 和旧安装内容不自动搬移或覆盖。

正式说明已同步到 docs/spec/file-formats.md、architecture.md、docs/glossary.md，以及 docs/manual/release-packaging.md、content-packaging.md、customize-avalonia-sample.md、external-game-debugging.md、cpu-benchmark.md。
