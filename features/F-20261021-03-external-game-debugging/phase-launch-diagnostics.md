# 启动诊断与契约导出修正

日期：2026-10-23。状态：verified-with-manual-follow-up。

## 触发与范围

用户反馈启动没有反应。读取实际项目启动配置、对比项目与 Sample 的实际契约 DLL 哈希后，以相同预期契约参数启动 Sample，复现 `InvalidDataException: The App contract differs from the Editor project`。契约校验先于日志初始化，原 Editor 没有监控进程，导致等待连接而无提示。

用户要求提供导出 DLL 到根 output 的 ps1，并忽略生成物；Editor 应提示错误。作为已有 feature 的一个修正阶段提交，保留真实契约身份校验。

## 实现

- `scripts/contracts/export-sample-contracts.ps1` 构建 Debug/Release Avalonia Sample，验证实际 App 和契约产物哈希相同，将契约及依赖 DLL 导出至根 `output/Contracts`，打印对应 App 路径；`/output/` 加入 Git 忽略。
- Editor 在项目会话内持有自己启动的进程监控；持续排空 stderr，只保留最近 8192 字符。连接前任何退出、连接后非零退出均通知现有调试面板 Error 与 Editor 日志；启动本身抛错也进入面板及日志。
- 断开时取消等待/读取并释放进程句柄，不杀 App；会话取消和代次检查防止旧进程污染新会话。
- Sample 顶层捕获日志初始化前的参数/契约错误，向 stderr 输出异常，以退出码 1 结束；运行期 fatal 同样保留非零退出码。
- 未扩展为任意应用通用日志协议；IDE 自行启动的进程仍由 IDE 管理。

## 验证

- 脚本实际执行成功，输出三个 DLL，导出契约与 Sample 里的 DLL SHA256 一致，`git check-ignore` 确认输出已忽略；构建 0 错误，环境无法查询 NuGet 漏洞数据产生 NU1900 警告。
- Integration 的外部 Sample 调试定向测试 2 项通过（原契约校验 + 新真实子进程失败路径）；新测试确认非零退出、具体异常、Changed 通知、断开后新会话 Error 清空，不需要原生窗口。
- Editor 调试面板挂载回归构建被正在运行的 .NET Host 锁定默认 Editor 输出目录，MSB3027/MSB3021；不关闭用户进程，改用隔离输出验证构建。实际窗口提示待重启人工确认。
- `dotnet build src/Editor/GalNet.Editor/GalNet.Editor.csproj --no-restore --output output/Editor -v quiet` 成功，0 错误、67 个现有/环境警告，隔离可执行产物可用于重启验证。
- `git diff --check`：通过；不包含另一个 feature 开工时已有的 phase-plan.md 改动。
