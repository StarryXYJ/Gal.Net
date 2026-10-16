# ADR-0006：游戏应用流程与 Avalonia 默认宿主分层

状态：Accepted（2026-10-16，用户确认重构范围后实施）

## 上下文

Sample 已承载可复用的运行替换、存档协调、平台资源释放、设置和输入实现。新客户端若复制这些代码，会重复承担取消、恢复和清理的正确性责任。将这些能力全部放入 Runtime 或 Avalonia 页面程序集，会分别引入宿主策略或扩大页面层依赖。

## 决定

- Core/Runtime 继续提供调度、编译、指令执行与平台无关状态机制；指令通过独立模块注册。通用宿主不隐式加入 Builtins，也不要求 Layer Presenter。
- 新增平台无关 GameHost，负责应用命令互斥、可替换运行生命周期、玩家持久化与偏好规则。Engine 仍独占剧情执行与执行 scope，不创建第二套推进器。
- Presentation.Abstractions.Application 承载会话、显示能力及 Gallery 路径等无 UI 框架类型的契约。
- Avalonia.GameHost 提供默认页面会话、运行绑定、展示组合、纹理租约、设置与平台输入/窗口适配。项目通过配置选择 catalog、执行模块及可替换工厂。
- 文件资源与玩家存储组合留在 Storage.FileSystem；无 Avalonia 依赖的 LibVLC 音频实现独立为 Media.LibVlc。视频画面接入留在 Avalonia。
- Sample 保留项目契约、启动参数、本地化配置和调试策略。Editor 保留创作工作区和自身文档生命周期，本轮不接入游戏预览页面。
- 本轮不移动 Core 内现有场景模型，也不实现 WPF/WinUI/Unity。未来平台通过独立模块或 Presenter 接口接入，不要求复用 Avalonia 的渲染对象。

## 候选与取舍

继续放在 Sample 的成本最低，但每个宿主都需要复制生命周期代码；全部放入 Runtime 会混入应用策略；全部放入 GameView 会迫使页面组件依赖完整运行和内置模块。独立宿主层增加少量程序集与配置入口，换取跨 UI 框架的会话复用和轻量 Sample。

迁移会改变应用接口与音频实现的命名空间；调用点一次性迁移，不保留重复实现。默认 Avalonia 宿主可以依赖具体文件和媒体适配，GameHost 与 Runtime 的依赖边界由架构测试约束。

## 所有权与验证

运行先停止 Engine，再等待恢复与 view 清理，然后释放后端和资源。显式注入的恢复会话由调用者拥有；GameRun 自动创建的恢复会话由自身释放。工厂返回的新 Layer/音频适配由展示会话拥有，输入资源租约和共享 LibVLC 运行时仍由外层拥有。

自定义指令在不注册 Builtins/Layer 的情况下运行、命令互斥、失败清理、工厂释放与非法偏好回退均有回归测试。当前接口与依赖图见 [架构规范](../spec/architecture.md)，分步实现与验证见 [重构记录](../design/game-host-refactor.md)。
