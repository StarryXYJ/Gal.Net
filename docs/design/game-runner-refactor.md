# 游戏运行编排与 Avalonia 呈现收敛

> 后续宿主分层已由 [game-host-refactor](game-host-refactor.md) 实现；本文保留早期收敛设计，当前类型和归属以架构规范为准。

## 范围

保留页面、presenter 和运行会话的职责边界。不改变 Editor/Launcher 功能；Editor 仅迁移共享类型名称和事件订阅 API。

## 设计

- 不新增通用 Runner。Engine 统一报告运行、完成、失败和停止，覆盖玩家推进、选择结果和阻塞实例自然完成三个入口。
- Engine 提供 `StopAsync` 和异步释放，停止先拒绝新请求并取消 scope，再清理活动实例、等待内部 continuation 退出。停止是 Engine 的终态，重开创建新实例。
- 状态事件在执行锁外按发生顺序发布；订阅者异常诊断但不改变剧情状态。事件只报告事实，不承担 UI、日志文案或存储业务。
- 准备、页面切换、首次推进留在宿主；内置场景重放继续复用 Builtins 入口。
- `AvaloniaGamePageView` 改名为 `AvaloniaGamePresenters`。玩家推进直接从页面 ViewModel 接到宿主，不经过 presenter 集合转发。
- Sample 的平台对象装配和统一清理集中到一个 `SampleGamePresentation`；执行模块先于借用的 presenter/后端释放。清理一个对象失败不能阻止其他对象清理。
- 删除未使用的 `SampleMediaViews`，Sample Runner 只保留平台绑定、日志和 UI 更新。

## 验证阶段

1. **verified**：Engine 停止、状态通知与生命周期测试，新增 11 个用例。
2. **verified**：Avalonia 命名/API 迁移、Sample 装配与事件绑定、旧代码清理。
3. **verified**：全仓 10 个测试项目共 384 项通过；另外 2 项原生音频取消测试通过。Sample.Headless 和 Editor.Headless 构建通过，solution whitespace / IDE0005 门禁及 git diff --check 通过。

当前状态：已实现并验证。

## 结果和限制

- 平台实现只提供呈现端口，无需继承 Runner 基类、管理 Engine continuation 或重复判断剧情终态。
- SampleGameRunner 保留页面准备/切换、日志和 UI 反馈；平台对象由 SampleGamePresentation 统一装配与释放。删除旧媒体占位对象和推进事件的两层转发。
- 放弃最初的通用 GameRunner 草案，职责直接归 Engine；不兼容旧 AvaloniaGamePageView 类型名。
- Editor 仅迁移共享 API，原预览编排未重构；Launcher 未改动。
- 常规测试不包含显式视频原生测试，本次未进行完整 GUI 操作验收。Avalonia 构建有既有 AVLN3001 警告；沙箱许可目录读取限制通过授权验证处理，没有修改产品代码绕过。
