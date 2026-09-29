# Runtime 端口与保存 API 清理设计

## 契约归属

- `IGameProgressService` 只由 `GameEngine` 消费，放入 Runtime.Progress；FileSystem 实现与 Editor/Sample 组合根依赖 Runtime。
- `IGameExitService` 与 `ISettingsService` 只有 Editor 消费者，放入 Editor.Abstraction.Services，使 Editor.Shared 实现依赖抽象而非 Core 宿主杂项。
- `ITextResolver` 留在 Core.Services，因为 Core 的 `IGameRuntime.TextResolver` 暴露它；迁入 Runtime 会造成 Core 反向依赖。
- 无消费者接口直接删除，不建立 obsolete 兼容层；仓库没有已证明的外部兼容要求。

## 保存 API

目标接口只保留：异步槽位枚举/读取/删除、`SaveRequest` 保存、quick-save 对应操作及 metadata 查询。所有 I/O 方法最后一个参数都是可选 `CancellationToken`。

FileSaveService 将 token 传到 gate、文件读写与 metadata 读取。调用方显式构造 `SaveRequest`，避免同一语义同时存在 snapshot 与 request 两套重载。

## 验证

使用行为测试覆盖保存内容、description、preview、删除、quick-save、清空与预取消 token；使用反射测试锁定接口不再暴露同步或不可取消 I/O 方法。
