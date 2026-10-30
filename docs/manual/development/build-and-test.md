# 构建、测试与本地 CI

## 本地运行 CI

```powershell
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1
```

入口先检查 SDK、发布保护与发布发现/过滤器，再执行 locked restore、Release 构建和桌面测试计划。普通 Windows CI 从 `GalNet.slnx` 自动生成 `.slnf`，保留测试和 fixture，排除 `GalNet.Sample.CrossPlatform/` 平台工程，避免要求 Android/iOS/Browser 工作负载；这不表示平台工程仍是空模板。restore/build/test 使用同一 Configuration；构建完成后才串行启动 no-build 测试，收集全部项目的失败，最后返回非零。真实设备的 Explicit 测试不属于普通 CI。默认 `All` 和 `BuildTest` 均不运行格式检查，格式是显式本地操作。

可以单独运行：

```powershell
# 只检查空白与未使用的 using，不修改文件
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1 -Stage Format
# 一键修复上述格式问题，会修改源码，运行后检查 diff
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1 -Stage Format -FixFormat
pwsh -NoProfile -File scripts/ci/Invoke-CI.ps1 -Stage BuildTest -Coverage
```

默认日志保存在唯一的 `out/ci/<run>/`；`ci-results.json` 记录整体结果、源码 commit、整体和每阶段耗时、退出码与完整日志路径，`test-results/` 保存 TRX 与可选 coverage。`-OutputRoot` 可指定证据目录。Actions 的 CI workflow 只在 PR 或手动触发时运行一个 BuildTest job；master 普通合并不重复跑 CI。默认关闭 coverage，手动 Actions 输入或本地 `-Coverage` 可开启；NuGet 依赖按 SDK 和锁文件缓存。后端三平台及 NativeAOT 矩阵保留为手动专项，NativeAOT 需勾选 `native_aot`。`-FixFormat` 只允许搭配 `-Stage Format`，不会在默认 CI 中隐式改写源码。Windows 的 dotnet format/testhost 需要允许子进程与命名管道通信；受限执行沙箱内的管道拒绝不是源码格式错误。
