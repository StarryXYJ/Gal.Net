---
feature: F-20260929-07-runtime-port-cleanup
status: done
updated: 2026-09-29
---

# Runtime 端口与保存 API 清理：实现总结

## 结果

维护性路线图 Phase 2a 已完成。Core 中历史宿主接口已按真实消费者归位或删除，保存服务只保留一套异步、可取消、基于 `SaveRequest` 的 API。

## 端口调整

- `IGameProgressService` 移入 `GalNet.Runtime.Progress`。
- `IGameExitService`、`ISettingsService` 移入 `GalNet.Editor.Abstraction.Services`。
- 删除无消费者的 `IGameDataProvider`、`INavigationHost`、`IInputService`、Core `KeyGesture`、`IGameSession` 与 `DefaultGameSession`。
- `ITextResolver` 保留在 Core，因为 Core 的 `IGameRuntime` 直接暴露该协议。
- `IAudioService` 保留给独立音频 feature 决定。

## 保存 API

- 删除同步 `ListSlots()` 和直接接受 `GameSnapshot` 的保存重载。
- Load、Delete、QuickLoad 等方法增加并向文件 I/O 传播 `CancellationToken`。
- `OperationCanceledException` 不再被损坏存档容错逻辑吞掉。
- Editor、Sample、Headless 调用方统一构造 `SaveRequest`。

## 验证

- FileSaveService 定向测试：6/6 通过。
- GeneralTest Release：275/275 通过。
- GalNet.Assets.Tests Release：34/34 通过。
- Editor Headless、Sample Headless：构建成功。
- 完整 Android、Browser、Desktop、iOS solution：0 错误；保留 1 条既有 WebAssembly native reference 警告。
- 搜索确认不存在已删除接口、DefaultGameSession 或旧保存重载引用。
