---
feature: F-20260929-11-game-host-presentation-split
status: implementing
updated: 2026-09-29
---

# 实施计划

## Phase 1 - 行为保护与展示边界

**状态：verified**

**目标：** 固定当前 dispatcher、展示组合和资源一致性语义，并建立拆分后的目标接口。

**任务：** 补充 `CompositeGameView` dispose/dispatch 边界、Avalonia dispatcher 和 `AssetManager` 并发 acquire/cancel/release 测试；记录 Sample 生命周期中可直接覆盖的 prepared/stop/replay 行为。

**验证：** Presentation、Assets、Integration 测试；Avalonia GameView 与 Sample build。

**退出条件：** 高风险行为有可重复断言，无生产行为变化，创建独立 Git 提交。

**验证（2026-09-29）：** 新增 `CompositeGameView` 逆序释放、异常聚合与 disposed dispatch 测试；新增 `AssetManager` 并发单飞、等待者局部取消、引用释放和 in-flight dispose 测试。Assets 32/32、Presentation 66/66、Integration 29/29 通过，Sample Avalonia Release 构建 0 错误。测试确认 `AssetManager` 的缓存、取消和释放仍属于同一一致性边界，本 feature 不拆其生产实现。

## Phase 2 - Avalonia presenter 拆分

**状态：verified**

**目标：** 让对话/选择、图层、动画与粒子展示可独立定位和验证。

**任务：** 引入共享 dispatcher adapter；迁移四个 presenter；让 `AvaloniaGamePageView` 只组合并暴露 presenter；迁移 Sample 与 Editor Preview 组合根。

**验证：** presenter 单元测试、Presentation 与 Integration 测试、Editor Preview/Editor Headless/Sample build。

**退出条件：** PageView 不再实现细分 presenter 接口，动画/粒子释放和 initial-presentation 行为保持，创建独立 Git 提交。

**验证（2026-09-29）：** 新增共享 `IAvaloniaUiDispatcher`，对话/选择、图层、动画和粒子分别由独立 presenter 实现；`AvaloniaGamePageView` 从约 568 行缩为 60 行组合器。Sample 与 Editor Preview 显式组合 presenter，持久场景 replay 使用对应端口。新增组合、dispatcher 使用和动画最终值测试；Presentation 69/69、Builtins 40/40、Integration 29/29 通过，Editor 与 Sample Avalonia Release 构建 0 错误。

## Phase 3 - Sample 资源与存档协作者

**状态：planned**

**目标：** 从 session 移出安装资源和玩家持久化的生命周期细节。

**任务：** 引入 `SampleGameResourceScope` 与 `SampleSaveSession`；迁移资源加载、Gallery、槽位刷新、save/load 与 clear player state；确保失败初始化和 reload 可完整释放。

**验证：** 协作者测试、Integration 与 Storage/Assets 测试、Sample smoke/build。

**退出条件：** Session 不再直接拥有 asset handles、file save/variable/progress 服务或 Gallery resolver 释放细节，创建独立 Git 提交。

## Phase 4 - Sample runner 与持久场景恢复

**状态：planned**

**目标：** 将 engine run lifecycle 与读档展示重放从页面会话命令中分离。

**任务：** 引入 `SampleGameRunner` 与 `PersistentSceneRestorer`；迁移 prepare/start/advance/stop/dispose 和 replay；保持导航前停止、opening presentation 和 UI 状态时序。

**验证：** runner/restorer 测试、Runtime replay、Builtins、Integration、Sample smoke/build。

**退出条件：** Session 主要负责公开命令与生命周期串行化，runner/restorer 可独立测试，创建独立 Git 提交。

## Phase 5 - 全量验证与收尾

**状态：planned**

**目标：** 同步稳定架构事实和路线图状态，记录计划偏差与剩余风险。

**验证：** 全部 10 个测试项目、Editor Headless、Sample Headless、Sample Avalonia 和完整 solution Release build；diff check。

**退出条件：** Phase 4b 有完整证据、feature summary 与正式文档，创建独立 Git 提交。
