---
feature: F-20261009-01-track-seffects
status: completed
created: 2026-10-09
---

# 实施计划

## Phase 1 — descriptor 与 Runtime 防护

**状态：completed**

建立 catalog、host capability 连接与参数校验；覆盖注册、未知 ID、错误参数和合法 attachment。

退出条件：Runtime 绝不向 host 发送未注册或不合法的 apply request。

## Phase 2 — Avalonia processors

**状态：completed**

实现 registry、gain、低通和高通 processor，按 voice 重算 output gain/equalizer，覆盖 apply/stop 聚合。

退出条件：已达成。Core、Runtime 和 Presentation 测试通过；Headless 成功编译 GameTestCase。
