# 测试套件按生产边界重组设计

## 当前问题

`GeneralTest` 直接引用 Core、Runtime、Builtins、Storage、Editor、Sample 和全部 Avalonia 展示项目。任何内层改动都会编译整套 UI，测试名称也无法表达真实依赖。`GalNet.Assets.Tests` 同时包含纯 Assets、Storage 与工程导出安装集成测试，边界同样失真。

## 目标项目

| 项目 | 主要职责 |
| --- | --- |
| `GalNet.Architecture.Tests` | 项目引用、命名空间和仓库结构门禁 |
| `GalNet.Core.Tests` | Core 领域模型、schema、scene、graph、变量与文本逻辑 |
| `GalNet.Runtime.Tests` | Runtime 状态、loader、表达式、音频队列和保存序列化 |
| `GalNet.Primitives.Builtins.Tests` | 推荐 entry module 与 primitive 行为 |
| `GalNet.Assets.Tests` | Assets provider、压缩与加密 |
| `GalNet.Storage.FileSystem.Tests` | 文件保存、变量与进度实现 |
| `GalNet.Editor.Shared.Tests` | Editor 协议、命令、文档与持久化协作者 |
| `GalNet.Editor.Tests` | Editor UI/ViewModel/交互逻辑 |
| `GalNet.Presentation.Tests` | Presentation.Abstractions 与 Avalonia 展示/渲染逻辑 |
| `GalNet.IntegrationTests` | 跨 Assets/Storage/Editor/Runtime/Sample 的端到端场景 |

测试项目可以引用被测模块的必要直接协作者，但不通过 ProjectReference 引用其他测试项目。需要多个外层实现共同工作的场景进入 Integration，不伪装成某个内层模块的单元测试。

## 共享配置

`test/Directory.Build.props` 统一 `IsPackable=false`、测试 SDK、NUnit、adapter、analyzers、coverage collector 和全局 NUnit using。各 csproj 只声明 TargetFramework 与生产 ProjectReference，减少重复配置。

## CI

CI 先 locked restore 各测试项目，再分三组执行：纯逻辑与架构、Presentation/Editor UI、Integration。每个项目生成独立 trx 和 coverage 文件，避免一个聚合项目隐藏编译依赖。

## 迁移与风险

- 移动测试文件时保持 namespace 和测试名不变，先以迁移前 `277 + 34 = 311` 项作为数量基线。
- 每一批拆分后独立 restore/build/test；lock file 由实际项目引用生成。
- 若某测试暴露未声明的跨模块依赖，优先调整归属到 Integration，而不是给内层测试项目追加整个 UI/Sample 依赖图。
