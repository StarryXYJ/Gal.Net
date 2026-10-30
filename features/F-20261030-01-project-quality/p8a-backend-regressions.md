# P8a：后端 PR 回归与实际运行证据

日期：2026-10-30。状态：verified。正式 spec/manual 仍由用户后续整理。

## 改动与职责

- `backend-portability.yml` 在相关 Shared、Storage、Audio、SchemaGenerator、Headless、测试、构建配置及 CI 脚本的 PR 上执行 Windows/Linux/macOS 后端矩阵；保留手动入口，NativeAOT 仍为手动可选任务。
- `verify-backends.py` 负责精确 SDK 检查、原生库准备、locked restore、七个既有测试工程和真实 Headless 进程。默认沿用现有 CMake 构建；本地可用 `--native-audio` 指定库。每步保留命令、退出码、时长和日志；整体输出 JSON、逐项 passed/failed/not_run 与 TRX 计数。
- 已存在的输出不会被覆盖。缺库、原生构建失败、SDK 不符、缺失/零执行 TRX、测试失败、Headless 非零退出或缺少验收标记均失败。独立测试失败后继续收集其他测试的结果。设备音频、GUI、NativeAOT 明确为未验证。
- `process_steps.py` 提取发布脚本已有的进程执行/日志/自有进程树清理；两个入口复用。修正 Windows 清理命令失败后无限等待，清理与等待均有上限。发布入口保护测试兼容 macOS 的进程查询，并验证被观察的无关进程确实存活。
- 架构测试读取 MSBuild ProjectReference 时统一两种路径分隔符，修复 Linux 对反斜杠路径的误报。生产程序集的职责、依赖和运行行为不变。

Headless 调用现有 `--aot-self-test`，构建时显式关闭 JSON 反射回退；本阶段运行托管程序集，不传 `--expect-aot`。自检覆盖生成契约、引擎快照/恢复、内置指令、真实包内资源读取和文件保存/进度持久化。成功必须同时有退出码 0 和 `AOT_ACCEPTANCE_OK`。

## 最终验证

以 `2e26604089b331530c85d900f13556e73247b6d2` 为源码基线，冻结两个独立副本，只叠加本阶段脚本及架构路径修正。主工作树同时有播放器改造；最终证据不包含其新增用例或行为。均使用 SDK 10.0.401、Release、locked restore，无 `--no-restore` 入口绕过。

| 工程/检查 | Windows 通过 | Linux 通过 | 未执行 |
| --- | ---: | ---: | --- |
| Core | 139 | 139 | 0 |
| Assets | 248 | 248 | 0 |
| Runtime | 131 | 131 | 0 |
| GameHost | 10 | 10 | 0 |
| Storage.FileSystem | 32 | 28 | Linux 4 个 Windows 句柄占用用例 |
| Media.Audio | 26 | 26 | 各 2 个 Explicit 实际音频设备用例 |
| Architecture | 53 | 53 | 0 |
| 测试合计 | 639 | 635 | Windows 2、Linux 6 |
| Headless 实际执行 | 退出 0 | 退出 0 | 均输出 `dynamicCode=True reflectionJson=False` |
| 后端入口保护测试 | 13 | 13 | 0 |
| 发布/进程清理保护测试 | 5 | 4 | Linux 跳过 1 个 Windows 专属清理失败测试 |

Linux 为 Ubuntu 26.04.1 / WSL，使用既有隔离 SDK；通过进程级库路径复用既有 ICU，保持正常全球化，没有安装系统包或启用 invariant globalization。原生音频使用当前源码以 gcc 编译的 `.so`；Windows 使用已有 native DLL。实际 Unicode WAV/Vorbis 解码均执行。

本地可复现入口（需要 Python 3.11+、固定 SDK，以及 CMake 或显式原生库）：

```text
python scripts/ci/test-backend-checks.py
python scripts/ci/test-application-publish.py
python scripts/ci/verify-backends.py --output <新的证据目录>
python scripts/ci/verify-backends.py --native-audio <当前平台库> --output <新的证据目录>
```

证据（`out` 为忽略目录）：

- Windows：`out/project-quality-p8a-windows-frozen/backend-results.json`、`logs/`、`results/`。
- Linux：`out/project-quality-p8a-linux-frozen/backend-results.json`、`logs/`、`results/`；原始目录 `/tmp/galnet-quality-p8a-kwNGUl/evidence-frozen`。
- 入口保护：`out/project-quality-p8a-python-guards.log`、`out/project-quality-p8a-linux-guards.log`；发布/进程清理用例真实执行见本轮工具记录。
- 两个源码副本：`out/project-quality-p8a-source`、`/tmp/galnet-quality-p8a-kwNGUl/source`；只叠加 P8a，避免并行工作树变化影响计数。
- Python 五个脚本语法解析通过，改动 whitespace 检查通过；人工核对 workflow 触发范围、矩阵、失败传播及总是上传证据。

## 验证中发现并修正的问题

1. NUnit 5.0.0 对 Explicit 用例可能给出 `total=28, executed=26, notExecuted=0`。报告保留原始计数，另以 `total-executed` 记录 `notRun`；增加回归，避免把设备测试漏记。见 lesson L-20261030-12。
2. 受限 Windows 宿主中的 taskkill 返回 1/Access denied，旧实现忽略后无限 wait。首次探针由外部清理，不计为自动清理通过；修正后在授权环境验证自有父子退出、无关进程存活，并以确定性失败替身验证清理失败不会再次无界等待。见 lesson L-20261030-13。
3. Linux 首轮架构测试 21 失败/32 通过，错误内容为包含反斜杠的完整 ProjectReference 被当成工程名。统一分隔符后冻结源码 53/53 通过；未放宽允许依赖集合。
4. Linux 初次 SDK 缺 ICU 使入口保护失败；复用既有库后通过。Windows 初轮受限网络产生 NU1900，最终授权 locked restore 无该告警。既有分析器告警及 gcc/miniaudio 编译告警保留，没有据此宣称零告警。

## 边界与下一步

- 尚未推送/触发 GitHub Actions，也未在 macOS 本机执行。矩阵配置完成不等于这些托管 runner 已通过。
- 两端最终本地验证使用显式原生库；默认 CMake 命令沿用旧 workflow，原生构建失败报告由保护测试覆盖，本轮未在本机执行默认 CMake 构建分支。
- 托管 Headless 验收不能证明 GUI 启动、真实设备音频或 NativeAOT 成功；报告保留这些边界。
- P8b 仍 planned：与并行跨平台工作衔接，补充发布后应用启动、内容读取及正常退出。性能基准只保留既有证据，没有增加不稳定耗时门禁。
