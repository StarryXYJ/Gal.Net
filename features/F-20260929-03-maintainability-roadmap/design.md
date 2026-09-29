---
feature: F-20260929-03-maintainability-roadmap
status: implemented
updated: 2026-09-29
---

# 设计

## 设计目标

本路线图不追求更细的程序集数量，而追求四个可以持续验证的属性：

1. 一个公共类型的程序集、命名空间和文档归属一致。
2. 内层模块定义策略和端口，外层模块提供 UI、媒体、文件系统等实现。
3. 单元测试只引用被测模块的必要依赖，跨模块行为显式进入集成测试。
4. 协调类保留编排职责，把状态机、资源生命周期和领域编辑操作交给可独立测试的协作者。

## 目标职责模型

```text
GalNet.Core
  领域值对象、Graph、Entry 基础协议、Scene、Snapshot、资源/Gallery 身份模型
  不依赖 UI、DI、文件系统、媒体库或其他 GalNet 程序集

GalNet.Presentation.Abstractions -> Core
  游戏展示与交互端口及请求 DTO

GalNet.Primitives.Builtins -> Core + Presentation.Abstractions
  官方内置 entry schema、primitive instance 与展示桥接

GalNet.Runtime -> Core + Presentation.Abstractions
  引擎、运行时状态、编译内容加载协调、运行时持久化端口

GalNet.Assets -> Core
  Archive、Provider、AssetManager 与 decoder 生命周期

GalNet.Storage.FileSystem -> Core + Runtime + Assets
  内容目录、安装包、存档、玩家变量和进度的默认磁盘实现

GalNet.Editor.Abstraction -> Core
  编辑器文档、命令、扩展协议；不引用具体 Editor UI

GalNet.Editor.Shared -> Editor.Abstraction + Core + Runtime + Assets
  项目读写、编译、命令处理、导出等无 Avalonia 实现

GalNet.Avalonia.* / Editor / Samples / Launcher
  UI、媒体、导航、组合根和产品宿主
```

上图是目标约束，不表示必须新增更多项目。应优先把契约放回已有消费者程序集；只有外部扩展确实需要“引用契约但不引用实现”时才增加新的 Abstractions 程序集。

## 契约归属方案

### 候选方案

**方案 A：保留 `Storage.Abstractions`。**

优点是迁移最少，也延续旧设计文档；缺点是 Runtime 永久依赖名为 Storage 的横向程序集，资源、Gallery、存档和变量继续混在同一边界，无法解决命名与职责错位。

**方案 B：按当前命名空间把所有契约并入 Core。**

优点是依赖图简单；缺点是 Core 会继续吸收存档、宿主和 UI 服务，成为通用协议仓库。

**方案 C：契约由策略消费者拥有，并删除 `Storage.Abstractions`。**

- `IGameContentProvider`、`ISaveService`、`IPlayerVariableStore`、`IVariableService` 归 Runtime 的 host/persistence ports。
- 资源文件、Archive、Provider、Manager 契约保留在 Core.Assets，或在证明有独立发布价值时再建立 `Assets.Abstractions`。
- `GalleryDataSource` 等纯 Gallery 契约归 Core.Gallery。
- 文件系统和 PAK 类型留在 `Storage.FileSystem`/`Assets` 实现层。

推荐以方案 C 编写 ADR，但在 ADR 接受前不移动类型。它能减少一个职责含混的程序集，并让 Runtime 的声明依赖与真实依赖重新一致。

## 命名空间与公共 API

- `GalNet.Presentation.Abstractions` 的公开类型迁到 `GalNet.Presentation.*`。
- `GalNet.Primitives.Builtins` 的内置 schema 和实现迁到 `GalNet.Primitives.Builtins.*`；Core 只保留扩展协议和基础类型。
- 运行时持久化端口使用 `GalNet.Runtime.*`；具体文件实现使用 `GalNet.Storage.FileSystem.*`。
- 一次迁移仓库内消费者、测试、示例与文档，不长期保留两套 using。
- 若已有外部消费者需要兼容，优先发布一个带 `[Obsolete]` 的过渡版本；当前仓库未证明该需求，不预先增加类型转发复杂度。

Core 服务清理按“有消费者且属于 Core 语义”判断：

- `IGameProgressService` 被运行时与内置原语消费，继续位于平台无关层。
- `IGameDataProvider` 若已被 `IGameContentProvider` 替代则删除。
- `INavigationHost`、`IInputService` 等 UI 宿主契约无有效消费者时删除；有消费者时迁到 Presentation 或具体宿主。
- `IAudioService` 的归属由音频 feature 统一决定，本路线图只防止继续扩散旧接口。
- `ISaveService` 收敛为一套带 `CancellationToken`、`SaveRequest` 和异步查询的 API；旧重载在迁移 feature 中删除或短期 obsolete。

## 测试与质量门禁

先修复基线，再拆测试项目：

```text
GalNet.Core.Tests
GalNet.Runtime.Tests
GalNet.Primitives.Builtins.Tests
GalNet.Assets.Tests
GalNet.Storage.FileSystem.Tests
GalNet.Editor.Shared.Tests
GalNet.Avalonia.Rendering.Tests
GalNet.Avalonia.GameView.Tests
GalNet.IntegrationTests
```

不要求每个生产程序集机械对应一个测试程序集。只有测试数量、依赖或运行环境足以形成独立边界时才拆分；小型纯契约项目由消费者测试覆盖。`IntegrationTests` 才允许同时引用 Editor、Runtime、Storage 和 Sample 组合根。

增加架构测试或 MSBuild 图检查，至少验证：

- Core 不引用其他 GalNet 项目和平台包。
- Runtime 不引用 Avalonia、Editor、FileSystem 或媒体实现。
- Editor.Shared 不引用 Avalonia。
- Abstraction 项目不引用实现项目。
- 测试项目的引用集合符合其类别。

CI 在 pull request、主分支 push 和手动触发时执行 restore/build/test；格式校验和架构测试成为独立步骤。平台 workload 可与普通 .NET/Desktop 验证分 job，避免 Android/iOS 环境问题遮蔽核心失败。

仓库级 `.editorconfig` 统一换行、缩进、using、命名和分析器严重级别。测试方法采用行为式下划线命名时，应只在测试目录显式禁用 `CA1707`，而不是积累告警或全局关闭规则。

## 职责热点拆分

### Editor 工作区

`EditorWorkspaceViewModel` 最终只保留 UI 状态投影和命令协调。候选协作者：

- `GraphSelectionController`：单选、多选、边和资源焦点。
- `GraphWorkspaceEditor`：节点、边、分支项和 entry 操作。
- `WorkspaceHistoryCoordinator`：undo/redo、属性快照和 change tracking。
- `WorkspacePersistenceCoordinator`：文档映射、保存调度和 dirty 状态。

现有 `BuiltInEditorCommandHandler` 已按 Graph/Entry/Variable/Project 分域；先把各 domain 实现移到独立 handler/file，再决定是否需要新的公共抽象。

### Sample 会话

`SampleGameSessionService` 保留页面可见的会话命令，拆出：

- `SampleGameResourceScope`：provider、asset handle、媒体和 effect 生命周期。
- `SampleGameRunner`：engine 创建、运行、停止和 prepared run。
- `SampleSaveSession`：槽位刷新、保存、读取和玩家状态重置。
- `PersistentSceneRestorer`：读档后的展示重放。

所有具体类型仍在 Sample 组合根，不把示例宿主策略推回 Runtime。

### Avalonia 展示

利用现有 `CompositeGameView`，把 `AvaloniaGamePageView` 内的对话/选择、图层、动画计划和粒子 presenter 拆为独立对象。共享 UI 线程调度通过一个小型 dispatcher adapter 提供，避免每个 presenter 复制线程切换代码。

`AssetManager` 虽然文件较大，但加载去重、缓存、引用计数和释放属于同一一致性边界。除非测试证明可以保持原子性，否则不按行数拆类；优先补取消、并发 acquire/release 和 dispose 竞态测试。

## 项目和物理目录

目录移动放在 API 与测试边界稳定之后，目标是让物理路径匹配 `GalNet.slnx` 的逻辑分组：

```text
src/
  Shared/          Core, Runtime, Primitives.Builtins
  Presentation/    Presentation.*, Avalonia.*
  Infrastructure/  Assets, Storage.FileSystem
  Editor/          Editor.Abstraction, Editor.Shared, Editor, Editor.Headless
  Samples/         Sample.Avalonia, Sample.Headless
  Launcher/        Launcher shared app and platform heads
```

是否保留 `Shared` 这一名称可在移动 feature 中调整；重要的是不要同时修改命名空间、行为和物理路径。SDK 风格项目通常不因目录移动改变程序集名，因此应把纯移动做成独立、可审查提交。

## 依赖与文档治理

- 删除确认未使用的 `CommunityToolkit.Mvvm` 等包引用。
- 将 `Microsoft.Extensions.DependencyInjection` 从 preview 版升级到与目标框架匹配的稳定版，单独验证锁文件变化。
- `ImplicitUsings`、`Nullable` 等共同属性只在 `Directory.Build.props` 定义，项目文件只保留例外。
- README 增加仓库结构、构建、测试和 feature 工作流入口。
- 当前事实写入 `docs/spec`；仍在讨论的边界写入 design/ADR；旧路径只在历史 feature 中保留。
- `.agents/agent-knowledge.md` 更新为仓库相对路径或当前有效路径，不修改历史证据文件中的原始路径。

## 风险与缓解

- **公共 API 大面积改名：** 先建立架构测试和引用清单，再按程序集逐个迁移。
- **并行 feature 冲突：** 粒子、编译管线和音频相关文件稳定后再迁移对应 namespace；热点拆分不与功能 feature 并行编辑同一文件。
- **纯重构引入行为变化：** 每个切片先补 characterization tests，移动与语义调整分开。
- **测试项目过度碎片化：** 以依赖和运行环境为拆分依据，不追求一项目一测试程序集。
- **目录移动造成历史噪声：** 最后执行纯移动，并在移动前确保工作树干净。

## ADR 候选

实施前至少提出一个 ADR：运行时/存储端口归属与 `GalNet.Storage.Abstractions` 的去留。若 Editor 插件 API 将从 `IServiceProvider + object` 改为强类型上下文，再单独提出一个 ADR；其余拆分类重构不需要 ADR。
