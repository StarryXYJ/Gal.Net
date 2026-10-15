# 设计

IPrimitiveModule.Register(PrimitiveRegistry) 贡献工厂。PrimitiveModule 接受注册回调并保留释放扩展点。CompositeGameView 创建与 catalog 绑定的注册表，在组合时校验完整 ID/schema/DTO，冻结后仅保存 ID → Func<PrimitiveCreateContext, PrimitiveInstance>。

PrimitiveRegistry.Register<T> 解码独立 Options；Immediate / Async 保留同步和单次异步便利入口。移除 PrimitiveImplementation，不新增逐指令描述对象。RegisterDeclared 用于内置契约子集筛选，仍由选择包装模块负责原模块释放。注册回调不得启动业务执行。

复杂实例在合适处接收 context 和 Options；仍被场景重放和底层测试消费的 request 构造路径保留，避免破坏重放边界或为删行复制状态机。同步指令继续使用回调，避免逐条创建无状态类。

风险：公共 API 迁移；注册表在组合后必须拒绝修改；契约子集只能跳过未声明指令，不能跳过已声明指令的 DTO 不匹配；Options 解码不能改变省略/null/默认值语义。Async 的错误先于完成事件发布保持不变。
