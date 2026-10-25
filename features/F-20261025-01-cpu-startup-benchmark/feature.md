# CPU 启动 benchmark

状态：verified，quick 通道。用户授权：先实现 CPU 事件计时、Benchmark 编译开关及自动化启动测试；Editor 与 Sample 共用。

目标：区分构建、程序初始化和各启动阶段，形成可重复的优化基线。
范围：独立诊断程序集；手工方法内埋点；有界线程安全事件；Chrome Trace JSON；启动完成自动退出；重复运行和统计脚本。
非目标：GPU timestamp/query、PIX 注入、自动 IL 编织、BenchmarkDotNet 微基准、此次直接优化启动。
验收：启用与禁用构建均通过；区间/瞬时事件和并发/容量测试通过；Editor 自动运行生成有效 trace 与统计；Sample 接入编译并尽可能实测。

设计见 design.md；验证计划见 phase-plan.md；最终证据见 summary.md。

P2 后续优化已完成三项对照实验；仅保留显示窗口前准备欢迎页，其他探测恢复原状。处置、提交和证据见 experiments.md。

P3 已完成调用栈诊断及契约安全的 ReadyToRun 发布对照，Editor/Sample 各 10 对全部更快。保留显式发布配置和通用诊断脚本，见 runtime-profile.md；普通 Debug 启动方式不变。
