# Phase plan

## P1 — verified

实现事件采集、编译开关、Editor/Sample 埋点和脚本；验证事件序列、Dispose 幂等、跨 await、容量上限、禁用后类型不存在；构建两个宿主；运行 Editor 重复启动基线。退出条件：实际证据记录在 summary.md，限制明确。

退出证据：4 项启用模式测试、1 项禁用模式测试、49 项架构检查通过；两个宿主的普通与 Benchmark Release 均构建通过，普通 deps 无诊断引用；Editor 和 Sample 自动采集、最终 smoke 和失败路径均验证，见 summary.md。

## P2 — verified

用户授权按 experiments.md 逐项测量、验证和独立提交；不改变 ready 口径，不提交其他 UI feature 改动。

三项实验完成：提前准备欢迎页在 20 对交错测试中改善约 5.4%，相关 3 个行为/主题测试通过，独立提交 4bb1dbc；主题探测及 Acrylic 探测均未达到门槛并逐字节恢复。恢复后的最终 Benchmark 构建与真实进程启动成功，记录见 experiments.md、summary.md。

## P3 — verified

用户授权继续采集启动调用栈，并在 JIT 占比明显时测试 ReadyToRun。增加 Benchmark 专用、显式启用的 EventPipe 事件镜像，用 dotnet-trace 将阶段与 JIT/采样栈对齐；普通构建无实现。采样只用于诊断，不参与优化收益统计。若进行 R2R，对比相同 RID、部署形式和源代码的普通 publish 与 R2R publish，保持契约程序集字节身份；以固定快照交错测量并验证后决定提交或拒绝。

退出证据：三次基线调用栈确认大量启动 JIT；Editor/Sample 各 10 对固定发布快照全部改善，中位数分别降低 41.6%/31.6%。保留 WindowsReadyToRun 发布配置，并隔离 RID 还原图；契约闭包排除转换且逐字节验证。启用/禁用诊断测试 5/5、1/1，Editor 相关回归 3/3；两个普通发布真实启动并正常退出、无诊断依赖，Editor 实际加载 45 个契约条目。详细数据与限制见 runtime-profile.md。
