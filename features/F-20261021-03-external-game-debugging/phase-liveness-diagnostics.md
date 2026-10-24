# 启动存活提示与可分享诊断日志

日期：2026-10-24。状态：implementing。

## 用户反馈

Editor 启动游戏测试不应无限等待；出现启动或响应问题时，显示错误即可，不要求弹窗。后续需要能让用户提供足够上下文的日志。

## 实现边界

- 本地启动后最多等待 30 秒完成认证连接；超时报告错误但继续监控并保留迟到连接机会，不杀 App。
- 调试请求最多等待 60 秒；状态与检查轮询最多等待 10 秒。请求超时提示远端操作可能仍在运行，不自动重试。
- 每次调试准备在测试 profile 的 `logs/` 中创建一个 Editor 诊断文件，与 Sample 的 `game-*.log` 放在一起。
- 诊断文件最多 1 MiB、保留最近 20 份；包含保存、构建、作者映射耗时，启动元数据，连接/请求事件，进程退出码和 App stdout/stderr。调试 token 不写入启动元数据，捕获输出中的 token 会被遮盖。
- 现有调试面板错误区与 Problems 面板用于提醒；不引入模态窗口，不终止 Editor 启动的外部 App。

## 当前改动

- `ExternalGameDebugClient` 增加连接与请求超时，并在调试面板错误中带出诊断文件路径。
- `DebugLaunchDiagnostics` 维护有界、逐次启动的会话日志。
- 子进程 stdout/stderr 转发到现有 Game Log Panel 和 Serilog Console sink；断开后不等待外部进程退出，并继续排空重定向输出，避免子进程管道阻塞。
- 内容构建和作者源码映射放到后台执行，Debug Dock 显示保存、构建、作者映射与启动阶段；相关状态文案补充四种本地化。
- 接入手册和 feature 验收范围已更新。

## 验证与收尾

- Editor 构建：`dotnet build src/Editor/GalNet.Editor/GalNet.Editor.csproj --no-restore --output Temp/F-20261021-03-debug-diagnostics-build` 成功，0 错误；包含已有分析器/XAML 警告。
- 待检查：四语 JSON 完整性、代码格式与差异范围。
- 待验证：无连接超时、契约校验失败日志、正常连接/运行日志、请求无响应错误，以及超时后进程未被终止、迟到连接仍能恢复。
- 当前未运行测试或真实 Sample 行为验证；本次按用户要求先提交进行中实现，行为验证完成前不标记 verified。
