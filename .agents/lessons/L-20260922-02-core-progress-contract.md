# 平台无关的玩家进度契约应归属 Core

状态：superseded
来源 Feature：F-20260922-01-entry-instance-runtime
取代依据：ADR-0001、F-20260929-07-runtime-port-cleanup

## 现象

`IGameProgressService` 的命名空间为 `GalNet.Core.Services`，但源文件和程序集实际属于 `GalNet.Storage.Abstractions`。因此只引用 Core 的 `GalNet.Primitives.Builtins` 无法实现平台无关的 `gallery.unlock`，即使该功能只需要玩家进度抽象。

## 根因

接口按当前文件夹归属，而不是按依赖方向归属；其 API 只使用 Core gallery 类型，也被 Runtime 用于阅读进度，没有存储实现专属概念。

## 如何发现

将 gallery 内置模块改为依赖 `IGameProgressService` 后，Builtins 编译报 CS0246；检查接口源项目和 Runtime 引用后确认接口不属于 Storage.Abstractions。

## 正确做法

本节原结论已失效。当前规则是按实际消费者拥有端口：`IGameProgressService` 由 Runtime 的 `GameEngine` 消费，因此位于 `GalNet.Runtime.Progress`；具体文件实现仍位于 Storage.FileSystem。Core 不应仅因协议平台无关就承担没有 Core 消费者的宿主端口。

## 适用范围

玩家进度、解锁状态、阅读记录及未来由内置 primitive 或 Runtime 使用的跨平台服务。

## 不要做什么

不要以命名空间名称掩盖程序集依赖，也不要为了使用一个 Core 契约让 Builtins 反向引用 Storage.Abstractions。

## 证据和关联文档

- `src/GalNet.Core/Services/IGameProgressService.cs`
- `src/GalNet.Primitives.Builtins/BuiltinEntryModules.cs`
- `features/F-20260922-01-entry-instance-runtime/design.md`
