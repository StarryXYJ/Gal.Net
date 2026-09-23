# 平台无关的玩家进度契约应归属 Core

状态：candidate
来源 Feature：F-20260922-01-entry-instance-runtime

## 现象

`IGameProgressService` 的命名空间为 `GalNet.Core.Services`，但源文件和程序集实际属于 `GalNet.Storage.Abstractions`。因此只引用 Core 的 `GalNet.Primitives.Builtins` 无法实现平台无关的 `gallery.unlock`，即使该功能只需要玩家进度抽象。

## 根因

接口按当前文件夹归属，而不是按依赖方向归属；其 API 只使用 Core gallery 类型，也被 Runtime 用于阅读进度，没有存储实现专属概念。

## 如何发现

将 gallery 内置模块改为依赖 `IGameProgressService` 后，Builtins 编译报 CS0246；检查接口源项目和 Runtime 引用后确认接口不属于 Storage.Abstractions。

## 正确做法

凡是由 Core、Runtime 或仅引用 Core 的 Builtins 消费，且 API 不泄漏文件、数据库或平台类型的进度契约，都应定义在 Core。具体文件存储服务在 Storage.FileSystem 等外层实现该契约；即使 Runtime 仍因变量服务等其他接口引用 Storage.Abstractions，也不应把进度契约留在其中。

## 适用范围

玩家进度、解锁状态、阅读记录及未来由内置 primitive 或 Runtime 使用的跨平台服务。

## 不要做什么

不要以命名空间名称掩盖程序集依赖，也不要为了使用一个 Core 契约让 Builtins 反向引用 Storage.Abstractions。

## 证据和关联文档

- `src/GalNet.Core/Services/IGameProgressService.cs`
- `src/GalNet.Primitives.Builtins/BuiltinEntryModules.cs`
- `features/F-20260922-01-entry-instance-runtime/design.md`
