# 实现自审

结论：pass。本记录为实现者自审，未进行独立 agent 审核。

核对 feature 目标与实际 diff：PrimitiveImplementation 已删除，生产/测试和当前正式文档没有残留 API 调用；没有新增逐指令描述类型。注册表按 view 组合，业务实例按调用创建，Options 解码仍基于规范化参数，模块继续拥有会话共享状态。

核对行为边界：完整 ID 排他、无 schema 错误、DTO 身份、null 实例、BatchId、同步异常和异步错误可见性保留；内置选择仍按传入 catalog 筛选，最终契约继续校验。自审发现嵌套选择应取交集，已实现并加入回归；注册结束和失败后均冻结，避免被保留的引用晚写注册表。场景重放继续使用 request 入口，未引入 Contracts 到 Core/Runtime 的反向依赖。

验证证据：Core 104、Runtime 80、Presentation 185、Builtins 110、Integration 104、Architecture 37 共 620 项通过；最后筛选修订后相关三组再次全部通过。解决方案构建成功；whitespace、IDE0005 和 git diff --check 通过。API 示例同步到 development/spec；实际生产成本包含新增注册表全部 88 行，净减 37 行。

迁移注意：外部自定义 IPrimitiveModule 须从 Instructions 属性改为 Register(PrimitiveRegistry)，PrimitiveModule 从描述集合改为注册回调。没有兼容旧 API 的适配层。本次未运行 Explicit 原生媒体用例或完整 GUI 人工验收；未发现阻止本次注册重构交付的问题。
