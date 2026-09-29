---
feature: F-20260929-04-quality-baseline
updated: 2026-09-29
---

# 设计

## 嵌入 JSON 语义

`LastDockLayout` 使用字符串承载结构化 JSON，但写入设置文件时通过 `EmbeddedJsonStringConverter` 作为嵌入 JSON value 输出，读取后再交给 `DockLayoutSerializer` 解析。外层序列化器可以合法改变缩进和换行，因此测试应比较 `JsonElement` 结构，而不是比较原始字符串。

转换器继续拒绝 escaped JSON string，避免设置文件重新出现双重编码。

## 规范边界

`.editorconfig` 只建立不会引发全仓库格式 churn 的基础规则：字符集、最终换行、空白、C# using/namespace/braces 建议。行为式 NUnit 测试名保留现有可读风格，仅在 `test/**/*.cs` 中禁用 `CA1707`。

## CI 分层

- `desktop-build-and-test` 是 PR/push 必须通过的门禁：locked restore、Desktop/Headless 构建、两个测试项目和覆盖率产物。
- `platform-build` 独立执行 Launcher workload/solution 构建。平台 SDK 或 workload 失败应显示在独立 job，不与核心测试日志混在一起。
- 格式验证只有在当前仓库能够无修改通过时才加入；若已有格式债务，则在后续纯格式 feature 中启用，避免建立天然失败的门禁。

## 依赖治理

中央包版本升级只修改 `Directory.Packages.props`，随后由 `dotnet restore` 机械更新受影响的 `packages.lock.json`。未使用包先从直接引用项目删除，再通过 locked restore 和构建确认没有隐式依赖。

