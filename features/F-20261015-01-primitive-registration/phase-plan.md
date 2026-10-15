# 阶段

1. verified：迁移注册机制、所有生产/测试消费者，保留现有错误和生命周期语义。注册、Options、播放模式、异常、释放与内置子集回归通过；注册成功或失败后冻结，并验证嵌套 catalog 交集。
2. verified：复杂实例 Options 接口、Sample、正式文档、agent 上下文与总结已完成。六组共 620 项不重复用例通过，解决方案构建 0 错误，改动文件格式/import 门禁与 diff 检查通过。生产净减 37 行，包含新注册机制。自审及边界见 review.md / summary.md。
