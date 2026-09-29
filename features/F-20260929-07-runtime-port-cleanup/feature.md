---
id: F-20260929-07-runtime-port-cleanup
title: Runtime 端口与保存 API 清理
type: refactor
status: done
created: 2026-09-29
updated: 2026-09-29
parent: F-20260929-03-maintainability-roadmap
---

# Runtime 端口与保存 API 清理

## 目标

完成路线图 Phase 2a 剩余工作：删除 Core 中无消费者的历史宿主接口，将真实 Runtime/Editor 端口归入消费者边界，并把 `ISaveService` 收敛为一套异步、可取消、基于 `SaveRequest` 的 API。

## 范围

- `IGameProgressService` 迁入 `GalNet.Runtime.Progress`。
- `IGameExitService`、`ISettingsService` 迁入 `GalNet.Editor.Abstraction.Services`。
- 删除无消费者的 `IGameDataProvider`、`INavigationHost`、`IInputService`、Core `KeyGesture`、`IGameSession` 与未使用的 `DefaultGameSession`。
- 保留 `ITextResolver` 在 Core：Core 的 `IGameRuntime` 直接消费该协议。
- 删除 `ISaveService` 的同步和 `GameSnapshot` 便捷重载；所有 I/O 方法接收 `CancellationToken`。
- 更新 FileSystem、Editor、Sample、Headless 与测试消费者。

## 非目标

- 不改变保存文件格式、槽位编号、损坏存档容错或 quick-save 语义。
- 不处理 `IAudioService`，不迁移 Presentation/Builtins 命名空间。
- 不重构 Editor 设置实现或游戏会话 UI。

## 验收标准

- Core Services 只保留仍由 Core 契约实际消费或另有 feature 所有权的接口。
- `ISaveService` 每个 I/O API 都是异步可取消的，保存只接受 `SaveRequest`。
- 保存 metadata、preview、读取、删除、quick-save 与取消测试通过。
- 全部测试、Headless 和完整 solution 构建通过。

## 相关链接

- [实施计划](phase-plan.md)
- [实现总结](summary.md)
- [ADR-0001](../../docs/adr/0001-runtime-storage-contract-ownership.md)
- [路线图 Phase 2a](../F-20260929-03-maintainability-roadmap/phase-plan.md)
