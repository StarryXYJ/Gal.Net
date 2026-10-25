# Phase plan

## P1 — verified

实现事件采集、编译开关、Editor/Sample 埋点和脚本；验证事件序列、Dispose 幂等、跨 await、容量上限、禁用后类型不存在；构建两个宿主；运行 Editor 重复启动基线。退出条件：实际证据记录在 summary.md，限制明确。

退出证据：4 项启用模式测试、1 项禁用模式测试、49 项架构检查通过；两个宿主的普通与 Benchmark Release 均构建通过，普通 deps 无诊断引用；Editor 和 Sample 自动采集、最终 smoke 和失败路径均验证，见 summary.md。

## P2 — verified

用户授权按 experiments.md 逐项测量、验证和独立提交；不改变 ready 口径，不提交其他 UI feature 改动。

三项实验完成：提前准备欢迎页在 20 对交错测试中改善约 5.4%，相关 3 个行为/主题测试通过，独立提交 4bb1dbc；主题探测及 Acrylic 探测均未达到门槛并逐字节恢复。恢复后的最终 Benchmark 构建与真实进程启动成功，记录见 experiments.md、summary.md。
