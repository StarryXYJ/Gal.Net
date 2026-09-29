---
feature: F-20260929-04-quality-baseline
status: done
updated: 2026-09-29
---

# 质量基线与工程门禁：实现总结

## 结果

维护性路线图 Phase 0 已完成。测试恢复为绿色基线，仓库具备基础代码规范和自动 CI，依赖锁定已更新，活动开发文档不再依赖旧机器绝对路径。

## 主要变更

- `LastDockLayout` 往返测试按 JSON 结构语义比较，同时继续拒绝双重编码的 escaped JSON string。
- 根 `.editorconfig` 统一字符集、最终换行、空白和基础 C# 约定；行为式 NUnit 名称仅在测试目录关闭 `CA1707`。
- CI 在 pull request 和 `master` push 自动运行 Desktop/Headless 构建与两套测试；完整平台解决方案构建保留为独立手动 job。
- `Microsoft.Extensions.DependencyInjection` 及 Abstractions 从 preview 升级到稳定版 `10.0.0`，受影响锁文件已同步。
- 删除 `GalNet.Editor.Shared` 未使用的 `CommunityToolkit.Mvvm` 直接引用。
- agent knowledge 与活动工作流文档改为仓库相对路径。

## 验证

- `dotnet restore GalNet.slnx --locked-mode`：成功。
- `GeneralTest` Release：258/258 通过。
- `GalNet.Assets.Tests` Release：34/34 通过，未再产生 `CA1707` 告警。
- `GalNet.Editor.Headless` 与 `GalNet.Sample.Headless` Release 构建：成功，0 错误。
- `dotnet build GalNet.slnx --no-restore --configuration Release -m:1 -p:UseSharedCompilation=false -p:UsedAvaloniaProducts=`：成功，0 错误；覆盖 Android、Browser、Desktop 与 iOS。保留 1 条 Browser WebAssembly native reference 未链接警告。
- `git diff --check`：通过；仅显示 Git 的 CRLF 转换提示。

## 计划偏差与已知限制

- 未启用阻断式格式门禁。`dotnet format --verify-no-changes` 在当前仓库报告约 3,840 项既有格式、using 与行尾差异；本阶段若直接修复会产生与质量基线无关的大范围 churn。应先创建独立纯机械格式化 feature，再在同一 feature 中启用 CI 门禁。
- 当前环境没有 PyYAML 或 Ruby，CI YAML 未通过独立解析器执行；workflow 结构已人工复核，实际 GitHub Actions 运行仍是最终验证。
- 完整解决方案在受限沙箱中会因 x64 MSBuild task host 通信失败而中止；同一命令在非沙箱环境成功，确认不是项目编译错误。

## 正式文档

- [Feature 工作流与 Agent 知识维护](../../docs/design/feature-workflow.md) 已改用仓库相对路径。
- [Feature 索引](../README.md) 已改用仓库相对路径。

## 后续工作

- 路线图 Phase 1：形成契约归属 ADR、允许的项目引用矩阵和首批架构测试。
- 独立格式化 feature：清理既有格式债务并启用 `dotnet format --verify-no-changes` CI 门禁。
- Gallery feature 的外部测试阻塞已解除，可按其自身 closeout 流程完成状态更新。
