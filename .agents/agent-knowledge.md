# GalNet Agent Knowledge

这是 GalNet 项目的长期 agent 上下文入口。这里只保存经过验证、以后工作仍然有用的项目事实、边界和经验索引；不要写入对当前任务没有复用价值的对话流水账、秘密或未经确认的猜测。

## 使用方式

开始工作时先读取本文件，再按当前任务读取仓库 `.agents/lessons/` 中相关的经验和 `.agents/skills/` 中匹配的 skill。细节以仓库 `docs/` 下的正式文档为准。

经验在发现时立即记录，feature 结束时再整理和晋升；不要等到上下文结束才回忆。

## 项目事实（已从仓库文档确认）

- 解决方案入口是 `GalNet.slnx`。
- `docs/spec/` 描述已经实现的稳定事实；`docs/design/` 描述设计和分阶段计划。
- Runtime/Core 应保持与 Avalonia、WPF、Skia、具体文件系统和其他平台实现解耦。
- 旧的 `GalNet.Control` 与 `GalNet.Control.Abstraction` 已完成迁移并删除；当前共享 Avalonia 游戏页面唯一入口是 `GalNet.Avalonia.GameView`，页面架构见 `docs/spec/control.md`。
- 会影响场景的运行时条目应先更新 `SceneState`，再通知展示层；读取存档后由 Engine 根据状态重放展示。
- 视觉扩展应通过展示抽象和渲染端口接入；不要把平台类型或渲染资源泄漏进 Core/Runtime。
- 当前已有大型阶段计划：[runtime-presentation-decoupling-phase-plan.md](../docs/design/runtime-presentation-decoupling-phase-plan.md) 和 [render-effects-pipeline-plan.md](../docs/design/render-effects-pipeline-plan.md)。新增 feature 应链接它们，不要复制其中的长期设计。

## 文档分层

- `features/<feature-id>/`：一次实现的需求、设计、阶段计划、审核和人类可读总结。
- `.agents/lessons/`：agent 专用的踩坑记录。
- `.agents/skills/`：项目 skill 的可版本化源文件；可复用经验晋升后在这里形成独立 skill。
- Codex 的用户级 skill 发现目录保存运行时副本；源文件变化后使用 `galnet-sync-skills` 同步，避免 agent 使用旧版本。
- `docs/spec/`：当前系统事实。
- `docs/design/`：跨 feature 设计和推荐工作流。
- `docs/adr/`：重要架构决定；接受后的 ADR 不直接改写。

## 当前工作

- `F-20260926-01-gallery-meta-pages` 的实现、正式文档与示例 Gallery 解锁 smoke test 已完成；其外部阻塞项 `EditorSettingsSerializationTests.LastDockLayout_RoundTripsAsAString` 已由 `F-20260929-04-quality-baseline` 修复，Gallery feature 可按自身流程收尾。
- 当前 discovery feature：[F-20260916-02-audio-system](../features/F-20260916-02-audio-system/feature.md)，用于澄清音频系统的首个可交付范围、后端能力和迁移语义。
- `F-20260929-01-compiled-content-pipeline` 正在实施；粒子 Primitive、Runtime 状态与 Presenter 桥接及存档后的 effect/particle 展示重放已经验证，完整 Sample/Headless smoke 与 Phase 4 收尾仍待完成。
- `F-20260929-03-maintainability-roadmap` 已完成并以 `pass-with-follow-up` 通过独立审核。资源协议归 Core.Assets；内容、保存、变量、Gallery 组合和进度端口归 Runtime；Editor 设置/退出协议归 Editor.Abstraction；展示端口使用 `GalNet.Presentation.Abstractions.*`，推荐内置类型使用 `GalNet.Primitives.Builtins`。`GalNet.Storage.Abstractions` 与无消费者历史接口已删除，`ISaveService` 已统一为异步可取消 API。测试已按 10 个生产/运行边界拆分，跨外层场景集中在 `GalNet.IntegrationTests`，Architecture 门禁同时约束生产与测试项目引用。Editor Workspace、Avalonia presenter、Sample 会话和 Editor 扩展边界均已按路线图收敛。21 个生产项目的物理路径现按 Shared、Presentation、Infrastructure、Editor、Samples 和 Launcher 分组。唯一明确后续项是 `F-20260929-14-format-baseline` 的机械格式债务与 CI 门禁。
- 后续 feature、验证命令和新经验在实际工作中补充，并保留来源链接或 feature ID。

## 维护规则

1. 只写已经观察到或从正式文档确认的事实。
2. 事实变化时更新本文件的对应条目，并注明来源。
3. 一次性事故先写 lesson；只有可复用、重复出现或高风险的规则才晋升成独立 skill。
4. 如果项目架构变化，检查并标记受影响的 lesson 和 skill，而不是继续沿用旧规则。

## 经验索引

- [受限环境中的 .NET/Avalonia 构建](lessons/L-20260922-01-dotnet-build-sandbox.md)：禁用 Roslyn shared compilation，并关闭 Avalonia build telemetry。
- [平台无关的进度契约归属](lessons/L-20260922-02-core-progress-contract.md)：已被 ADR-0001 和 F-20260929-07 取代；当前进度端口由实际消费者 Runtime 拥有。
- [受限环境中的平台 SDK 枚举](lessons/L-20260925-01-platform-sdk-sandbox.md)：全解决方案构建可能只因 Android/Browser/iOS 的 SDK 探测读取用户目录失败；应区分平台环境限制和业务项目编译结果。
- [受限环境中的 Avalonia licensing 枚举](lessons/L-20260926-01-avalonia-license-sandbox.md)：Avalonia BuildServices 即使关闭 telemetry 仍可能读取用户级 licensing tickets；应与业务编译失败区分。
- [首次对话的 Avalonia 模板就绪](lessons/L-20260927-01-dialogue-template-startup.md)：阻塞对话不能因模板部件尚未就绪而静默完成，否则引擎会错误地跑完整个流程。
- [样例资源元数据必须随内容版本化](lessons/L-20260927-02-version-sample-meta.md)：被 `.galgroup` 使用的 `.meta` 映射不可被通用忽略规则吞掉。
- [平台媒体路径必须从资源句柄实体化](lessons/L-20260927-03-asset-handle-materialization.md)：页面不能绕过 `AssetManager` 扫描资源目录；文件路径只能由 acquire 的内容临时生成。
- [粒子端到端桥接验证](lessons/L-20260929-01-particle-presenter-unwired.md)：schema 和平台 presenter 存在不代表剧情 primitive 已可执行；必须验证 Runtime 状态和 presenter 桥接。该问题已在 `F-20260929-01` 解决。
- [持久场景状态必须显式重放展示](lessons/L-20260929-03-persistent-scene-presentation-replay.md)：Runtime 快照恢复不会自动重建渲染端对象；每种持久场景对象都必须覆盖状态到 Presenter 的重放路径。
- [动画存档保存稳定语义而非播放游标](lessons/L-20260929-04-animation-save-semantics.md)：一次性非阻塞动画保存末值，循环动画保存第 0 帧状态与完整定义并从头重播，阻塞动画沿用此前稳定快照。
- [Skia atlas 变换使用 source 局部坐标](lessons/L-20260929-05-skia-atlas-local-transform.md)：`DrawAtlas` 的旋转缩放中心应使用 source rect 的宽高，不能重复带入 atlas 偏移。
- [Sample 调试清空的语义](lessons/L-20260929-01-sample-debug-reset-scope.md)：顶层“清空游戏数据”重置玩家状态；日志清空仅属于日志面板。
- [Sample 调试会话销毁的导航顺序](lessons/L-20260929-02-sample-debug-navigate-before-teardown.md)：先切换到标题页，再停止引擎或释放场景展示，避免重置/重载期间黑屏。
- [格式门禁必须建立在干净基线上](lessons/L-20260929-06-format-gate-clean-baseline.md)：Windows 仓库在统一行尾前直接启用 `dotnet format` 会把既有行尾、using 和空白债务混入功能改动；先做独立机械格式化，再启用阻断式门禁。
- [测试项目不要用独立 MSBuild 进程并行构建共享依赖](lessons/L-20260929-07-parallel-test-shared-obj-lock.md)：多个 `dotnet test` 进程会争用共享生产项目的 `obj` 输出；全仓测试应顺序执行或由单一 MSBuild graph 调度。
- [项目移动后必须清理旧构建中间产物](lessons/L-20260929-08-project-move-stale-restore-path.md)：移动 .NET 项目后先删除随项目移动的 `bin/obj`，再从新路径 locked restore，避免旧绝对路径缓存重建旧目录和 lock 文件。
