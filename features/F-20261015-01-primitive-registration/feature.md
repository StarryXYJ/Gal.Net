# Primitive 注册简化

用户授权：2026-10-15，移除 PrimitiveImplementation，让泛型注册入口解码 Options 并交给每次调用的实例，保留同步短回调。

范围：执行注册、内置模块、适合直接消费 Options 的实例、Sample、自定义注册示例与回归测试。保留 schema/执行独立组合、模块共享状态和所有权；不改变内容、存档、Engine 调度或播放模式语义。

验收：生产 API 不再含 PrimitiveImplementation；保留完整 ID 排他、schema/DTO 检查、独立 Options、BatchId、失败传播、部分内置选择与逆序释放；报告包含公共机制成本的实际生产净行数。
