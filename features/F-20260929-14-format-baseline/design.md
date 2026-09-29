---
feature: F-20260929-14-format-baseline
status: proposed
updated: 2026-09-29
---

# 设计

盘点确认最终命令集合为：

```powershell
dotnet format whitespace GalNet.slnx --no-restore
dotnet format style GalNet.slnx --no-restore --diagnostics IDE0005
```

前者只处理 whitespace、最终换行和字符集，后者只处理 Roslyn import 排序/清理；不运行 `analyzers`，也不让 `style` 扩大到其他诊断。应用格式时使用固定 SDK、locked restore 和单一工作树；格式提交只包含机械输出。

CI 使用与本地清理完全相同的 solution、子命令、诊断列表和 `--no-restore --verify-no-changes` 参数，避免本地与 CI 的格式定义漂移。验证期间按既有规则顺序运行测试，并为 Avalonia 完整构建设置受限环境参数。

若格式工具对生成文件、XAML 或第三方样板产生不稳定输出，只通过精确路径或诊断范围排除，不使用全局关闭掩盖普通源码。
