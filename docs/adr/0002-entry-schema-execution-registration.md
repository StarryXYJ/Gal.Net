# ADR-0002：Entry 契约与执行注册分离

状态：Accepted（2026-10-10）

## 上下文

旧 EntryModule 同时保存参数 schema、composite 展开和 primitive 工厂。编译器必须创建运行期模块，平台依赖与契约耦合；按模块/前缀排他注册不利于局部替换。此次迁移不保留旧 API 或存档兼容，不改造 Editor/Launcher 功能。

## 决定

`IEntryModule` 只声明 schema 和编译期展开；`IPrimitiveModule` 贡献完整指令 ID 与实例工厂并拥有共享状态。`GameRegistration` 独立组合两者。前缀和 schema 模块名不排他，仅完整 ID 必须唯一。主内置入口 `AddBuiltins` 组合 schema 和所有执行模块；开发者可只选择 schema、部分内置逻辑或全部自定义实现。

轨道规则留在平台无关 `AudioRuntime`，播放由 `IAudioPlaybackHost` 提供。Avalonia/LibVLC 和 Unity/原生音频均通过组合接口接入；不强制继承内置基类。执行模块是共享状态的生命周期单元，指令是路由单元。

## 候选与取舍

- 逐条注册可用于小扩展，但不强迫所有开发者采用；领域模块便于共享轨道和服务。
- 继续将工厂放入 schema 会维持编译器/平台耦合，因此不采用。
- 强制平台继承轨道基类会绑定后端结构；组合接口更容易适配已有播放系统。

## 后果

宿主须明确管理 Engine、view、presenter 的释放顺序；schema-only view 可构建，但 GameEngine 拒绝缺失执行实现的 Graph。新执行模块必须提供对应 schema，重复 ID 明确失败。旧模块工厂 API 与无消费者音频协议删除；快照移除冗余索引并升级版本 3。验证由注册、运行时、内置和跨层集成测试覆盖，使用示例见 [development](../development.md)。

旧 Presentation.Defaults 的唯一剩余空 view 移入 Presentation.Abstractions，独立 Defaults 项目和无用引用删除。
