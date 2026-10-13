# 实现总结

本 feature 实现了无 UI 的项目操作层与面向 agent 的单次调用 CLI，并完成独立项目 schema 与平台执行的分离。不提供旧 CLI 别名、默认全内置 catalog 或旧格式迁移。

## 结果

- GalNet.Contracts.Builtins 只依赖 Core；13 个独立模块共 42 个 entry，汇总方法仅返回模块列表。
- 每个内容项目部署 Contracts/GalNet.Project.Contracts.dll，公开 ProjectSchemas.Modules。游戏项目引用同一独立契约项目，CLI/Editor 隔离加载；完整 typeId 排他，模块名可重复。缺契约明确失败，原始查询与结构校验仍可用。
- Shared 负责 files/find/references、Graph/Group/entry/变量查询、原始校验、单 Group 编译、build/export 和严格编辑/保存。CLI 负责 help、参数、JSON 协议和退出码；Editor 复用服务、typed 单元命令及项目 scope catalog。
- 项目创建支持已构建契约目录或缺契约空骨架；显示名改名保持目录与身份。Group 创建/改名/删除同步 Graph 和 Raw 文件；dry-run 不写，引用候选删除需显式 --force。
- 作者保存拒绝未知字段/参数、重复属性、缺 ID 和无法无损转换的值，保留空字符串；外部修改拒绝覆盖，多文件失败恢复已有内容。
- 整项目删除使用 Windows 外层回收站适配，无永久删除 fallback。Editor 先关闭匹配会话并等待资源清理，再回收，成功后更新最近记录。
- Sample 自定义 sample.screenshot(fileName?) 契约无 Avalonia 依赖；平台模块直接借用 GamePage，保存场景 PNG 到 Pictures/<Graph 显示名>。Normal/Quick 截图，CatchUp 无文件副作用；异步取消、冲突、不安全目录及生命周期已覆盖。

## 提交与验证

本轮继续实施的阶段提交为 5544c72（项目/Group 生命周期）、5777aec（回收站）、3ee9ece（截图）；此前 Phase 1–5b1 提交及证据保留在 phase-plan.md。Phase 7 包含审核修复、最终回归和正式文档同步。

阶段验证包含真实 CLI 自定义 Composite 的 create→execute→validate→compile、相同契约 build/export、Editor 两项目切换/关闭、目标执行覆盖。Windows 实物验收仅使用新建临时项目：系统回收站可见、恢复成功、5 个文件 SHA-256 相同。截图实物为 640×360 PNG，角点和中心像素断言通过并已打开视觉检查；实际 CLI 截图契约查询→校验→编译→build→export 成功，Headless 返回 1 并报告 sample.screenshot 缺执行实现。

最终回归：共享层 162/162、Editor 35/35、Runtime 80/80、Builtins 64/64、Architecture 30/30、Integration 101/101，共 472 项；最后保留原资源 ID 拼写的修改追加查询/校验 33/33 针对性通过。本 feature 文件的 whitespace 与 IDE0005/import 门禁通过，导入排序修正后消费者重新编译通过。GameTestCase 已部署生成契约并真实 validate 成功（零诊断），find 返回实际 metadata 的原始 GUID 与 /id 位置。生成 DLL 被 .gitignore 排除，现有示例说明保留并补充契约部署步骤。测试基于当前工作树，其他任务的变量持久化与国际化改动保留未提交，不混入本 feature 提交。

## 审核与计划偏差

[review.md](review.md) 记录审核发现及修正。增加 Windows 外层项目以保持 Shared/Core/Runtime 平台无关；截图发现普通 Runtime 未传播已完成异步展示失败，补齐既有 Faulted 路径。预览使用确定性安全文件名保留原始 Group ID，避免把 ID 当文件路径。资源 metadata 读取统一对齐实际 AssetMetaCodec，fixture 由实际 codec 生成，查询返回原始 ID 拼写和真实 pointer，不迁移作者数据。严格 JSON 解析与帮助 schema 共用设置。

## 使用与文档

- [CLI README](../../src/Editor/GalNet.Editor.Headless/README.md)：完整命令及协议，help --json 为当前机器事实源。
- [Sample.Contracts](../../src/Samples/GalNet.Sample.Contracts/README.md)：构建部署、静态模块组合及单平台截图示例。
- [架构](../../docs/spec/architecture.md)、[Entry](../../docs/spec/entry-types.md)、[文件格式](../../docs/spec/file-formats.md)、[开发手册](../../docs/development.md)、[ADR-0004](../../docs/adr/0004-project-entry-contract-assembly.md)：已验证事实。

## 边界与后续

未增加 UI 布局、操作按钮或契约目录选择；Editor CreateAsync 要求明确契约目录，当前推荐 CLI 创建再打开。Editor preview 不能自动获得外部自定义平台实现，缺实现明确拒绝。原始查询保留未知 entry，类型化编辑拒绝有损保存。引用检查是静态 JSON 候选、complete=false；--force 不修复任意 C# 引用。保存不承诺崩溃或跨进程整体原子性。Windows 回收仅支持本地固定磁盘，CLI 不关闭另一进程中的 Editor。契约是可信项目代码，不是沙箱；无 schema 热重载/常驻服务。

既有主题 Headless 测试需单独审核 async Dispatch 重载：12.0.5 无无结果 Func<Task> 重载，async void 可提前结束。本 feature 新测试全部显式等待 Dispatch<bool>，实物 PNG 独立证明确实完成；未把既有主题测试绿色结果计为本次截图证据。
