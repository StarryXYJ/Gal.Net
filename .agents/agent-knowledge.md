# GalNet Agent Knowledge

这是 GalNet 项目的长期 agent 上下文入口。这里只保存经过验证、以后工作仍然有用的项目事实、边界和经验索引；不要写入对当前任务没有复用价值的对话流水账、秘密或未经确认的猜测。

## 使用方式

开始工作时先读取本文件，再按当前任务读取 `G:\program\GalDotNet\.agents\lessons\` 中相关的经验和 `G:\program\GalDotNet\.agents\skills\` 中匹配的 skill。细节以 `G:\program\GalDotNet\docs\` 下的正式文档为准。

经验在发现时立即记录，feature 结束时再整理和晋升；不要等到上下文结束才回忆。

## 项目事实（已从仓库文档确认）

- 解决方案入口是 `GalNet.slnx`。
- `G:\program\GalDotNet\docs\spec\` 描述已经实现的稳定事实；`G:\program\GalDotNet\docs\design\` 描述设计和分阶段计划。
- Runtime/Core 应保持与 Avalonia、WPF、Skia、具体文件系统和其他平台实现解耦。
- 旧的 `GalNet.Control` 与 `GalNet.Control.Abstraction` 已完成迁移并删除；当前共享 Avalonia 游戏页面唯一入口是 `GalNet.Avalonia.GameView`，页面架构见 `docs/spec/control.md`。
- 会影响场景的运行时条目应先更新 `SceneState`，再通知展示层；读取存档后由 Engine 根据状态重放展示。
- 视觉扩展应通过展示抽象和渲染端口接入；不要把平台类型或渲染资源泄漏进 Core/Runtime。
- 当前已有大型阶段计划：[runtime-presentation-decoupling-phase-plan.md](G:\program\GalDotNet\docs\design\runtime-presentation-decoupling-phase-plan.md) 和 [render-effects-pipeline-plan.md](G:\program\GalDotNet\docs\design\render-effects-pipeline-plan.md)。新增 feature 应链接它们，不要复制其中的长期设计。

## 文档分层

- `G:\program\GalDotNet\features\<feature-id>\`：一次实现的需求、设计、阶段计划、审核和人类可读总结。
- `G:\program\GalDotNet\.agents\lessons\`：agent 专用的踩坑记录。
- `G:\program\GalDotNet\.agents\skills\`：项目 skill 的可版本化源文件；可复用经验晋升后在这里形成独立 skill。
- 当前 Codex 的运行时副本位于 `C:\Users\Starry\.codex\skills\galnet-*`；源文件变化后使用 `galnet-sync-skills` 同步，避免 agent 使用旧版本。
- `G:\program\GalDotNet\docs\spec\`：当前系统事实。
- `G:\program\GalDotNet\docs\design\`：跨 feature 设计和推荐工作流。
- `G:\program\GalDotNet\docs\adr\`：重要架构决定；接受后的 ADR 不直接改写。

## 当前工作

- `F-20260926-01-gallery-meta-pages` 的实现、正式文档与示例 Gallery 解锁 smoke test 已完成；2026-09-28 的全量 `GeneralTest` 仍有一项非本 feature 的 `EditorSettingsSerializationTests.LastDockLayout_RoundTripsAsAString` 失败，因此该 feature 维持 `implementation`，等待修复或正式豁免后关闭。
- 当前 discovery feature：[F-20260916-02-audio-system](G:\program\GalDotNet\features\F-20260916-02-audio-system\feature.md)，用于澄清音频系统的首个可交付范围、后端能力和迁移语义。
- 后续 feature、验证命令和新经验在实际工作中补充，并保留来源链接或 feature ID。

## 维护规则

1. 只写已经观察到或从正式文档确认的事实。
2. 事实变化时更新本文件的对应条目，并注明来源。
3. 一次性事故先写 lesson；只有可复用、重复出现或高风险的规则才晋升成独立 skill。
4. 如果项目架构变化，检查并标记受影响的 lesson 和 skill，而不是继续沿用旧规则。

## 经验索引

- [受限环境中的 .NET/Avalonia 构建](lessons/L-20260922-01-dotnet-build-sandbox.md)：禁用 Roslyn shared compilation，并关闭 Avalonia build telemetry。
- [平台无关的进度契约归属](lessons/L-20260922-02-core-progress-contract.md)：被 Core、Runtime 或 Builtins 消费的玩家进度接口必须由 Core 提供，不能仅因某个实现位于存储层就放进 Storage.Abstractions 程序集。
- [受限环境中的平台 SDK 枚举](lessons/L-20260925-01-platform-sdk-sandbox.md)：全解决方案构建可能只因 Android/Browser/iOS 的 SDK 探测读取用户目录失败；应区分平台环境限制和业务项目编译结果。
- [受限环境中的 Avalonia licensing 枚举](lessons/L-20260926-01-avalonia-license-sandbox.md)：Avalonia BuildServices 即使关闭 telemetry 仍可能读取用户级 licensing tickets；应与业务编译失败区分。
- [首次对话的 Avalonia 模板就绪](lessons/L-20260927-01-dialogue-template-startup.md)：阻塞对话不能因模板部件尚未就绪而静默完成，否则引擎会错误地跑完整个流程。
- [样例资源元数据必须随内容版本化](lessons/L-20260927-02-version-sample-meta.md)：被 `.galgroup` 使用的 `.meta` 映射不可被通用忽略规则吞掉。
- [平台媒体路径必须从资源句柄实体化](lessons/L-20260927-03-asset-handle-materialization.md)：页面不能绕过 `AssetManager` 扫描资源目录；文件路径只能由 acquire 的内容临时生成。
