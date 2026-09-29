# 全仓机械格式基线与 CI 门禁总结

## 结果

- 使用 `dotnet format whitespace` 统一 whitespace、最终换行与 UTF-8 编码。
- 使用限定为 `IDE0005` 的 style 命令排序并清理 import，没有批量修复语义分析器告警。
- 机械基线修改 129 个 `.cs` 文件，提交中不包含运行时行为变更。
- CI 新增独立阻断式 `format` job，使用与本地完全相同的 solution 和验证命令。

## 分阶段提交

- `6c25016 docs: define repository format baseline`
- `eca86ad style: establish repository format baseline`
- Phase 3 的 CI 门禁与收尾在最终提交中完成。

## 验证

- `dotnet format whitespace GalNet.slnx --no-restore --verify-no-changes`：通过。
- `dotnet format style GalNet.slnx --no-restore --verify-no-changes --diagnostics IDE0005`：通过。
- 10 个测试项目顺序执行，340/340 通过。
- 完整 `GalNet.slnx` Release build 覆盖 Desktop、Browser、Android 和 iOS，0 错误。
- `git diff --check`：通过。

## 限制

CI workflow 的结构与命令已本地复核，但首次真实 GitHub Actions 运行仍是远端 workload 恢复和环境可用性的最终验证。现有 CA、NUnit、Avalonia XAML 和 Browser native-reference 告警不属于格式基线范围。
