# 内置作者契约

本项目只依赖 GalNet.Core，提供参数契约、纯参数校验和编译期 Composite；不创建执行模块、Engine、媒体或 UI。

```csharp
using GalNet.Contracts.Builtins;
using GalNet.Core.Entry;

var selected = new TargetProfileEntryCatalog(
[
    BuiltinSchemas.CreateDialogueModule(),
    BuiltinSchemas.CreateFlowModule()
]);

var all = new TargetProfileEntryCatalog(BuiltinSchemas.CreateAllModules());
```

CreateAllModules 只返回 14 个独立模块的只读列表。各组件是 Dialogue、Nvl、Layers、Animations、Audio、Video、Effects、Particles、Flow、Variables、Gallery、Transitions、Voice 和 Custom，不存在聚合大模块。Animations 包含人物动作、震动、动态集中线和冲击 Composite，Transitions 包含 shader 遮罩与模糊转场；它们使用显式项目 shader 引用并编译为 timeline.play。

选用 Composite 时，也要选用其输出所需的 primitive；编译器验证缺失契约。游戏宿主另行注册执行实现，由 GalNet.Primitives.Builtins 或自己的平台程序集提供。完整 typeId 唯一，模块名/前缀不用于匹配执行实现。

内置稳定 ID 集中在 BuiltinEntryIds；所有带参数的 Primitive 和 Composite 使用 DTO.Schema，无参数指令使用空表。参数与纯展开逻辑不再需要 Primitive 作者子类。Composite 通过 ForDto 接收类型化输入，子指令可使用 ValueCodec.SerializeFields 和子 DTO 构造；开放动画事件 payload 保持 JSON。
