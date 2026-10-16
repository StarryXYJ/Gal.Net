# 模块化游戏状态与音频恢复

2026-10-15。实现范围：中央注册式 GameState、模块恢复生命周期、视觉与音频迁移、Sample 新档验证。独立 Editor feature 单独提交，不混入本次变更。

## 结果

- GameSnapshot v4：流程/变量与 StateId → version/JSON payload。StateTable<T> 按稳定 ID 访问；新增状态类型无需改中央快照、深复制或恢复 switch。缺失/未知 section、重复身份、版本与 JSON 载荷错误明确拒绝；旧档不迁移。
- 删除 SceneState、Active* 镜像、BuiltinPresentationReplay、PersistentSceneReplaySession 及动画字符串回放参数。Layer/EffectInstance/ParticleEmitterInstance 直接作为唯一中立记录；动画目标索引引用它们。LoopAnimationState 保存类型化定义，执行控制另存。
- PrimitiveRegistry 注册状态、校验和恢复依赖；StateRestoreSession 管理 continuation 与逆序资源释放。读档验证后换代，恢复成功前阻止推进；失败报告 Faulted。旧任务引用旧根，并核对记录/播放身份。
- 音频轨道与 SEffect 使用同一机制，保存 active/pending、内容 gain、暂停和空闲效果；恢复全部仍存在的轨道，使用当前玩家音量与项目路由。暂停轨道 Resume 前不启动，后续 voice 继承效果；后端 identity/ticket 不入档。
- 更新实际 intro 剧情与两种翻译，增加暂停音频检查点。文件存档 smoke 使用真实作者文件编译，并创建临时新档，不修改旧存档。

## 验证

最终验证结果在 Temp/state-verified/ 的 TRX 与 Temp/state-verified-*.log；本地日志不作为仓库产物提交。Core 111、Runtime 85、Builtins 114、Integration 105、Presentation 191、Architecture 38、Storage.FileSystem 21，共 665 项通过。原生媒体 opt-in 用例未启用，不能据此声称已验证真实媒体播放。

解决方案构建通过（0 错误，13 条分析器/XAML 警告）；CLI 全项目完整校验成功（diagnostics 为空），intro 实际编译 41→41 entries。证据：Temp/state-solution-verified.log、Temp/state-cli-validate.json、Temp/state-cli-compile.json。格式化限定本 feature 相关文件；git diff --check 通过。

## 成本与取舍

统计 HEAD 起本次生产 C#（Shared、Sample Avalonia、AvaloniaAudioSEffects，含未跟踪新源码）：已有文件 +618/-942，新文件 322 行，总计 +940/-942，净减 2 行。不包含测试、文档或并行 Editor 改动。此次同时补充音频存档与通用扩展协议，收益主要是删除重复职责，而非大量减行。

实施复用可变中立记录，未另加镜像 State DTO 和适配器壳；属性变更必须在 State.Update 内同步。通用 JSON codec 在快照/读档边界隔离对象。业务校验仍由模块声明，不能从 CLR 类型自动推出资源存在性或恢复语义。

保存继续使用最后稳定检查点；之后的自然音频结束不反向改写历史快照。恢复从头开始，不实现 seek、淡化时钟、item scope effect 或音频参数动画。原生 LibVLC 与 GUI 人工完整流程未执行。

## 文档

- [Runtime 当前行为](../../docs/spec/runtime.md)
- [扩展开发入口](../../docs/development.md#持久状态与读档扩展)
- [设计与实施决策](../../docs/design/modular-game-state-and-restore.md)
- [ADR-0005](../../docs/adr/0005-modular-persistent-state.md)
- [自审](review.md)
