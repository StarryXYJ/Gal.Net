---
status: confirmed
feature: F-20261029-05-cross-platform-applications
---

# Release 使用构建与产物门禁，不重复完整测试

## 现象

`v0.1.0-preview.3` 的 release workflow 已通过 tag 与 `master` 归属校验，但在 `Invoke-CI.ps1 -Stage BuildTest` 的完整测试阶段失败，导致后续构建、封存和上传均被跳过。

## 决策与原因

常规 CI 已独立承担测试门禁。release 的职责是从精确 tag 构建正式版本、验证 NuGet/应用/ZIP 消费与载荷，并封存、上传同一份已验证产物；重复运行完整测试既不增强这份产物的验证，也会阻塞发布。

## 正确做法

release workflow 只保留精确 tag/master 校验、正式版本构建、`Release.ps1` 的产物与消费者验证、封存和上传。验证 evidence 收集只收集 release 打包日志与 `package-validation.json`。

## 禁止做法

不要把 `Invoke-CI.ps1 -Stage BuildTest` 或其他完整测试矩阵重新放入 release job；测试应在常规 CI/PR 门禁中执行。也不要因 release 测试失败而跳过 tag、版本、消费者或 ZIP 验证。

## 适用范围

所有 GalNet 版本 tag release；来源工作流为 `.github/workflows/release-build.yml`，维护说明为 `docs/manual/publishing/engine-release.md`。
