---
feature: F-20260929-12-editor-extension-type-safety
status: planned
updated: 2026-09-29
---

# 实施计划

## Phase 1 - API 行为保护与强类型 bridge

**状态：verified**

**任务：** 为 registry 与现有 contribution bridge 补测试；在 Editor.Abstraction 新增无参数、带参数和 inspector 泛型基类；验证错误类型与 null 参数诊断。

**验证：** Editor.Tests、Architecture.Tests、Editor.Abstraction build。

**证据：** `GalNet.Editor.Tests` 27/27、`GalNet.Architecture.Tests` 14/14 通过；`GalNet.Editor.Abstraction` Release 构建成功。Editor 测试因 Avalonia BuildServices 需要写入用户目录而在沙箱外执行；`--no-restore` 构建仅报告离线 NuGet 漏洞源 `NU1900`，没有新增编译或分析器警告。

**退出条件：** 旧接口不变，泛型 bridge 有正常和失败测试，创建独立 Git 提交。

## Phase 2 - 内置 contribution 迁移

**状态：verified**

**任务：** 将 6 个内置 dock panel 与 3 个 inspector contribution 迁移到泛型 delegate 实现；补一个从 registry 查找到内置 ViewModel 创建的组合测试。

**验证：** Editor.Tests、Editor.Shared.Tests、Editor 与 Editor.Headless Release build。

**证据：** 6 个内置 panel 与 3 个 inspector contribution 已迁移到泛型 delegate；注册 lambda 中不再包含 parameter/dock ViewModel 强转。`GalNet.Editor.Tests` 28/28、`GalNet.Editor.Shared.Tests` 16/16 通过；Editor 与 Editor.Headless Release 构建成功，只有既有分析器告警和离线 NuGet 漏洞源 `NU1900`。

**退出条件：** 内置注册 lambda 不再直接强转 parameter/dock ViewModel，创建独立 Git 提交。

## Phase 3 - 文档与路线图收尾

**状态：planned**

**任务：** 同步扩展 API 约束、路线图状态、feature summary 与 agent knowledge。

**验证：** 10 个测试项目、完整 solution Release build、`git diff --check`。

**退出条件：** Phase 5 有完成证据且没有未记录的兼容性风险，创建独立 Git 提交。
