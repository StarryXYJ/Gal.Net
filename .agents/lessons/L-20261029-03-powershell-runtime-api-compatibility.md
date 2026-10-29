# PowerShell 脚本必须验证实际宿主的 .NET API

状态：confirmed
日期：2026-10-29
来源：用户反馈 Sample 启动脚本在 Windows PowerShell 报 MethodNotFound（独立修复）

- 现象：run-avalonia-sample.ps1 调用 System.IO.Path.TrimEndingDirectorySeparator 失败；Headless 脚本使用同一表达式。
- 根因：该方法存在于现代 .NET，但 Windows PowerShell 5.1 运行于 .NET Framework，不提供它。开发环境默认 pwsh 的成功不能证明系统 powershell.exe 兼容。
- 正确做法：无显式高版本宿主要求的简单路径处理使用两者共有的 API。本次改为 String.TrimEnd(char[])；项目路径已转绝对路径，仅在与固定 GameTestCase 路径比较时去除末尾分隔符，不用于文件删除或根目录重写。
- 验证：Windows PowerShell 5.1.26100.9444 与 PowerShell 7 各运行两个原样复制的 Sample 脚本，覆盖默认、反斜杠结尾、斜杠结尾、大小写、自定义项目、SkipBuild，共 24 项通过。外部构建/部署/启动被记录桩替代，只验证实际宿主下的路径及调用控制流，不声称重新验收播放器。
- 禁止：仅做 PowerShell 语法解析或仅用 pwsh 执行，就宣称 Windows PowerShell 兼容；通过要求用户换 shell 掩盖普通脚本新引入的 API 不兼容。
- 证据：artifacts/powershell-sample-compat/；相关脚本 scripts/samples/run-avalonia-sample.ps1、run-headless-sample.ps1。
